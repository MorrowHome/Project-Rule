using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 规则系统门面。**全局单例。**
    /// </summary>
    /// <remarks>
    /// <para>它**不认识能量，也不认识冷却**。费用检查与扣除在 <c>RuleReleaseService</c>。</para>
    /// <para>写入只能通过 <see cref="Request"/>，且必须经由 <c>RuleReleaseService</c>。</para>
    /// </remarks>
    public class RuleSystem : IRuleSystem
    {
        // ==================================================================
        //  单例
        // ==================================================================

        private static RuleSystem _instance;

        /// <summary>全局单例。由 <c>RuleSystemDriver</c> 在 Awake 里初始化。</summary>
        public static RuleSystem Instance => _instance;

        /// <summary>初始化单例。重复调用会覆盖（重开关卡 / 切场景时有用）。</summary>
        public static void Initialize(IRuleCatalog catalog)
        {
            _instance = new RuleSystem(catalog);
        }

        /// <summary>仅测试用：注入替身。</summary>
        public static void SetInstanceForTesting(RuleSystem instance) => _instance = instance;

        /// <summary>清空单例。切场景 / 退出时调用，让下次重新初始化。</summary>
        public static void Reset() => _instance = null;

        // ==================================================================
        //  字段
        // ==================================================================

        private readonly RuleTable        _table = new RuleTable();
        private readonly AttributeManager _attrs = new AttributeManager();
        private readonly Dictionary<AttributeId, AttributeRuleBase> _rules = new();
        private readonly IRuleCatalog _catalog;

        public IRuleTable        Table      => _table;
        public IAttributeManager Attributes => _attrs;

        public RuleSystem(IRuleCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            // 7 个属性各注册一个规则类。
            // 未实现的先不注册 —— Request 会因为没有规则类而返回 Undefined。
            _rules[AttributeId.Gravity] = new GravityRule();
        }

        // ==================================================================
        //  写入入口
        // ==================================================================

        /// <summary>
        /// 请求规则生效。流程固定为：<b>注册 → 执行 → 失败回滚 → 广播</b>。
        /// </summary>
        public RuleRequestResult Request(AttributeId attr, RelationId rel, RuleSource source)
        {
            // ① 前置检查：格位未定义 / 属性规则类未实现
            var def = _catalog.Get(attr, rel);
            if (def == null) return RuleRequestResult.Fail(RuleRequestStatus.Undefined);
            if (!_rules.TryGetValue(attr, out var ruleClass))
                return RuleRequestResult.Fail(RuleRequestStatus.Undefined);

            // ② 注册（先写状态，让执行期间的查询读到一致数据）
            _table.Set(attr, rel, def.Duration, source);

            // ③ 执行
            var result = ruleClass.Request(rel);

            // ④ 失败回滚
            if (!result.Succeeded)
            {
                _table.Clear(attr);
                return result;
            }

            // ⑤ 广播（状态已写完）
            OnRuleActivated?.Invoke(attr, rel, source);
            OnRulesChanged?.Invoke();
            return result;
        }

        public RuleCost GetCost(AttributeId attr, RelationId rel)
        {
            var def = _catalog.Get(attr, rel);
            return def == null ? default : new RuleCost(def.ActivationCost, def.Cooldown);
        }

        // ==================================================================
        //  每帧
        // ==================================================================

        /// <summary>由 <c>RuleSystemDriver</c> 在 Update 里调用。</summary>
        public void Tick(float deltaTime)
        {
            _table.Tick(deltaTime, OnExpired);
            foreach (var rule in _rules.Values) rule.Tick(deltaTime);
        }

        private void OnExpired(AttributeId attr)
        {
            if (_rules.TryGetValue(attr, out var ruleClass)) ruleClass.Clear();
            OnRuleExpired?.Invoke(attr);
            OnRulesChanged?.Invoke();
        }

        // ==================================================================
        //  重开关卡 / 存档
        // ==================================================================

        public void ClearAll()
        {
            foreach (var rule in _rules.Values) rule.Clear();
            _table.ClearAll();
            _attrs.ResetToDefault();
            OnRulesChanged?.Invoke();
        }

        public List<string> ExportActiveIds()
        {
            var ids = new List<string>();
            foreach (var pair in _table.Slots)
            {
                var def = _catalog.Get(pair.Key, pair.Value.Relation);
                if (def != null) ids.Add(def.Id);
            }
            return ids;
        }

        // ==================================================================
        //  事件
        // ==================================================================

        public event Action<AttributeId, RelationId, RuleSource> OnRuleActivated;
        public event Action<AttributeId>                         OnRuleExpired;
        public event Action                                      OnRulesChanged;
    }
}
