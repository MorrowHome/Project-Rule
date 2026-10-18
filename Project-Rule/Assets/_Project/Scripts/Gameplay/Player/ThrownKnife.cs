using System;
using System.Collections.Generic;
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
    /// <para><b>它做什么</b>：① 自己积分重力（读 <c>Physics2D.gravity</c>，所以「重力 × 反转」
    /// 一行特判都不用写），风场里再叠一层风力（读 <see cref="IWindField"/>，力 ÷ 质量）；
    /// ② Trigger 区（风场等）**穿过去**，是按钮就顺手按下；
    /// ③ 撞到**实心地形**（<c>Ground</c> 层）**停在原地**，等寿命到；
    /// ④ 撞到**场景道具 / 敌人身体 / 受击箱**时**炸成一把纸屑再消失**（<see cref="PaperPlaneBurst"/>）；
    /// ⑤ 什么都没撞到的话，寿命到了才消失。</para>
    ///
    /// <para><b>为什么去掉「碰到就消失」</b>：扫掠掩码是 Everything 且 <c>useTriggers</c> 开着，
    /// 风场（<c>WindZone_Test</c> 这类 Trigger 区）也会被扫到 —— 纸飞机飞进一堆**根本没有实体**的
    /// 区域就没了，那是误判，不是碰撞（2026-10-18 反馈）。现在的规则是：**只有
    /// <see cref="_solidMask"/>（默认 <c>Ground</c> 层）的实心碰撞体挡得住它**，
    /// 其余一切（Trigger 区、场景道具、敌人）都只是「碰了一下」。</para>
    ///
    /// <para><b>为什么不用 Dynamic 刚体</b>：<c>Hurtbox</c> 层在 2D 碰撞矩阵里与所有层都不碰，
    /// 动态刚体永远撞不到受击箱；而且刀要的是「穿过去、只结算一次」，不是弹开。
    /// 所以跟 <c>EnemyProjectile</c> 同一条路线：<b>Kinematic + 自己扫掠</b>（<c>CircleCast</c>）。</para>
    ///
    /// <para><b>身上没有碰撞体</b>：判定全靠扫掠，所以它不会被玩家推、也不会挡住谁。</para>
    ///
    /// <para><b>伤害</b>：只走 <see cref="CombatSystem"/>，传的是**基础伤害** ——
    /// 伤害 / 因果规则由 <c>DamageProcessor</c> 统一结算，这里不自己换算。命中准入
    /// （自己不打自己 / 只打敌对阵营）仍由 <c>CombatSystem.TryResolveTarget</c> 决定。
    /// 同一把刀对**同一个目标只结算一次**（去重集合），否则穿过一次躯干会连打十几下。</para>
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
        [Tooltip("飞行寿命（秒）。**只有它到了才消失** —— 撞到东西不会让刀消失。")]
        [SerializeField] private float _lifetime = 6f;

        [Tooltip("自己的重力倍率。乘在 Physics2D.gravity 上 —— 与铁箱 / 石头一样，\n" +
                 "重力规则一翻它就跟着翻，不需要任何特判。")]
        [SerializeField, Range(0f, 3f)] private float _gravityScale = 1f;

        [Header("风")]
        [Tooltip("风场所在的层（Wind）。0 = 这架纸飞机不响应风。")]
        [SerializeField] private LayerMask _windLayer = 1 << 11;

        [Header("碰撞")]
        [Tooltip("扫掠半径（米）。按纸飞机的实际轮廓给，不要按整张贴图给。")]
        [SerializeField] private float _radius = 0.12f;

        [Tooltip("扫掠扫哪些层。默认 Everything —— 扫到不等于会消失，只是「碰了一下」。")]
        [SerializeField] private LayerMask _hitMask = ~0;

        [Tooltip("**唯一挡得住它的层**（默认 Ground）。只有这一层的实心碰撞体会让它停在原地；\n" +
                 "Trigger 区（风场等）与其它层一律穿过去。")]
        [SerializeField] private LayerMask _solidMask = 1 << 8;   // Ground

        [Header("命中")]
        [Tooltip("碰到接触式开关（CitySwitch / 摇杆）时按下去，然后继续飞。")]
        [SerializeField] private bool _pressSwitches = true;

        [Tooltip("撞到场景道具 / 敌人身体时**炸成一把纸屑再消失**。关掉就只是原地消失。")]
        [SerializeField] private bool _shatter = true;

        [Tooltip("纸屑数量。0 = 不炸。")]
        [SerializeField, Range(0, 12)] private int _scrapCount = PaperPlaneBurst.DefaultCount;

        [Header("伤害（5.2 投掷物扣血公式）")]
        [Tooltip("D_raw = 本值 × 质量 × 速度档位（速度档位见 SpeedScaleFor）。\n" +
                 "全局伤害倍率由伤害规则（DamageProcessor）乘，**不要**在这里再乘一次。")]
        [SerializeField] private float _damageCoefficient = DamageCoefficient;

        [Tooltip("速度档位的上界（米/秒）：≤第 1 项 → 0 档、≤第 2 项 → 1 档……超过最后一项仍算最后一档。\n" +
                 "默认 8 / 12 / 16 / 20，对齐 16 m/s 的出手速度（原来的 4 / 6 / 8 / 10 是按 8 m/s 的投掷物配的）。")]
        [SerializeField] private float[] _speedThresholds = { 8f, 12f, 16f, 20f };

        [Header("表现")]
        [Tooltip("跟着速度方向转的视觉（纸飞机要机头朝前）。留空取第一个子物体。")]
        [SerializeField] private Transform _visual;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private SpriteRenderer _renderer;
        private Transform _owner;
        private Collider2D[] _ownerColliders;
        private CombatSystem _combat;
        private object _attacker;
        private Vector2 _velocity;
        private float _knockback;
        private float _age;
        private bool _spent;
        private bool _launched;
        private bool _stuck;

        private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
        private readonly Collider2D[] _windHits = new Collider2D[8];

        /// <summary>本物理帧的风加速度（力 ÷ 质量）；不在风场里是零。</summary>
        public Vector2 WindAcceleration { get; private set; }

        /// <summary>本物理帧的空气阻力系数（每秒衰减率）。</summary>
        public float WindDrag { get; private set; }

        /// <summary>这一帧是不是在风场里。</summary>
        public bool InWind => WindAcceleration.sqrMagnitude > 0.0001f || WindDrag > 0f;

        /// <summary>已经结算过伤害的目标：同一把刀对同一实体只打一次。</summary>
        private readonly HashSet<IDamageable> _damaged = new HashSet<IDamageable>();

        /// <summary>消失（寿命到）时广播一次，供表现层接特效。</summary>
        public event Action<ThrownKnife> OnSpent;

        public Vector2 Velocity => _velocity;
        public float   Age      => _age;
        public bool    Spent    => _spent;

        /// <summary>撞上实心地形后停在原地了（还在等寿命结束，没有消失）。</summary>
        public bool Stuck => _stuck;

        /// <summary>最后撞到的**实心地形**碰撞体（停下时记一次，供验证与调试）。</summary>
        public Collider2D LastHit { get; private set; }

        // ==================================================================
        //  伤害公式（5.2 投掷物扣血公式）
        // ==================================================================

        /// <summary>公式里的固定系数：<c>D_raw = 5 × 质量 × 速度档位</c>。</summary>
        public const float DamageCoefficient = 5f;

        /// <summary>速度档位的默认上界：≤8 → 0 档、≤12 → 1、≤16 → 2、≤20 → 3，再快仍算 3。</summary>
        public static readonly float[] DefaultSpeedThresholds = { 8f, 12f, 16f, 20f };

        /// <summary>
        /// 速度档位：拿着档位表逐段比，返回第几档（0 起）。
        /// </summary>
        /// <remarks>
        /// <para>区间按设计稿原样**右闭**（<c>≤上界</c> 归本档）。档位表默认 8 / 12 / 16 / 20：
        /// 原来的 4 / 6 / 8 / 10 是按 8 m/s 的投掷物配的，飞刀出手就是 16 m/s，所以整体放大一倍。</para>
        /// <para>超过最后一个上界时**仍按最后一档**（不会越界成第 4 档）。</para>
        /// </remarks>
        public static int SpeedScaleFor(float speed, float[] thresholds = null)
        {
            thresholds = thresholds != null && thresholds.Length > 0 ? thresholds : DefaultSpeedThresholds;

            for (int i = 0; i < thresholds.Length; i++)
                if (speed <= thresholds[i]) return i;

            return thresholds.Length - 1;
        }

        /// <summary>原始伤害 <c>D_raw = 系数 × 质量 × 速度档位</c>（还没过伤害规则、没减防）。</summary>
        public static float RawDamageFor(float mass, float speed, float coefficient = DamageCoefficient,
                                         float[] thresholds = null)
            => coefficient * Mathf.Max(mass, 0.01f) * SpeedScaleFor(speed, thresholds);

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
            if (_visual != null) _renderer = _visual.GetComponent<SpriteRenderer>();

            // Inspector 里把档位表清空了就退回默认，免得分档直接失效（全 0 档 = 一点伤害都没有）。
            if (_speedThresholds == null || _speedThresholds.Length == 0)
                _speedThresholds = DefaultSpeedThresholds;
        }

        /// <summary>
        /// 出手。投掷者只管「从哪、朝哪、多快」，飞行与消失归本组件；
        /// **打多少由本组件按 5.2 公式现算**（质量 × 速度档位），投掷者不再传伤害。
        /// </summary>
        /// <param name="attacker">投掷者（玩家 / 敌人）。它的所有碰撞体都会被扫掠跳过。</param>
        /// <param name="combat">命中后结算伤害用的战斗系统。</param>
        /// <param name="direction">飞行方向（会被归一化）。</param>
        /// <param name="speed">初速度（米/秒）。</param>
        /// <param name="knockback">击退数值。</param>
        public void Launch(Component attacker, CombatSystem combat, Vector2 direction, float speed,
                           float knockback)
        {
            _attacker       = attacker;
            _owner          = attacker != null ? attacker.transform : null;
            _ownerColliders = attacker != null ? attacker.GetComponentsInChildren<Collider2D>(true) : null;
            _combat         = combat;
            _knockback      = knockback;
            _velocity       = direction.sqrMagnitude > 0.0001f ? direction.normalized * speed : Vector2.right * speed;
            _age            = 0f;
            _spent          = false;
            _stuck          = false;
            _launched       = true;
            _damaged.Clear();
            LastHit         = null;

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

        /// <summary>推进一步。返回 false 表示这一步寿命到了、已经消失。</summary>
        public bool Step(float dt)
        {
            if (!_launched || _spent) return false;

            _age += dt;
            if (_age >= _lifetime) { Spend(); return false; }

            // 已经停在墙上 / 地上：只剩寿命在走。
            if (_stuck) return true;

            _velocity += Physics2D.gravity * _gravityScale * dt;

            // 风：与玩家 WindSensor 同一套算法 —— 报的是**力**，这里除以自己的质量变成加速度，
            // 阻力再按「每秒衰减率」扣一次。质量规则因此直接改变纸飞机被吹成什么样。
            _velocity += SenseWind() * dt;
            _velocity -= _velocity * Mathf.Clamp01(WindDrag * dt);

            float distance = _velocity.magnitude * dt;
            if (distance <= 0f) return true;

            Vector2 direction = _velocity / _velocity.magnitude;

            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(_hitMask);

            int count = Physics2D.CircleCast(_body.position, _radius, direction, filter, _hits, distance);

            float travel = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = _hits[i];
                Collider2D other = hit.collider;
                if (IsThrowerCollider(other)) continue;

                if (Blocks(other))
                {
                    // 实心地形：停在接触点，**不消失** —— 照样等寿命。
                    LastHit = other;
                    travel = Mathf.Min(travel, Mathf.Max(0f, hit.distance - 0.01f));
                    continue;
                }

                // 受击箱：结算伤害，然后**炸成纸屑消失**。
                if (other.gameObject.layer == Hurtbox.Layer)
                {
                    DamageThroughHurtbox(other);
                    Shatter(hit.point, direction);
                    return false;
                }

                // 非 Trigger 的实体（场景道具 / 敌人身体）：同样炸掉，消失。
                if (!other.isTrigger)
                {
                    DamageThroughBody(other);
                    Shatter(hit.point, direction);
                    return false;
                }

                // Trigger 区（风场等）穿过去；是按钮就顺手按下。
                PressSwitch(other);
            }

            _body.MovePosition(_body.position + direction * travel);

            if (travel < distance)
            {
                _stuck    = true;
                _velocity = Vector2.zero;
            }
            else
            {
                Face();
            }

            return true;
        }

        // ==================================================================
        //  接触
        // ==================================================================

        /// <summary>
        /// 查一次「我在哪个风场里、风有多大」。
        /// </summary>
        /// <remarks>
        /// <para>与 <c>WindSensor</c> 同一条通道：风场是 <b>Trigger</b>，而且飞刀身上没有碰撞体，
        /// 所以只能自己用 <c>OverlapPoint</c> 查 <c>Wind</c> 层 —— 不能等触发回调。</para>
        /// <para>风场报的是**力**（牛顿），这里除以 <c>Rigidbody2D.mass</c> 才是加速度。
        /// 质量由 <c>RuleAffectedBody</c> 按质量规则写进刚体，于是「质量 × 削弱 → 被吹得更飘」自动成立。</para>
        /// </remarks>
        private Vector2 SenseWind()
        {
            WindAcceleration = Vector2.zero;
            WindDrag = 0f;

            if (_windLayer.value == 0) return WindAcceleration;

            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(_windLayer);

            Vector2 point = _body.position;
            int count = Physics2D.OverlapPoint(point, filter, _windHits);

            Vector2 force = Vector2.zero;
            float drag = 0f;
            for (int i = 0; i < count; i++)
            {
                var field = _windHits[i].GetComponentInParent<IWindField>();
                if (field == null) continue;

                force += field.ForceAt(point);
                drag  += field.DragAt(point);
            }

            if (force.sqrMagnitude > 0.0001f)
                WindAcceleration = force / Mathf.Max(_body.mass, 0.01f);   // 与 RuleAffectedBody 的 MinMass 一致

            WindDrag = drag;
            return WindAcceleration;
        }

        /// <summary>只有实心地形挡得住它。Trigger（风场 / 按钮 / 受击箱）一律不挡。</summary>
        private bool Blocks(Collider2D other)
        {
            if (other == null || other.isTrigger) return false;
            return (_solidMask & (1 << other.gameObject.layer)) != 0;
        }

        /// <summary>Trigger 区穿过去时唯一会做的事：是接触式开关就按下（石头 / 铁箱走的就是这一条）。</summary>
        private void PressSwitch(Collider2D other)
        {
            if (!_pressSwitches || other == null) return;

            var target = other.GetComponentInParent<IContactSwitch>();
            if (target != null) target.PressByContact();
        }

        /// <summary>撞碎了：在撞击点炸一把纸屑，然后自己消失。</summary>
        private void Shatter(Vector2 point, Vector2 direction)
        {
            if (_shatter && _scrapCount > 0 && _renderer != null)
            {
                PaperPlaneBurst.Spawn(point, direction, _renderer.sprite, _renderer.sharedMaterial,
                                      _renderer.color, _scrapCount);
            }

            Spend();
        }

        /// <summary>受击箱：命中判定的唯一入口，伤害传基础值，结算归 DamageProcessor。</summary>
        private void DamageThroughHurtbox(Collider2D hurtbox)
        {
            if (_combat == null) return;
            if (!CombatSystem.TryResolveTarget(hurtbox, _attacker, out IDamageable receiver)) return;

            ApplyOnce(receiver);
        }

        /// <summary>
        /// 敌人身体 / 场景道具这类**非受击箱**的碰撞体。
        /// </summary>
        /// <remarks>
        /// 身体的碰撞体在受击箱**外面**，扫掠通常先碰到它 —— 只在这里判「该碎了」而不结算伤害的话，
        /// 一把刀撞在敌人身上会一个伤害都不出。所以这里按父链找到同一个 <see cref="IDamageable"/>，
        /// 走 <see cref="CombatSystem.CanHit"/> + <see cref="CombatSystem.ApplyDamage"/> 的正常出口；
        /// 同一个目标仍然只结算一次（与受击箱那条路共用一个去重集合）。
        /// </remarks>
        private void DamageThroughBody(Collider2D body)
        {
            if (_combat == null) return;

            IDamageable owner = body.GetComponentInParent<IDamageable>();
            if (owner == null || !CombatSystem.CanHit(_attacker, owner)) return;

            ApplyOnce(owner);
        }

        private void ApplyOnce(IDamageable receiver)
        {
            if (!_damaged.Add(receiver)) return;

            // 5.2：D_raw = 5 × 质量 × 速度档位（**全局伤害倍率**由 DamageProcessor 按伤害规则乘，
            // 这里绝不能先乘一遍，否则规则会被算两次）。减防与四舍五入在承受方（EntityDamageable）。
            float raw = RawDamageFor(_body.mass, _velocity.magnitude, _damageCoefficient, _speedThresholds);

            _combat.ApplyDamage(_attacker, receiver, raw, _knockback);
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
