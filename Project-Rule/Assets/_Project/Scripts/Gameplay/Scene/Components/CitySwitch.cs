using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>摇杆可反复切换，按钮可单次锁定；通过 UnityEvent 连门、电梯或炸弹，不按物体名字找对象。</summary>
    public sealed class CitySwitch : CityInteractable
    {
        public bool Toggle = true;
        public bool IsOn;
        [Tooltip("可选：双端摇杆读取同一电梯/门的目标状态，避免两边开关不同步。")]
        public CityPathMotor FollowMotor;
        public UnityEvent<bool> OnChanged = new UnityEvent<bool>();
        public override bool Available => Toggle || !IsOn;
        public override bool Interact(PlayerController player)
        {
            if (!Available || Paused) return false;
            bool on = FollowMotor != null ? !FollowMotor.TargetEnd : !IsOn;
            SetOn(on);
            return true;
        }
        public void SetOn(bool on)
        {
            if (FollowMotor != null) FollowMotor.SetOn(on);
            if (IsOn == on) return;
            IsOn = on; RefreshVisual(); OnChanged.Invoke(IsOn);
        }
        private void Start() => RefreshVisual();
        private void LateUpdate()
        {
            if (FollowMotor != null && IsOn != FollowMotor.TargetEnd) { IsOn = FollowMotor.TargetEnd; RefreshVisual(); }
        }
        private void RefreshVisual()
        {
            if (Visuals.Length > 0) Visuals[0].color = IsOn ? new Color(.3f, .75f, .5f) : new Color(.85f, .6f, .2f);
        }
        protected override object CaptureMechanismState() => IsOn;
        protected override void RestoreMechanismState(object state)
        {
            IsOn = (bool)state; RefreshVisual();
            // 不重发联动事件：目标自己的快照已经恢复，重发会把门/电梯再次启动。
        }
    }
}
