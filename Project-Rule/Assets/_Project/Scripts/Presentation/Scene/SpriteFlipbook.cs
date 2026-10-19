using UnityEngine;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>循环帧序列（飞机螺旋桨这类不需要状态的循环美术）；暂停时冻结。</summary>
    public sealed class SpriteFlipbook : MonoBehaviour
    {
        public SpriteRenderer Target;
        public Sprite[] Frames = System.Array.Empty<Sprite>();
        [Min(1f)] public float FramesPerSecond = 12f;

        private float _elapsed;

        private void OnDisable() => _elapsed = 0f;

        private void LateUpdate()
        {
            if (Target == null || Frames == null || Frames.Length == 0) return;

            if (!CityMechanism.Paused) _elapsed += Time.deltaTime;
            int frame = Mathf.FloorToInt(_elapsed * Mathf.Max(1f, FramesPerSecond)) % Frames.Length;
            if (Target.sprite != Frames[frame]) Target.sprite = Frames[frame];
        }
    }
}
