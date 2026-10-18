using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    /// <summary>3×3 八块滑动拼图；通过合法空格移动打乱，保证初始局面可解。</summary>
    public sealed class DemonSlidingPuzzle : CityMechanism
    {
        [InspectorName("八块拼图（按编号）")]
        public Transform[] Tiles = new Transform[8];
        [Min(.2f)] [InspectorName("拼图格距")] public float CellSize = 1.1f;
        [InspectorName("打乱种子")]
        public int ShuffleSeed = 17;
        [Min(1)] [InspectorName("合法打乱步数")] public int ShuffleMoves = 24;
        [InspectorName("拼图完成联动")]
        public UnityEvent<bool> OnSolved = new UnityEvent<bool>();
        private int[] _cells = { 1, 2, 3, 4, 5, 6, 7, 8, 0 };
        public int BlankIndex => Array.IndexOf(_cells, 0);
        public bool Solved { get; private set; }
        public int[] Cells => (int[])_cells.Clone();
        protected override void Awake() { base.Awake(); Shuffle(); }
        public void Shuffle()
        {
            _cells = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 0 };
            var random = new System.Random(ShuffleSeed);
            int previous = -1;
            var choices = new int[4];
            for (int i = 0; i < Mathf.Max(1, ShuffleMoves); i++)
            {
                int blank = BlankIndex, count = 0;
                for (int n = 0; n < 9; n++)
                    if (n != previous && Mathf.Abs(n % 3 - blank % 3) + Mathf.Abs(n / 3 - blank / 3) == 1) choices[count++] = n;
                int from = choices[random.Next(count)];
                _cells[blank] = _cells[from]; _cells[from] = 0; previous = blank;
            }
            if (IsComplete()) { int blank = BlankIndex; int from = blank % 3 > 0 ? blank - 1 : blank + 1; _cells[blank] = _cells[from]; _cells[from] = 0; }
            Solved = false; RefreshTiles();
        }
        public bool TryMove(Vector2Int direction)
        {
            if (Paused || Solved || Mathf.Abs(direction.x) + Mathf.Abs(direction.y) != 1) return false;
            int blank = BlankIndex, x = blank % 3 + direction.x, y = blank / 3 + direction.y;
            if (x < 0 || x > 2 || y < 0 || y > 2) return false;
            int from = y * 3 + x;
            _cells[blank] = _cells[from]; _cells[from] = 0;
            RefreshTiles();
            Solved = IsComplete();
            if (Solved) OnSolved.Invoke(true);
            return true;
        }
        private bool IsComplete()
        {
            for (int i = 0; i < 8; i++) if (_cells[i] != i + 1) return false;
            return _cells[8] == 0;
        }
        private void RefreshTiles()
        {
            for (int cell = 0; cell < 9; cell++)
            {
                int id = _cells[cell];
                if (id > 0 && id <= Tiles.Length && Tiles[id - 1] != null)
                    Tiles[id - 1].localPosition = new Vector3((cell % 3 - 1) * CellSize, (1 - cell / 3) * CellSize, 0f);
            }
        }
        private struct State { public int[] Cells; public bool Solved; }
        protected override object CaptureMechanismState() => new State { Cells = (int[])_cells.Clone(), Solved = Solved };
        protected override void RestoreMechanismState(object state)
        { var s = (State)state; _cells = (int[])s.Cells.Clone(); Solved = s.Solved; RefreshTiles(); }
    }
}
