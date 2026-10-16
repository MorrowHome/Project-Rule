using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>本次游玩的世界检查点。磁盘保存交给主程监听 SaveRequested，不把内存快照冒充永久存档。</summary>
    public sealed class CityCheckpoint : CityInteractable
    {
        public string CheckpointId;
        public UnityEvent<string> SaveRequested = new UnityEvent<string>();
        public bool Activated { get; private set; }
        private Snapshot _saved;
        public override bool Interact(PlayerController player)
        {
            if (Paused || player == null || SnapshotSystem.Instance == null) return false;
            Activated = true;
            _saved = SnapshotSystem.Instance.Capture();
            SaveRequested.Invoke(CheckpointId); return true;
        }
        public bool TryRespawn()
        {
            if (Paused || _saved == null || SnapshotSystem.Instance == null) return false;
            SnapshotSystem.Instance.Restore(_saved, RestoreMode.Full); return true;
        }
        protected override object CaptureMechanismState() => Activated;
        protected override void RestoreMechanismState(object state) => Activated = (bool)state;
    }
}
