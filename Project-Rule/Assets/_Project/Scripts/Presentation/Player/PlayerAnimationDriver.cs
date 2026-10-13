using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Presentation.Player
{
    /// <summary>
    /// 把 <see cref="PlayerController"/> 的状态翻译成动画状态。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有这一层</b>：<see cref="PlayerController"/> 不认识 Animator —— 它只管物理与输入。
    /// 表现层需要动画，就在中间放一个翻译员：它只**读**玩家状态、**推**动画状态，不参与任何玩法判断。</para>
    ///
    /// <para><b>状态机在哪</b>：状态图的**结构**在 <c>Hero.controller</c> 里（22 个 state），
    /// 但**过渡条件不连 Transition**，由本类每帧算「该是哪个状态」再 <c>CrossFade</c> 过去。
    /// 这么做的理由有两条：状态多且相互打断，画在 Animator 窗口里会成蜘蛛网；写在代码里能 review、能 diff。
    /// （代价：过渡手感不能在 Animator 窗口里调，得改代码。）</para>
    ///
    /// <para>⚠️ <b>判定顺序必须和 <c>PlayerController.FixedUpdate</c> 的分支顺序一致</b> ——
    /// 玩法没有一个「当前状态」变量（它是扁平的优先级级联），动画的状态是**推**出来的。
    /// 顺序一旦对不上，就会出现「物理在冲刺、动画在走路」。顺序见 <see cref="ResolveState"/>。</para>
    /// </remarks>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimationDriver : MonoBehaviour
    {
        // ---- state 名（必须和 Hero.controller 里的 state 同名）----
        private static readonly int StIdle       = Animator.StringToHash("Idle");
        private static readonly int StWalk       = Animator.StringToHash("Walk");
        private static readonly int StRun        = Animator.StringToHash("Run");
        private static readonly int StJumpStart  = Animator.StringToHash("JumpStart");
        private static readonly int StAirborne   = Animator.StringToHash("Airborne");
        private static readonly int StLand       = Animator.StringToHash("Land");
        private static readonly int StWallJump   = Animator.StringToHash("WallJump");
        private static readonly int StWeightless = Animator.StringToHash("Weightless");
        private static readonly int StPush       = Animator.StringToHash("Push");   // ⚠️ 暂借当冲刺，见下

        // ==================================================================
        //  序列化
        // ==================================================================

        [Header("引用（留空自动找）")]
        [SerializeField] private PlayerController _player;
        [SerializeField] private Animator         _animator;
        [SerializeField] private SpriteRenderer   _renderer;
        [SerializeField] private EntityStatus     _status;

        [Header("切换")]
        [Tooltip("状态之间交叉淡入的秒数。0 = 硬切。")]
        [SerializeField, Range(0f, 0.5f)] private float _crossFade = 0.06f;

        [Tooltip("速度超过它才算「跑」，否则算「走」。")]
        [SerializeField] private float _runSpeedThreshold = 5f;

        [Tooltip("低于它就算站住了。")]
        [SerializeField] private float _idleSpeedThreshold = 0.35f;

        [Tooltip("想用别的东西顶替冲刺动画时改这里（现在没有冲刺素材）。")]
        [SerializeField] private string _dashStateName = "Push";

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>当前正在播的 state（hash）。用来避免每帧无谓地 CrossFade。</summary>
        private int _current;

        /// <summary>正在播的一次性动作（0 = 没有）。播完自动回到常规状态。</summary>
        private int _oneShot;

        private bool _warnedNoAnimator;

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

            // 暂停：动画要一起冻住。架构 §3.1 说动画跟随 Time.timeScale，
            // 但暂停**不改** timeScale（D18 用 Physics2D.simulationMode 停物理），所以这里显式停。
            _animator.speed = (_status != null && _status.IsPaused) ? 0f : 1f;

            // 朝向：Facing 已经是「换算后」的方向，「摩擦 × 反转」时自动是反的 —— 不用另外检测。
            if (_renderer != null) _renderer.flipX = _player.Facing < 0f;

            PlayState(ResolveState());
        }

        // ==================================================================
        //  状态判定
        // ==================================================================

        /// <summary>
        /// 算出本帧该播哪个状态。
        /// </summary>
        /// <remarks>
        /// <para><b>顺序 = <c>PlayerController.FixedUpdate</c> 的分支顺序</b>，从「最强势、最不能被打断」
        /// 到「最普通」：</para>
        /// <list type="number">
        /// <item>冲刺 —— FixedUpdate ⑤ 里冲刺接管水平</item>
        /// <item>贴墙下滑 —— FixedUpdate ④′</item>
        /// <item>空中 —— ④ 重力（按沿「上」的速度分起跳/滞空/失重）</item>
        /// <item>地面：按速度分走 / 跑 / 站</item>
        /// </list>
        /// <para>一次性动作（落地、起跳）插在最前面，但会被上面的强势状态打断。</para>
        /// </remarks>
        private int ResolveState()
        {
            // ① 一次性动作还在播 → 让它播完（落地、起跳不能被打断）
            if (_oneShot != 0)
            {
                AnimatorStateInfo st = _animator.GetCurrentAnimatorStateInfo(0);
                if (st.shortNameHash == _oneShot && st.normalizedTime < 1f) return _oneShot;
                _oneShot = 0;
            }

            // ② 冲刺（暂时借 Push —— 美术还没出冲刺序列）
            if (_player.IsDashing) return Animator.StringToHash(_dashStateName);

            // ③ 贴墙下滑
            //    ⚠️ 蹬墙跳那 21 帧里混了「起跳/来回跳/滑墙/落地」，美术说等我们拆。
            //       拆完之后这里要换成专门的 WallSlide 状态。
            if (_player.IsWallSliding) return StWallJump;

            // ④ 空中
            if (!_player.IsGrounded)
            {
                // 重力反转时用专门的失重序列（美术为它出了 14 帧）
                if (Physics2D.gravity.y > 0f) return StWeightless;

                // 刚离地 → 起跳；之后 → 滞空。判「上升」要沿 _up，不是世界 y。
                float alongUp = Vector2.Dot(_player.Velocity, -Physics2D.gravity.normalized);
                if (alongUp > 0.1f && _player.JumpsLeft > 0) return StJumpStart;
                return StAirborne;
            }

            // ⑤ 地面：按水平速度分档
            float speed = Mathf.Abs(_player.Velocity.x);
            if (speed > _runSpeedThreshold) return StRun;
            if (speed > _idleSpeedThreshold) return StWalk;
            return StIdle;
        }

        /// <summary>切到指定状态。只在真的变了才切。</summary>
        private void PlayState(int state)
        {
            if (state == _current) return;

            _animator.CrossFadeInFixedTime(state, _crossFade);
            _current = state;
        }

        // ==================================================================
        //  外部触发（一次性动作）
        // ==================================================================

        /// <summary>播一个一次性动作，播完自动回到常规状态。</summary>
        /// <remarks>给别的组件调（比如收到伤害时播 Hurt）。</remarks>
        public void PlayOneShot(int stateHash)
        {
            _oneShot = stateHash;
            _current = 0;                  // 强制切过去
            PlayState(stateHash);
        }
    }
}
