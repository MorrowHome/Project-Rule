using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Rules;
using UnityEngine;

namespace RuleGame.Development.RuleWheel
{
    /// <summary>试播时开放目录中的词条；正式场景替换为 P1/P3 的库存组件，UI 不保存解锁进度。</summary>
    public sealed class RuleWheelLabUnlocks : MonoBehaviour, IRuleUnlocks
    {
        public bool IsUnlocked(AttributeId attribute, RelationId relation) => true;
    }
}
