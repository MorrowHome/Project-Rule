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

        /// <summary>该属性当前是否有规则生效（轮盘高亮用）。</summary>
        public bool IsAttributeActive(AttributeId attribute) => _rules.Table.IsActive(attribute);

        /// <summary>该属性当前生效的是否就是这条关系（轮盘高亮与「二次释放 = 取消」用）。</summary>
        public bool IsActive(AttributeId attribute, RelationId relation)
            => _rules.Table.IsActive(attribute, relation);

        /// <summary>取当前生效的关系；该属性没有生效规则时返回 false。</summary>
        public bool TryGetActiveRelation(AttributeId attribute, out RelationId relation)
        {
            if (_rules.Table.GetSlot(attribute) is { } active)
            {
                relation = active.Relation;
                return true;
            }
            relation = default;
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
            // 已生效的规则可以免费取消，所以它永远可点 —— 不查冷却与能量。
            // 这条必须排在冷却 / 能量之前，否则「能量不足时反而取消不掉自己放出去的规则」。
            if (_rules.Table.IsActive(Attribute, relation)) return RuleReleaseStatus.Success;
            if (!_unlocks.IsUnlocked(Attribute, relation)) return RuleReleaseStatus.Locked;
            if (_status.IsOnCooldown(Attribute, relation)) return RuleReleaseStatus.OnCooldown;
            if (_status.Energy < _rules.GetCost(Attribute, relation).Energy) return RuleReleaseStatus.NotEnoughEnergy;
            return RuleReleaseStatus.Success;
        }

        public RuleReleaseResult Confirm(RelationId relation)
        {
            if (Stage != RuleSelectionStage.Relation) return new RuleReleaseResult(RuleReleaseStatus.Undefined);
            // 二次释放 = 取消：同一 属性×关系 已经在生效，就把这条规则撤掉。
            // 取消免费（不扣能量、不进冷却），所以不经过 GetBlockReason 的冷却 / 能量检查；
            // 万一槽位在读到之后被别的途径清掉，TryCancel 会返回 false，再走正常释放。
            if (_rules.Table.IsActive(Attribute, relation) && _release.TryCancel(Attribute))
            {
                Close();
                return new RuleReleaseResult(RuleReleaseStatus.Success);
            }
            RuleReleaseStatus blocked = GetBlockReason(relation);
            if (blocked != RuleReleaseStatus.Success) return new RuleReleaseResult(blocked);
            RuleReleaseResult result = _release.TryRelease(Attribute, relation, RuleSource.Player);
            if (result.Succeeded) Close();
            return result;
        }
    }
}
