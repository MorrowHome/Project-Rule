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

        /// <summary>是否按着左右方向键。`Idle ⇄ Run` 用它判定，**不用速度**。</summary>
        public const string ParamMoving         = "Moving";

        public const string ParamGrounded       = "Grounded";
        public const string ParamDashing        = "Dashing";
        public const string ParamWallSliding    = "WallSliding";
        public const string ParamGravityFlipped = "GravityFlipped";
        public const string ParamRising         = "Rising";

        public const string ParamJump     = "Jump";

        /// <summary>二段跳（空中段）。与 <see cref="ParamJump"/> 分开：起跳和空中那一下是两个动作。</summary>
        public const string ParamAirJump  = "AirJump";

        public const string ParamWallJump = "WallJump";
        public const string ParamLand     = "Land";
        public const string ParamHurt     = "Hurt";
        public const string ParamDie      = "Die";

        /// <summary>起手一刀（连段的第一下）。</summary>
        public const string ParamAttack     = "Attack";

        /// <summary>连段的下一刀（收招前再按一次）。</summary>
        public const string ParamAttackNext = "AttackNext";

        private static readonly int HSpeed          = Animator.StringToHash(ParamSpeed);
        private static readonly int HMoving         = Animator.StringToHash(ParamMoving);
        private static readonly int HGrounded       = Animator.StringToHash(ParamGrounded);
        private static readonly int HDashing        = Animator.StringToHash(ParamDashing);
        private static readonly int HWallSliding    = Animator.StringToHash(ParamWallSliding);
        private static readonly int HGravityFlipped = Animator.StringToHash(ParamGravityFlipped);
        private static readonly int HRising         = Animator.StringToHash(ParamRising);

        private static readonly int HJump         = Animator.StringToHash(ParamJump);
        private static readonly int HAirJump      = Animator.StringToHash(ParamAirJump);
        private static readonly int HWallJump     = Animator.StringToHash(ParamWallJump);
        private static readonly int HLand         = Animator.StringToHash(ParamLand);
        private static readonly int HHurt         = Animator.StringToHash(ParamHurt);
        private static readonly int HDie          = Animator.StringToHash(ParamDie);
        private static readonly int HAttack       = Animator.StringToHash(ParamAttack);
        private static readonly int HAttackNext   = Animator.StringToHash(ParamAttackNext);

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
                _player.OnAirJumped  += HandleAirJumped;
                _player.OnWallJumped += HandleWallJumped;
                _player.OnLanded     += HandleLanded;
                _player.OnAttacked   += HandleAttacked;
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
                _player.OnAirJumped  -= HandleAirJumped;
                _player.OnWallJumped -= HandleWallJumped;
                _player.OnLanded     -= HandleLanded;
                _player.OnAttacked   -= HandleAttacked;
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

            PushContinuousParameters();
        }

        /// <summary>
        /// 朝向**必须放在 LateUpdate**，不能和上面的参数一起写在 <c>Update</c> 里。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么</b>：Animator 的精灵换帧曲线绑定的是**同一个</b> <see cref="SpriteRenderer"/>。
        /// 动画求值发生在 <c>Update</c> 之后，它会把整个绑定的渲染器属性集写回默认值 ——
        /// 于是 <c>Update</c> 里刚设好的 <c>flipX</c> 被同一帧覆盖回 <c>false</c>，
        /// 表现就是「往左走不翻面」（实测：把 Animator 停掉，手动写的 flipX 就能保持住）。
        /// 放到 <c>LateUpdate</c> 就落在动画写入之后，不会再被吃掉。</para>
        ///
        /// <para>⚠️ 不要去改成「用 <c>localScale.x = -1</c> 翻面」：<c>Hero_Run</c> / <c>Hero_Airborne</c>
        /// / <c>Hero_Die</c> 三个剪辑都在写 <c>localScale</c>，同样会被动画覆盖。</para>
        ///
        /// <para>⚠️ 素材原生朝右，所以朝左（<c>Facing &lt; 0</c>）时才翻。</para>
        ///
        /// <para><c>Facing</c> 已经是「换算后」的方向，「摩擦 × 反转」时自动是反的 —— 不用另外检测。</para>
        /// </remarks>
        private void LateUpdate()
        {
            if (_renderer == null || _player == null) return;

            int state = _animator != null ? _animator.GetCurrentAnimatorStateInfo(0).shortNameHash : 0;

            // 「朝左就翻」再叠一层「这个状态在图集里本身是反的」——两者异或。
            // 见 MirroredStateNames 上面那段说明。
            _renderer.flipX = (_player.Facing < 0f) ^ IsMirroredState(state);
        }

        // ==================================================================
        //  图集镜像补偿
        // ==================================================================
        //
        // ⚠️ 这是一块**临时补丁**，为了配合 Hero_Animation_Sheet_Transparent_UnityGrid 那张图集。
        //
        // 背景：21 个剪辑都切到了那张图集，但图集里有 17 个序列**被镜像了**（实测：拿每一格和
        // 对应的单张 Hero_*.png 比像素，这些序列「镜像后的差」远小于「原样的差」）。剩下
        // Idle / Airborne / JumpStart / Land 是正的。同一张图一半正一半反，所以只在代码里
        // 统一翻一次是修不好的 —— 那会把正的那 4 个弄反，**必须逐状态判断**。
        //
        // 为什么放在 flipX（布尔）而不是 localScale.x：布尔镜像切换是**一帧硬翻**，2D 里正常；
        // 用负 scale 则会在 Idle(+1) ↔ Run(−1) 交叉淡入时**从 −1 插值经过 0**，
        // 人被拍扁再翻过去 —— 就是那个「纸片人翻转」。
        //
        // 📌 什么时候能删掉它：图集重新导出成**全部朝右**之后，把 Mirror 表清空即可
        //    （留空数组 = 退化成纯 `Facing < 0`）。**换素材时先看这张表还对不对。**

        /// <summary>图集里被镜像、需要额外翻一次的状态名。</summary>
        private static readonly string[] MirroredStateNames =
        {
            "Run", "Die", "DrawSword", "Hurt", "OrbHold", "PickupBoth", "PickupOne",
            "PullOut", "Push", "PutBack", "Sheathe", "Slash1", "Slash2", "SlashDown",
            "Throw", "WallJump", "Weightless",
        };

        private static int[] _mirroredHashes;

        private static bool IsMirroredState(int stateHash)
        {
            if (_mirroredHashes == null)
            {
                _mirroredHashes = new int[MirroredStateNames.Length];
                for (int i = 0; i < MirroredStateNames.Length; i++)
                    _mirroredHashes[i] = Animator.StringToHash(MirroredStateNames[i]);
            }

            for (int i = 0; i < _mirroredHashes.Length; i++)
                if (_mirroredHashes[i] == stateHash) return true;

            return false;
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

            // `Idle ⇄ Run` 看的是**有没有按方向键**，不是速度：速度快慢在冰面滑行、被击退、
            // 冲刺收尾时都会骗人 —— 那几种情况下人没在按方向，动画却会一直跑。
            _animator.SetBool(HMoving, Mathf.Abs(_player.MoveInput.x) > 0.01f);

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
        private void HandleAirJumped()  => SetTrigger(HAirJump);
        private void HandleWallJumped() => SetTrigger(HWallJump);
        private void HandleLanded()     => SetTrigger(HLand);

        /// <summary>
        /// 挥了一刀。第一次是「起手」（拔剑 → 第一段），之后每一下是「接续」（下一段）。
        /// </summary>
        /// <remarks>
        /// 用两个触发器而不是一个，是因为 <c>AnyState → DrawSword</c> 的优先级高于一切 ——
        /// 用一个 <c>Attack</c> 的话，连段中途再按会从拔剑重来。
        /// </remarks>
        private void HandleAttacked()
            => SetTrigger(_player.AttackComboIndex <= 0 ? HAttack : HAttackNext);

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
