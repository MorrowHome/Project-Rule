using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 输入：把输入资源翻译成「一帧的意图」，并管输入门禁。只读键，不懂物理。
    /// </summary>
    /// <remarks>
    /// <para><b>两个时间尺度</b>：<see cref="Tick"/> 跑在视觉帧（<c>Update</c>），
    /// <see cref="Sample"/> 跑在物理帧（<c>FixedUpdate</c>）。一个渲染帧里可能没有物理帧、
    /// 也可能有多个，所以四个边沿标记是**粘滞**的：按下就置位，直到 <see cref="Sample"/> 取走。
    /// 不粘滞的话，渲染帧率高于物理帧率时按键会被整帧吃掉。</para>
    /// <para><see cref="Move"/> 是**保持值**，不是边沿 —— 按住多久都算，随 <see cref="Tick"/> 刷新，
    /// 不被 <see cref="Sample"/> 清掉。</para>
    /// <para><b>门禁</b>：选择器打开时接管输入，但**不暂停物理**。释放后的那一帧仍要吞掉按键，
    /// 否则同一次确认键会既关掉界面又挥出一刀。</para>
    /// </remarks>
    public sealed class PlayerInput
    {
        private readonly InputAction _moveAction;
        private readonly InputAction _jumpAction;
        private readonly InputAction _attackAction;
        private readonly InputAction _dashAction;

        private Vector2 _move;
        private bool    _jumpPressed;
        private bool    _jumpReleased;
        private bool    _dashPressed;
        private bool    _attackPressed;
        private bool    _rewindPressed;

        private int _blockCount;
        private int _resumeFrame;

        public PlayerInput(InputActionAsset actions)
        {
            if (actions == null) return;

            _moveAction   = actions.FindAction("Player/Move",   false);
            _jumpAction   = actions.FindAction("Player/Jump",   false);
            _attackAction = actions.FindAction("Player/Attack", false);

            // ⚠️ 冲刺主键位是**鼠标右键**（在 Tick 里直接读 Mouse.current，不走输入资源）。
            //    这里保留 Player/Crouch（C 键 / 手柄 buttonEast）作为**手柄与键盘的后备** ——
            //    它本来就没人用，保留它不必动团队共享的 .inputactions，手柄也还有得用。
            //    等输入方案定稿，把它重命名成 Dash 再改这一行即可（详见 Doc/玩家动作设计.md §3.6）。
            _dashAction   = actions.FindAction("Player/Crouch", false);
        }

        /// <summary>四个动作是否都找到了。没找到时 <see cref="Move"/> 恒为零。</summary>
        public bool IsReady => _moveAction != null;

        /// <summary>当前按住的方向（**保持值**，未经摩擦反转换算）。</summary>
        public Vector2 Move => _move;

        /// <summary>门禁正在生效，或刚释放、恢复帧还没过。</summary>
        public bool IsSuppressed => _blockCount > 0 || Time.frameCount < _resumeFrame;

        public void Enable()
        {
            _moveAction?.Enable();
            _jumpAction?.Enable();
            _attackAction?.Enable();
            _dashAction?.Enable();
        }

        public void Disable()
        {
            _moveAction?.Disable();
            _jumpAction?.Disable();
            _attackAction?.Disable();
            _dashAction?.Disable();
        }

        /// <summary>
        /// 视觉帧：读键。
        /// </summary>
        /// <param name="suppressed">暂停 / 已阵亡 / 被 UI 接管 / 恢复帧 —— 都不读键，并清空粘滞标记。</param>
        public void Tick(bool suppressed)
        {
            if (suppressed)
            {
                _move          = Vector2.zero;
                _jumpPressed   = false;
                _jumpReleased  = false;
                _dashPressed   = false;
                _attackPressed = false;
                _rewindPressed = false;
                return;
            }

            _move = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;

            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())     _jumpPressed   = true;
            if (_jumpAction != null && _jumpAction.WasReleasedThisFrame())    _jumpReleased  = true;   // 可变跳跃高度：松键那一刻削速
            // 冲刺：鼠标右键是主键位（直接读设备，不走输入资源），Player/Crouch 作手柄/键盘后备。
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) _dashPressed = true;
            if (_dashAction != null && _dashAction.WasPressedThisFrame())     _dashPressed   = true;
            if (_attackAction != null && _attackAction.WasPressedThisFrame()) _attackPressed = true;

            // 时间恒定的「定点回溯」：**独占一个键**（键盘 Q / 手柄 LB）。
            // ⚠️ 不用 R —— RuleWheelLabHUD 的 R 是「清空规则」，会撞车。
            // ⚠️ 不走 .inputactions 的 Interact —— 那个动作带 Hold 交互，
            //    按住才会触发，不适合「按一下退一步」。
            if (Keyboard.current?.qKey.wasPressedThisFrame == true
                || Gamepad.current?.leftShoulder.wasPressedThisFrame == true) _rewindPressed = true;
        }

        /// <summary>
        /// 物理帧：取走本帧意图。边沿标记取走即清 —— 所以**每物理帧只能调一次**。
        /// </summary>
        public State Sample()
        {
            var state = new State(_move, _jumpPressed, _jumpReleased, _dashPressed, _attackPressed, _rewindPressed);

            _jumpPressed   = false;
            _jumpReleased  = false;
            _dashPressed   = false;
            _attackPressed = false;
            _rewindPressed = false;

            return state;
        }

        /// <summary>接管输入。可重入计数，配套的令牌释放后恢复。</summary>
        public IDisposable Block()
        {
            _blockCount++;
            return new Token(this);
        }

        private sealed class Token : IDisposable
        {
            private PlayerInput _owner;

            public Token(PlayerInput owner) => _owner = owner;

            public void Dispose()
            {
                if (_owner == null) return;

                _owner._blockCount = Mathf.Max(0, _owner._blockCount - 1);

                // 选择器在玩家 Update 前结束时，这一帧的攻击键仍属于 UI，不能再次挥刀。
                _owner._resumeFrame = Time.frameCount + 1;
                _owner = null;
            }
        }

        /// <summary>
        /// 本物理帧的意图。
        /// </summary>
        /// <remarks>边沿字段只在**被取走的那一帧**为真；<see cref="Move"/> 是保持值。</remarks>
        public readonly struct State
        {
            public readonly Vector2 Move;
            public readonly bool    JumpPressed;
            public readonly bool    JumpReleased;
            public readonly bool    DashPressed;
            public readonly bool    AttackPressed;

            /// <summary>时间恒定的定点回溯键（Q / LB）。窗口没开时是空操作。</summary>
            public readonly bool    RewindPressed;

            public State(Vector2 move, bool jumpPressed, bool jumpReleased, bool dashPressed, bool attackPressed,
                         bool rewindPressed = false)
            {
                Move          = move;
                JumpPressed   = jumpPressed;
                JumpReleased  = jumpReleased;
                DashPressed   = dashPressed;
                AttackPressed = attackPressed;
                RewindPressed = rewindPressed;
            }
        }
    }
}
