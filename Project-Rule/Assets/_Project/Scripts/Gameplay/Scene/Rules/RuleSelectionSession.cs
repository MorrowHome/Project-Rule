using System;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Scene.Rules
{
    public enum RuleSelectionStage { Closed, Attribute, Relation }

    /// <summary>两级选择的状态机。只通过 P3 的释放服务结算；镜头、时间和 UI 在上层组合。</summary>
    public sealed class RuleSelectionSession
    {
        private readonly IRuleCatalog _catalog;
        private readonly IRuleUnlocks _unlocks;
        private readonly IEntityStatus _status;
        private readonly IRuleSystem _rules;
        private readonly RuleReleaseService _release;
        public RuleSelectionStage Stage { get; private set; }
        public AttributeId Attribute { get; private set; }

        public RuleSelectionSession(IRuleSystem rules, IRuleCatalog catalog, IEntityStatus status, IRuleUnlocks unlocks)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _status = status ?? throw new ArgumentNullException(nameof(status));
            _unlocks = unlocks ?? throw new ArgumentNullException(nameof(unlocks));
            _release = new RuleReleaseService(rules, status, unlocks);
        }

        public void Open() => Stage = RuleSelectionStage.Attribute;
        public void Close() => Stage = RuleSelectionStage.Closed;
        public void Back() { if (Stage == RuleSelectionStage.Relation) Stage = RuleSelectionStage.Attribute; }

        public bool IsAttributeAvailable(AttributeId attribute)
        {
            foreach (RelationId relation in Enum.GetValues(typeof(RelationId)))
                if (_catalog.Get(attribute, relation) != null && _unlocks.IsUnlocked(attribute, relation)) return true;
            return false;
        }

        public bool SelectAttribute(AttributeId attribute)
        {
            // 一级轮盘始终保留，关系预览期间也允许直接悬停切换属性。
            if (Stage == RuleSelectionStage.Closed || !IsAttributeAvailable(attribute)) return false;
            Attribute = attribute;
            Stage = RuleSelectionStage.Relation;
            return true;
        }

        public RuleReleaseStatus GetBlockReason(RelationId relation)
        {
            if (_catalog.Get(Attribute, relation) == null) return RuleReleaseStatus.Undefined;
            if (!_unlocks.IsUnlocked(Attribute, relation)) return RuleReleaseStatus.Locked;
            if (_status.IsOnCooldown(Attribute, relation)) return RuleReleaseStatus.OnCooldown;
            if (_status.Energy < _rules.GetCost(Attribute, relation).Energy) return RuleReleaseStatus.NotEnoughEnergy;
            return RuleReleaseStatus.Success;
        }

        public RuleReleaseResult Confirm(RelationId relation)
        {
            if (Stage != RuleSelectionStage.Relation) return new RuleReleaseResult(RuleReleaseStatus.Undefined);
            RuleReleaseStatus blocked = GetBlockReason(relation);
            if (blocked != RuleReleaseStatus.Success) return new RuleReleaseResult(blocked);
            RuleReleaseResult result = _release.TryRelease(Attribute, relation, RuleSource.Player);
            if (result.Succeeded) Close();
            return result;
        }
    }
}
