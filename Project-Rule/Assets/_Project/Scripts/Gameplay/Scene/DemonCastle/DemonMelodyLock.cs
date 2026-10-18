using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    /// <summary>匹配可配置曲调；错音保留仍可成为正确前缀的尾音，超时清空。</summary>
    public sealed class DemonMelodyLock : CityMechanism
    {
        [InspectorName("目标曲调（音符编号）")]
        public int[] Sequence = { 0, 1, 0, 0, 1 };
        [Min(.1f)] [InspectorName("音符超时（秒）")] public float NoteTimeout = 5f;
        [InspectorName("曲调完成联动")]
        public UnityEvent<bool> OnUnlocked = new UnityEvent<bool>();
        public int Progress { get; private set; }
        public bool Unlocked { get; private set; }
        private float _remaining;
        public void PlayNote(int note)
        {
            if (Paused || Unlocked || Sequence == null || Sequence.Length == 0) return;
            // 最多 Sequence.Length 次比较；不分配历史音符列表。
            int candidate = Progress + 1;
            while (candidate > 0)
            {
                bool matches = Sequence[candidate - 1] == note;
                for (int i = 0; matches && i < candidate - 1; i++)
                    if (Sequence[i] != Sequence[Progress - (candidate - 1) + i]) matches = false;
                if (matches) break;
                candidate--;
            }
            Progress = candidate; _remaining = NoteTimeout;
            if (Progress == Sequence.Length) { Unlocked = true; OnUnlocked.Invoke(true); }
        }
        private void FixedUpdate()
        {
            if (Paused || Unlocked || Progress == 0) return;
            _remaining -= Time.fixedDeltaTime;
            if (_remaining <= 0f) { Progress = 0; _remaining = 0f; }
        }
        private struct State { public int Progress; public bool Unlocked; public float Remaining; }
        protected override object CaptureMechanismState() => new State { Progress = Progress, Unlocked = Unlocked, Remaining = _remaining };
        protected override void RestoreMechanismState(object state)
        { var s = (State)state; Progress = s.Progress; Unlocked = s.Unlocked; _remaining = s.Remaining; }
    }
}
