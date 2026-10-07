using RuleGame.Infrastructure.Rules;

namespace RuleGame.Core.Rules
{
    /// <summary>按 属性×关系 查找 <see cref="RuleDef"/>。</summary>
    public interface IRuleCatalog
    {
        /// <summary>查不到返回 null（表示该格位未定义）。</summary>
        RuleDef Get(AttributeId attr, RelationId rel);
    }
}
