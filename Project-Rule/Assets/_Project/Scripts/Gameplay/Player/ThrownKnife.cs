using System;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 玩家投出去的飞刀（白盒外观是一架纸飞机）。
    /// </summary>
    /// <remarks>
    /// <para><b>三件事</b>：① 自己积分重力（读 <c>Physics2D.gravity</c>，所以「重力 × 反转」
    /// 一行特判都不用写）；② **撞到任何东西就消失**（扫掠半径碰到的第一个非投掷者碰撞体）；
    /// ③ 撞到接触式开关（<see cref="IContactSwitch"/>）顺手按下去 —— 与石头 / 铁箱走同一条路。</para>
    ///
    /// <para><b>为什么不用 Dynamic 刚体</b>：<c>Hurtbox</c> 层在 2D 碰撞矩阵里与所有层都不碰，
    /// 动态刚体永远撞不到受击箱；而且刀要的是「碰到即消失」，不是弹开。
    /// 所以跟 <c>EnemyProjectile</c> 同一条路线：<b>Kinematic + 自己扫掠</b>（<c>CircleCast</c>）。</para>
    ///
    /// <para><b>身上没有碰撞体</b>：判定全靠扫掠，所以它不会被玩家推、也不会挡住谁。
    /// 查询用 <c>useTriggers = true</c>（受击箱、按钮都是 Trigger）。</para>
    ///
    /// <para><b>伤害</b>：只走 <see cref="CombatSystem"/>，传的是**基础伤害** ——
    /// 伤害 / 因果规则由 <c>DamageProcessor</c> 统一结算，这里不自己换算。命中准入
    /// （自己不打自己 / 只打敌对阵营）仍由 <c>CombatSystem.TryResolveTarget</c> 决定。</para>
    ///
    /// <para>⚠️ 原型：没有接世界快照（<c>ISnapshotParticipant</c>），时间回溯不会清掉在飞的刀。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class ThrownKnife : MonoBehaviour
    {
        // ==================================================================
        //  配置
        // ==================================================================

        [Header("飞行")]
        [Tooltip("飞行寿命（秒）。超时自己消失，防止漏网的刀永远飞下去。")]
        [SerializeField] private float _lifetime = 6f;

        [Tooltip("自己的重力倍率。乘在 Physics2D.gravity 上 —— 与铁箱 / 石头一样，\n" +
                 "重力规则一翻它就跟着翻，不需要任何特判。")]
        [SerializeField, Range(0f, 3f)] private float _gravityScale = 1f;

        [Header("碰撞")]
        [Tooltip("扫掠半径（米）。按纸飞机的实际轮廓给，不要按整张贴图给。")]
        [SerializeField] private float _radius = 0.12f;

        [Tooltip("扫掠撞哪些层。默认 Everything：地面、场景道具、敌人、按钮都算「碰到就消失」。\n" +
                 "投掷者自己的碰撞体永远跳过，不受这个掩码影响。")]
        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("命中")]
        [Tooltip("撞到接触式开关（CitySwitch / 摇杆）时按下去。")]
        [SerializeField] private bool _pressSwitches = true;

        [Header("表现")]
        [Tooltip("跟着速度方向转的视觉（纸飞机要机头朝前）。留空取第一个子物体。")]
        [SerializeField] private Transform _visual;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private Transform _owner;
        private Collider2D[] _ownerColliders;
        private CombatSystem _combat;
        private object _attacker;
        private Vector2 _velocity;
        private float _damage;
        private float _knockback;
        private float _age;
        private bool _spent;
        private bool _launched;

        private readonly RaycastHit2D[] _hits = new RaycastHit2D[12];

        /// <summary>消失（撞到东西 / 超时）时广播一次，供表现层接特效。</summary>
        public event Action<ThrownKnife> OnSpent;

        public Vector2 Velocity => _velocity;
        public float   Age      => _age;
        public bool    Spent    => _spent;

        /// <summary>撞到的那个碰撞体（隐藏/消失前可读，供验证与调试）。</summary>
        public Collider2D LastHit { get; private set; }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            // 位移完全由本组件扫掠后 MovePosition 写入 —— 引擎不插一脚。
            _body.bodyType      = RigidbodyType2D.Kinematic;
            _body.gravityScale  = 0f;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;

            if (_visual == null && transform.childCount > 0) _visual = transform.GetChild(0);
        }

        /// <summary>
        /// 出手。投掷者只管「从哪、朝哪、多快、打多少」，飞行与消失归本组件。
        /// </summary>
        /// <param name="attacker">投掷者（玩家 / 敌人）。它的所有碰撞体都会被扫掠跳过。</param>
        /// <param name="combat">命中后结算伤害用的战斗系统。</param>
        /// <param name="direction">飞行方向（会被归一化）。</param>
        /// <param name="speed">初速度（米/秒）。</param>
        /// <param name="damage">**基础伤害** —— 会过 DamageProcessor。</param>
        /// <param name="knockback">击退数值。</param>
        public void Launch(Component attacker, CombatSystem combat, Vector2 direction, float speed,
                           float damage, float knockback)
        {
            _attacker       = attacker;
            _owner          = attacker != null ? attacker.transform : null;
            _ownerColliders = attacker != null ? attacker.GetComponentsInChildren<Collider2D>(true) : null;
            _combat         = combat;
            _damage         = damage;
            _knockback      = knockback;
            _velocity       = direction.sqrMagnitude > 0.0001f ? direction.normalized * speed : Vector2.right * speed;
            _age            = 0f;
            _spent          = false;
            _launched       = true;

            // ⚠️ 物理体与 Transform 对齐：工程的 Physics2D.autoSyncTransforms 是关着的，
            //    只写 transform.position 的话刚体还在出生点（预制体原点 = 0,0），
            //    第一次扫掠就会从世界原点出发 —— 实测会「隔着半个关卡撞到按钮」。
            _body.position = transform.position;

            Face();
        }

        private void FixedUpdate()
        {
            if (!_launched || _spent) return;
            if (TimeManager.Instance?.IsPaused == true) return;      // 暂停：不推进、不老死

            Step(Time.fixedDeltaTime);
        }

        // ==================================================================
        //  一步飞行（验证工具直接调它，与 FixedUpdate 是同一段代码）
        // ==================================================================

        /// <summary>推进一步。返回 false 表示这一步撞掉了。</summary>
        public bool Step(float dt)
        {
            if (!_launched || _spent) return false;

            _age += dt;
            if (_age >= _lifetime) { Spend(); return false; }

            _velocity += Physics2D.gravity * _gravityScale * dt;

            float distance = _velocity.magnitude * dt;
            if (distance <= 0f) return true;

            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(_hitMask);

            int count = Physics2D.CircleCast(_body.position, _radius, _velocity / _velocity.magnitude,
                                             filter, _hits, distance);

            RaycastHit2D nearest = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (IsThrowerCollider(_hits[i].collider)) continue;
                if (!found || _hits[i].distance < nearest.distance) { nearest = _hits[i]; found = true; }
            }

            if (found) { Strike(nearest); return false; }

            _body.MovePosition(_body.position + _velocity * dt);
            Face();
            return true;
        }

        // ==================================================================
        //  命中
        // ==================================================================

        private void Strike(RaycastHit2D hit)
        {
            Collider2D other = hit.collider;
            LastHit = other;

            // ① 接触式开关：石头 / 铁箱按按钮走的就是这一条。
            if (_pressSwitches && other != null)
            {
                var target = other.GetComponentInParent<IContactSwitch>();
                if (target != null) target.PressByContact();
            }

            // ② 受击箱：命中判定的唯一入口。伤害传基础值，结算归 DamageProcessor。
            if (_combat != null && other != null && other.gameObject.layer == Hurtbox.Layer
                && CombatSystem.TryResolveTarget(other, _attacker, out IDamageable receiver))
            {
                _combat.ApplyDamage(_attacker, receiver, _damage, _knockback);
            }

            Spend();
        }

        private void Spend()
        {
            if (_spent) return;

            _spent = true;
            OnSpent?.Invoke(this);

            // 编辑期自检会直接调 Step()：那个场合 Destroy 会报错，用 DestroyImmediate。
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        private bool IsThrowerCollider(Collider2D other)
        {
            if (other == null) return true;

            if (_ownerColliders != null)
            {
                for (int i = 0; i < _ownerColliders.Length; i++)
                    if (_ownerColliders[i] == other) return true;
            }

            return _owner != null && other.transform.IsChildOf(_owner);
        }

        // ==================================================================
        //  表现
        // ==================================================================

        /// <summary>机头永远朝速度方向（纸飞机的「飞」感全靠这一下）。</summary>
        private void Face()
        {
            if (_visual == null || _velocity.sqrMagnitude < 0.0001f) return;

            float angle = Mathf.Atan2(_velocity.y, _velocity.x) * Mathf.Rad2Deg;
            _visual.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
