using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    public enum CityRewardKind { Power, Health, Energy }
    /// <summary>永久奖励适配口：领奖记录必须与奖励一起提交，战斗回溯不能重复领奖。</summary>
    public interface ICityRewardReceiver
    {
        bool IsClaimed(string sourceId);
        bool TryGrant(string sourceId, CityRewardKind kind, int amount);
    }
    public sealed class CityRewardSource : CityInteractable
    {
        [Tooltip("每个场景实例必须有唯一 ID，不能直接沿用 prefab 的空 ID。")]
        public string SourceId;
        public CityRewardKind Kind;
        public int Amount = 1;
        [Tooltip("需实现 ICityRewardReceiver；未接入时不能领取，不会吞掉宝箱。")]
        public MonoBehaviour Receiver;
        public bool PickupVisual;
        private bool _claimed;
        private ICityRewardReceiver Sink => Receiver as ICityRewardReceiver;
        public override bool Available => !_claimed && !string.IsNullOrWhiteSpace(SourceId) && Sink != null && !Sink.IsClaimed(SourceId);
        public override bool Interact(PlayerController player)
        {
            if (Paused || player == null || !Available || !Sink.TryGrant(SourceId, Kind, Mathf.Max(1, Amount))) return false;
            _claimed = true; RefreshVisual(); return true;
        }
        private void RefreshVisual()
        {
            if (PickupVisual) SetVisible(!_claimed);
            else if (_claimed && Visuals.Length > 0) Visuals[0].color = new Color(.35f, .35f, .35f);
        }
        protected override object CaptureMechanismState() => _claimed;
        protected override void RestoreMechanismState(object state)
        {
            _claimed = (bool)state || (Sink != null && Sink.IsClaimed(SourceId)); RefreshVisual();
        }
    }
}
