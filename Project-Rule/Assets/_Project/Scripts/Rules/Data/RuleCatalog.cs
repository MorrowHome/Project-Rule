using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 规则目录资产：把所有 <see cref="RuleDef"/> 列在一处，供规则系统索引。
    /// </summary>
    /// <remarks>
    /// 放在 <c>Assets/_Project/Data/Rules/</c>，由 <c>RuleSystemDriver</c> 引用。
    /// </remarks>
    [CreateAssetMenu(menuName = "规则游戏/RuleCatalog", fileName = "RuleCatalog")]
    public class RuleCatalog : ScriptableObject, IRuleCatalog
    {
        [SerializeField] private RuleDef[] _rules;

        private Dictionary<(AttributeId, RelationId), RuleDef> _index;

        public RuleDef Get(AttributeId attr, RelationId rel)
        {
            BuildIndex();
            return _index.TryGetValue((attr, rel), out var def) ? def : null;
        }

        private void BuildIndex()
        {
            if (_index != null) return;
            _index = new Dictionary<(AttributeId, RelationId), RuleDef>();
            if (_rules == null) return;

            foreach (var def in _rules)
            {
                if (def == null) continue;
                var key = (def.Attribute, def.Relation);
                if (_index.ContainsKey(key))
                {
                    Debug.LogError($"[RuleCatalog] 重复定义：{key}（{def.name}）。后者被忽略。", this);
                    continue;
                }
                _index[key] = def;
            }
        }

        private void OnValidate() => _index = null;   // 编辑器里改了数组就重建索引
    }
}
