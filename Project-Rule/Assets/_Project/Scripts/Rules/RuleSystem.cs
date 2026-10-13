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
    public class RuleSystem : IRuleSystem, ITimeRewindControl
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
        /// <remarks>
        /// ⚠️ 它会**先把写在引擎上的效果撤掉**再断开引用。
        /// 重力（<c>Physics2D.gravity</c>）、时间倍率（<c>Time.timeScale</c>）这类
        /// 有引擎全局载体的东西**不会随单例消失而回滚** —— 不清就会跨场景泄漏
        /// （下一关开局还是反重力 / 2 倍速）。属性管理器同理。
        /// <para>这里**不广播事件**：拆除期订阅者可能已经被销毁了。</para>
        /// </remarks>
        public static void Reset()
        {
            if (_instance != null)
            {
                // 各规则类的 Clear() 都是幂等的，重复调用无害
                foreach (var rule in _instance._rules.Values) rule.Clear();
                _instance._table.ClearAll();
                _instance._attrs.ResetToDefault();
            }
            _instance = null;
        }

        // ==================================================================
        //  字段
        // ==================================================================

        private readonly RuleTable        _table = new RuleTable();
        private readonly AttributeManager _attrs = new AttributeManager();
        private readonly Dictionary<AttributeId, AttributeRuleBase> _rules = new();
        private readonly IRuleCatalog _catalog;

        /// <summary>留着具体类型，好把「时间恒定」的控制入口转出去。</summary>
        private readonly TimeRule _timeRule;

        public IRuleTable        Table      => _table;
        public IAttributeManager Attributes => _attrs;

        public RuleSystem(IRuleCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            // 6 个属性各注册一个规则类，属性管理器走**构造函数注入**（决策 2026-10-12）。
            // 未实现的先不注册 —— Request 会因为没有规则类而返回 Undefined。
            _rules[AttributeId.Gravity]  = new GravityRule(_attrs);
            _rules[AttributeId.Friction] = new FrictionRule(_attrs);
            _rules[AttributeId.Mass]     = new MassRule(_attrs);
            _rules[AttributeId.Damage]   = new DamageRule(_attrs);
            _rules[AttributeId.Causality] = new CausalityRule(_attrs);

            // 时间规则是唯一带「外部控制入口」的（恒定的回溯按键）。TimeManager /
            // SnapshotSystem 由它自己懒取单例 —— 后者与 RuleSystem 构造期成环。
            _timeRule = new TimeRule(_attrs, _table);
            _rules[AttributeId.Time] = _timeRule;
        }

        // ==================================================================
        //  主动结束
        // ==================================================================

        /// <summary>
        /// 主动结束某个属性当前生效的规则。
        /// </summary>
        /// <remarks>
        /// <para>与「超时 / 被覆盖」共用同一条收尾路径：<c>Clear()</c> → 清槽位 → 广播。
        /// 所以时间恒定开着的时候取消它，回溯窗口也会被正确关掉。</para>
        /// <para>该属性本来就没有生效中的规则时返回 false，且**不广播** ——
        /// 否则 UI 会被空操作刷一遍。</para>
        /// </remarks>
        public bool Cancel(AttributeId attr)
        {
            if (!_table.GetSlot(attr).HasValue) return false;

            if (_rules.TryGetValue(attr, out var ruleClass)) ruleClass.Clear();
            _table.Clear(attr);

            OnRuleExpired?.Invoke(attr);
            OnRulesChanged?.Invoke();
            return true;
        }

        // ==================================================================
        //  ITimeRewindControl（时间恒定的回溯窗口）
        // ==================================================================

        /// <inheritdoc />
        public bool IsWindowOpen => _timeRule != null && _timeRule.IsWindowOpen;

        /// <inheritdoc />
        public bool TryRewind() => _timeRule != null && _timeRule.TryRewind();

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

            // ② 记下旧槽位：覆盖前要清旧状态，失败时要能还原
            ActiveRule? previous = _table.GetSlot(attr);
            ActiveRule?[] backup = previous.HasValue ? _table.SnapshotSlots() : null;

            // ③ 覆盖前先让旧关系把世界恢复原样。
            //    纯写值的规则看不出差别（每次 Request 都会写全自己那几个字段），
            //    但带外部状态的行为型规则（挂账账本、恒定快照）漏了这步就会泄漏。
            if (previous.HasValue) ruleClass.Clear();

            // ④ 注册（先写状态，让执行期间的查询读到一致数据）
            _table.Set(attr, rel, def.Duration, source);

            // ⑤ 执行
            var result = ruleClass.Request(rel);

            // ⑥ 失败回滚：还原槽位**和**规则类状态，而不是清空。
            //    清空等于「请求失败反而把原来生效的规则也弄丢了」。
            if (!result.Succeeded)
            {
                if (previous.HasValue)
                {
                    _table.RestoreSlots(backup);
                    // 上面 Clear() 已把世界恢复成默认，这里重新应用旧关系盖回去
                    if (!ruleClass.Request(previous.Value.Relation).Succeeded)
                    {
                        UnityEngine.Debug.LogError(
                            $"[RuleSystem] 回滚 {attr} 到 {previous.Value.Relation} 失败，" +
                            "该属性的状态可能与规则表不一致。");
                    }
                }
                else
                {
                    _table.Clear(attr);
                    ruleClass.Clear();
                }
                return result;
            }

            // ⑦ 广播（状态已写完）
            //    旧规则真的没了才报「结束」；失败回滚那条路径上面已经 return 了
            if (previous.HasValue) OnRuleExpired?.Invoke(attr);
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
            // 先记下哪些属性有规则 —— 清完就读不到了
            var ended = new List<AttributeId>();
            foreach (var pair in _table.Slots) ended.Add(pair.Key);

            foreach (var rule in _rules.Values) rule.Clear();
            _table.ClearAll();
            _attrs.ResetToDefault();

            // 先写状态，再广播（与 Request 同一条纪律）
            for (int i = 0; i < ended.Count; i++) OnRuleExpired?.Invoke(ended[i]);
            OnRulesChanged?.Invoke();
        }

        // ==================================================================
        //  测试接缝
        // ==================================================================

        /// <summary>仅测试用：替换某个属性的规则类。</summary>
        /// <remarks>
        /// 给验证脚本注入探针用（例如统计 <c>Clear()</c> 被调了几次、
        /// 或造一个必定失败的规则来验证回滚）。传 null 表示移除。
        /// 与 <see cref="SetInstanceForTesting"/> 同一个用途。
        /// </remarks>
        public void SetRuleForTesting(AttributeId attr, AttributeRuleBase rule)
        {
            if (rule == null) _rules.Remove(attr);
            else _rules[attr] = rule;
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
