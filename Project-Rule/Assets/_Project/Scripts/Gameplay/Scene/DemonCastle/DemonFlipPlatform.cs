using UnityEngine;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    /// <summary>从平台起跳才翻面；碰撞体保持稳定，只有外观旋转，完成后切换地面材质。</summary>
    public sealed class DemonFlipPlatform : DemonSurfaceTrigger
    {
        [Header("翻转平台")]
        [InspectorName("翻面外观")]
        public Transform Face;
        [InspectorName("粗糙面材质")]
        public PhysicsMaterial2D RoughMaterial;
        [InspectorName("冰面材质")]
        public PhysicsMaterial2D IceMaterial;
        [Min(.05f)] [InspectorName("翻转时长")] public float FlipSeconds = .35f;
        public bool Icy { get; private set; }
        public bool Turning { get; private set; }
        private float _elapsed;
        protected override bool Activate()
        {
            if (Turning) return false;
            Turning = true; _elapsed = 0f; return true;
        }
        protected override void TickSurface(float dt)
        {
            if (!Turning) return;
            _elapsed += dt;
            if (_elapsed >= FlipSeconds) { Icy = !Icy; Turning = false; _elapsed = 0f; }
            RefreshFace();
        }
        private void Start() => RefreshFace();
        private void RefreshFace()
        {
            float angle = Icy ? 180f : 0f;
            if (Turning) angle += 180f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / Mathf.Max(.05f, FlipSeconds)));
            if (Face != null) Face.localRotation = Quaternion.Euler(0f, 0f, angle);
            if (Shape != null) Shape.sharedMaterial = Icy ? IceMaterial : RoughMaterial;
        }
        private struct State { public bool Icy, Turning; public float Elapsed; }
        protected override object CaptureSurfaceState() => new State { Icy = Icy, Turning = Turning, Elapsed = _elapsed };
        protected override void RestoreSurfaceState(object state)
        {
            var s = (State)state; Icy = s.Icy; Turning = s.Turning; _elapsed = s.Elapsed; RefreshFace();
        }
    }
}
