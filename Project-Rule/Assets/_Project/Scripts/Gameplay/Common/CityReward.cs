using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Rules;
using UnityEngine;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 宝箱 / 龙珠能发的东西。**只在末尾追加** —— prefab 里存的是整数。
    /// </summary>
    public enum CityRewardKind
    {
        [InspectorName("战斗力")] Power = 0,
        [InspectorName("生命")] Health = 1,
        [InspectorName("能量")] Energy = 2,
        [InspectorName("能力")] Ability = 3,
        [InspectorName("规则属性")] RuleAttribute = 4,
        [InspectorName("规则关系")] RuleRelation = 5,
    }

    /// <summary>一次领取的完整载荷。宝箱只负责组装，怎么兑现由接收方决定。</summary>
    public readonly struct CityReward
    {
        public readonly CityRewardKind Kind;
        public readonly int Amount;
        public readonly PlayerAbility Ability;
        public readonly AttributeId Attribute;
        public readonly RelationId Relation;

        public CityReward(CityRewardKind kind, int amount = 0,
                          PlayerAbility ability = PlayerAbility.None,
                          AttributeId attribute = AttributeId.Gravity,
                          RelationId relation = RelationId.Revert)
        {
            Kind = kind;
            Amount = amount;
            Ability = ability;
            Attribute = attribute;
            Relation = relation;
        }
    }

    /// <summary>永久奖励适配口：领奖记录必须与奖励一起提交，战斗回溯不能重复领奖。</summary>
    public interface ICityRewardReceiver
    {
        bool IsClaimed(string sourceId);
        bool TryGrant(string sourceId, CityReward reward);
    }
}
