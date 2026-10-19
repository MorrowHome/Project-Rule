using UnityEngine;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 楼层拉杆的交付美术表现：按 <see cref="CityFloorLever.Position"/> 在多档帧序列里前后播放。
    /// </summary>
    /// <remarks>
    /// <para>帧序按「最右档 → 最左档」排（美工交付的 1 → 5 就是右 → 左）；而档位 0 = 最左档，
    /// 所以索引要反过来算：档 0 用最后一帧，最高档用第 0 帧。</para>
    /// <para>状态每帧从机关读，不依赖事件 —— 快照恢复不发事件也不会失步（与 <c>CitySwitchSpriteView</c> 同一套）。</para>
    /// </remarks>
    public sealed class CityFloorLeverSpriteView : MonoBehaviour
    {
        public CityFloorLever Lever;
        public SpriteRenderer Target;
        public Sprite[] Frames = System.Array.Empty<Sprite>();
        [Min(1f)] public float FramesPerSecond = 22f;

        private int _index;
        private int _target;
        private float _step;

        private void OnEnable()
        {
            _index = _target = FrameFor(Lever != null ? Lever.Position : 0);
            _step = 0f;
            Apply();
        }

        private void LateUpdate()
        {
            if (Lever == null || Target == null || Frames == null || Frames.Length < 2) return;

            _target = FrameFor(Lever.Position);
            if (_index == _target) return;

            _step += Time.deltaTime * Mathf.Max(1f, FramesPerSecond);
            int move = Mathf.FloorToInt(_step);
            if (move <= 0) return;

            _step -= move;
            _index = Mathf.RoundToInt(Mathf.MoveTowards(_index, _target, move));
            Apply();
        }

        /// <summary>档位 → 帧索引。0 档 = 最左 = 最后一帧。</summary>
        private int FrameFor(int position)
        {
            if (Frames == null || Frames.Length == 0) return 0;

            int stops = Mathf.Max(2, Lever != null ? Lever.StopCount : 2);
            float t = Mathf.Clamp01(position / (float)(stops - 1));
            return Mathf.Clamp(Mathf.RoundToInt((1f - t) * (Frames.Length - 1)), 0, Frames.Length - 1);
        }

        private void Apply()
        {
            if (Target != null && Frames != null && Frames.Length > 0)
                Target.sprite = Frames[Mathf.Clamp(_index, 0, Frames.Length - 1)];
        }
    }
}
