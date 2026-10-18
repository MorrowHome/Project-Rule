using UnityEngine;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    /// <summary>在发条上起跳，空格沿指定轴移动一格；碰到边界后反向。</summary>
    public sealed class DemonWindingKey : DemonSurfaceTrigger
    {
        [InspectorName("目标拼图")]
        public DemonSlidingPuzzle Puzzle;
        [InspectorName("纵向移动")]
        public bool Vertical;
        [Range(-1, 1)] [InspectorName("当前方向（±1）")] public int Direction = 1;
        [InspectorName("按压外观")]
        public Transform KeyVisual;
        private float _pressed;
        protected override bool Activate()
        {
            if (Puzzle == null || Puzzle.Solved) return false;
            int direction = Direction < 0 ? -1 : 1;
            Vector2Int step = Vertical ? new Vector2Int(0, direction) : new Vector2Int(direction, 0);
            if (!Puzzle.TryMove(step))
            {
                direction = -direction;
                if (!Puzzle.TryMove(-step)) return false;
            }
            Direction = direction; _pressed = .25f; UpdateVisual(); return true;
        }
        protected override void TickSurface(float dt) { _pressed = Mathf.Max(0f, _pressed - dt); UpdateVisual(); }
        private void UpdateVisual()
        { if (KeyVisual != null) KeyVisual.localRotation = Quaternion.Euler(0f, _pressed > 0f ? 65f : 0f, 0f); }
        private struct State { public int Direction; public float Pressed; }
        protected override object CaptureSurfaceState() => new State { Direction = Direction, Pressed = _pressed };
        protected override void RestoreSurfaceState(object state)
        { var s = (State)state; Direction = s.Direction; _pressed = s.Pressed; UpdateVisual(); }
    }
}
