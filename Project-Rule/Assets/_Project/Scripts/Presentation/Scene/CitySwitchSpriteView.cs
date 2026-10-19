using UnityEngine;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 开关类机关（按钮 / 摇杆）的交付美术表现：按 <see cref="CitySwitch.IsOn"/> 在帧序列里前后播放。
    /// </summary>
    /// <remarks>
    /// 帧序按「关 → 开」排列：关着显示第 0 帧，打开时正向播到最后一帧，关回去时倒着播回第 0 帧。
    /// 按钮只有 2 帧，摇杆是 5 帧。状态每帧从开关读，不依赖 UnityEvent —— 快照恢复不发事件也不会失步。
    /// </remarks>
    public sealed class CitySwitchSpriteView : MonoBehaviour
    {
        public CitySwitch Switch;
        public SpriteRenderer Target;
        public Sprite[] Frames = System.Array.Empty<Sprite>();
        [Min(1f)] public float FramesPerSecond = 20f;

        private int _index;
        private int _target;
        private float _step;

        private void OnEnable()
        {
            if (Frames == null || Frames.Length == 0) return;
            _index = Switch != null && Switch.IsOn ? Frames.Length - 1 : 0;
            _target = _index;
            _step = 0f;
            Apply();
        }

        private void LateUpdate()
        {
            if (Switch == null || Target == null || Frames == null || Frames.Length == 0) return;

            _target = Switch.IsOn ? Frames.Length - 1 : 0;
            if (_index == _target) return;

            _step += Time.deltaTime * Mathf.Max(1f, FramesPerSecond);
            int move = Mathf.FloorToInt(_step);
            if (move <= 0) return;

            _step -= move;
            _index = Mathf.RoundToInt(Mathf.MoveTowards(_index, _target, move));
            Apply();
        }

        private void Apply()
        {
            if (Target != null && Frames.Length > 0)
                Target.sprite = Frames[Mathf.Clamp(_index, 0, Frames.Length - 1)];
        }
    }
}
