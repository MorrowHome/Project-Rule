using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Presentation.Player
{
    /// <summary>
    /// 把 <see cref="PlayerController"/> 的状态翻译成 Animator 参数。
    /// </summary>
    /// <remarks>
    /// <para><b>这一层只发「意图」，不发「状态」</b>。过渡条件、混合、播完再切这些事
    /// 全部由 <c>Hero.controller</c> 自己管 —— 这里只把玩法状态映射成参数，
    /// 然后 <c>SetFloat</c> / <c>SetBool</c> / <c>SetTrigger</c> 就结束了。
    /// 这正是《丝之歌》那套分工：<b>C# 当决策者、Animator 当执行者</b>。</para>
    ///
    /// <para><b>为什么不再 <c>CrossFade(状态名)</c></b>：那样等于绕开 Animator 自己的状态机，
    /// 于是拿不到 <c>Has Exit Time</c>（播完再走）、<c>AnyState</c>（谁能打断谁）、
    /// 过渡时长这些引擎免费给的东西 —— 之前那几个「动作冻在最后一帧」「落地没动画」
    /// 都是这么来的。参数驱动之后，这些改在图的配置里，不用写代码。</para>
    ///
    /// <para><b>两类参数，来源不同</b>：</para>
    /// <list type="bullet">
    /// <item><b>持续条件</b>（Speed / Grounded / Dashing / WallSliding / GravityFlipped / Rising）
    /// —— 每帧重写。它们描述「现在是什么」，重写不会丢。</item>
    /// <item><b>一次性事件</b>（Jump / WallJump / Land / Hurt / Die）—— 从玩法层的
    /// <b>边沿事件</b>发过来。它们描述「刚刚发生了什么」，靠每帧采样会漏
    /// （0.33 秒的落地只有 4 个物理帧），所以直接从源头发 <c>SetTrigger</c>。</item>
    /// </list>
    ///
    /// <para>玩法层不认识本类 —— 依赖方向是 Presentation → Gameplay，单向。</para>
    /// </remarks>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimationDriver : MonoBehaviour
    {
        // ==================================================================
        //  Animator 参数名
        // ==================================================================
        //
        // ⚠️ 这些常量是**唯一真相**：编辑器里的 HeroAnimatorBuilder 直接引用它们来建参数，
        //    所以改名只需要改这一处，图和代码不会各说各话。

        public const string ParamSpeed          = "Speed";
        public const string ParamGrounded       = "Grounded";
        public const string ParamDashing        = "Dashing";
        public const string ParamWallSliding    = "WallSliding";
        public const string ParamGravityFlipped = "GravityFlipped";
        public const string ParamRising         = "Rising";

        public const string ParamJump     = "Jump";
        public const string ParamWallJump = "WallJump";
        public const string ParamLand     = "Land";
        public const string ParamHurt     = "Hurt";
        public const string ParamDie      = "Die";

        private static readonly int HSpeed          = Animator.StringToHash(ParamSpeed);
        private static readonly int HGrounded       = Animator.StringToHash(ParamGrounded);
        private static readonly int HDashing        = Animator.StringToHash(ParamDashing);
        private static readonly int HWallSliding    = Animator.StringToHash(ParamWallSliding);
        private static readonly int HGravityFlipped = Animator.StringToHash(ParamGravityFlipped);
        private static readonly int HRising         = Animator.StringToHash(ParamRising);

        private static readonly int HJump     = Animator.StringToHash(ParamJump);
        private static readonly int HWallJump = Animator.StringToHash(ParamWallJump);
        private static readonly int HLand     = Animator.StringToHash(ParamLand);
        private static readonly int HHurt     = Animator.StringToHash(ParamHurt);
        private static readonly int HDie      = Animator.StringToHash(ParamDie);

        // ==================================================================
        //  序列化
        // ==================================================================

        [Header("引用（留空自动找）")]
        [SerializeField] private PlayerController _player;
        [SerializeField] private Animator         _animator;
        [SerializeField] private SpriteRenderer   _renderer;
        [SerializeField] private EntityStatus     _status;

        // ==================================================================
        //  运行时
        // ==================================================================

        private float _lastHealth;
        private bool  _warnedNoAnimator;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_player   == null) _player   = GetComponent<PlayerController>();
            if (_status   == null) _status   = GetComponent<EntityStatus>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void OnEnable()
        {
            if (_player != null)
            {
                _player.OnJumped     += HandleJumped;
                _player.OnWallJumped += HandleWallJumped;
                _player.OnLanded     += HandleLanded;
            }

            if (_status != null)
            {
                _status.OnHealthChanged += HandleHealthChanged;
                _lastHealth = _status.Health;      // 起手对齐，否则第一帧会被当成「掉血」
            }
        }

        private void OnDisable()
        {
            if (_player != null)
            {
                _player.OnJumped     -= HandleJumped;
                _player.OnWallJumped -= HandleWallJumped;
                _player.OnLanded     -= HandleLanded;
            }

            if (_status != null) _status.OnHealthChanged -= HandleHealthChanged;
        }

        private void Update()
        {
            if (_animator == null)
            {
                if (!_warnedNoAnimator)
                {
                    _warnedNoAnimator = true;
                    Debug.LogWarning("[PlayerAnimationDriver] 找不到 Animator，动画不会播。", this);
                }
                return;
            }

            // 暂停：动画要一起冻住。D18 用 Physics2D.simulationMode 停物理、**不改** timeScale，
            // 所以这里必须显式停，否则拼装界面一开角色还在原地跑。
            _animator.speed = (_status != null && _status.IsPaused) ? 0f : 1f;

            // 朝向：Facing 已经是「换算后」的方向，「摩擦 × 反转」时自动是反的 —— 不用另外检测。
            if (_renderer != null) _renderer.flipX = _player.Facing < 0f;

            PushContinuousParameters();
        }

        // ==================================================================
        //  持续条件（每帧重写）
        // ==================================================================

        private void PushContinuousParameters()
        {
            Vector2 velocity = _player.Velocity;

            // 地面 Blend 用的水平速度。**不做归一化** —— 阈值直接写「米/秒」更好读，
            // 也和 PlayerController 里 _moveSpeedMax 是同一个量纲。
            _animator.SetFloat(HSpeed, Mathf.Abs(velocity.x));

            _animator.SetBool(HGrounded, _player.IsGrounded);
            _animator.SetBool(HDashing,  _player.IsDashing);
            _animator.SetBool(HWallSliding, _player.IsWallSliding);
            _animator.SetBool(HGravityFlipped, Physics2D.gravity.y > 0f);
            _animator.SetBool(HRising, Vector2.Dot(velocity, Up) > 0.1f);
        }

        /// <summary>当前「上」方向 = 重力反方向；重力失效（零向量）时退化为世界上方。</summary>
        private static Vector2 Up
            => Physics2D.gravity.sqrMagnitude > 0.0001f ? -Physics2D.gravity.normalized : Vector2.up;

        // ==================================================================
        //  一次性事件
        // ==================================================================

        private void HandleJumped()     => SetTrigger(HJump);
        private void HandleWallJumped() => SetTrigger(HWallJump);
        private void HandleLanded()     => SetTrigger(HLand);

        /// <summary>掉血 → 受伤；掉到 0 → 死亡。回血不触发任何东西。</summary>
        private void HandleHealthChanged(float current, float max)
        {
            if (current <= 0f)
            {
                SetTrigger(HDie);
            }
            else if (current < _lastHealth)
            {
                SetTrigger(HHurt);
            }

            _lastHealth = current;
        }

        private void SetTrigger(int hash)
        {
            // 事件可能在 Awake 之前到（同物体上的组件顺序不保证），也不能在没 Animator 时炸。
            if (_animator != null) _animator.SetTrigger(hash);
        }
    }
}
