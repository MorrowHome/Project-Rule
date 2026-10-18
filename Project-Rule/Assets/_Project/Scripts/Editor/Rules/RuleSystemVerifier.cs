using System.Collections.Generic;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Rules;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Rules
{
    /// <summary>
    /// 规则系统自身契约的验证器：覆盖清理、失败回滚、<c>OnRuleExpired</c> 触发范围，
    /// 以及轮盘「同一格位二次释放 = 免费取消」这条路径。
    /// </summary>
    /// <remarks>
    /// <para>它用 <c>RuleSystem.SetRuleForTesting</c> 把质量槽位换成一个**探针规则**，
    /// 因为真实规则每次 <c>Request</c> 都会写全自己的字段，覆盖时到底有没有调
    /// 旧规则的 <c>Clear()</c> 从属性值上看不出来。</para>
    /// <para>⚠️ 必须在 <b>Play 模式</b>下调用（与其它验证器一致）。</para>
    /// </remarks>
    public static class RuleSystemVerifier
    {
        private const float Tol = RuleVerifyHarness.Tolerance;

        /// <summary>探针规则：只记录被调了什么，不真的改世界。</summary>
        private sealed class ProbeRule : AttributeRuleBase
        {
            /// <summary><c>Clear()</c> 被调了几次。</summary>
            public int ClearCount;

            /// <summary><c>Request</c> 进来几次（含失败的那次）。</summary>
            public int ApplyCount;

            /// <summary>最后一次进来的关系。</summary>
            public RelationId LastApplied;

            /// <summary>置 true 后，下一次 <c>Request</c> 必定失败。</summary>
            public bool FailNext;

            public ProbeRule(IAttributeManager attributes) : base(attributes) { }

            protected override bool OnRevert() => Apply(RelationId.Revert);
            protected override bool OnNullify() => Apply(RelationId.Nullify);
            protected override bool OnConstant() => Apply(RelationId.Constant);
            protected override bool OnEnhance() => Apply(RelationId.Enhance);
            protected override bool OnWeaken() => Apply(RelationId.Weaken);

            public override void Clear() => ClearCount++;

            private bool Apply(RelationId rel)
            {
                ApplyCount++;
                LastApplied = rel;      // 先记下来，失败那次也算「进来过」
                if (FailNext) { FailNext = false; return false; }
                return true;
            }
        }

        public static string Run()
        {
            var h = new RuleVerifyHarness();
            RuleDef[] defs = RuleVerifyHarness.MakeDefs(AttributeId.Mass, 10f);

            WorldRegistry.Initialize();
            var catalog = new RuleVerifyHarness.TestCatalog(defs);
            RuleSystem.Initialize(catalog);
            RuleSystem sys = RuleSystem.Instance;
            if (sys == null) return "✗ RuleSystem 初始化失败";

            var probe = new ProbeRule(sys.Attributes);
            sys.SetRuleForTesting(AttributeId.Mass, probe);

            var expired = new List<AttributeId>();
            int activated = 0;
            int changed = 0;
            sys.OnRuleExpired += a => expired.Add(a);
            sys.OnRuleActivated += (a, r, s) => activated++;
            sys.OnRulesChanged += () => changed++;

            // ==============================================================
            //  ① 首次请求：该属性本来没规则，不该调 Clear()
            // ==============================================================
            h.Section("1 首次请求（无旧规则）");

            RuleRequestResult r1 = sys.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player);
            h.Check("请求成功", r1.Succeeded, r1.Status.ToString());
            h.Check("不调旧规则的 Clear()", probe.ClearCount == 0, $"ClearCount={probe.ClearCount}");
            h.Check("不触发 OnRuleExpired", expired.Count == 0, $"expired={expired.Count}");
            h.Check("槽位 = Enhance",
                    sys.Table.IsActive(AttributeId.Mass, RelationId.Enhance), SlotText(sys));
            h.Check("OnRuleActivated 触发一次", activated == 1, $"activated={activated}");

            // ==============================================================
            //  ② 覆盖槽位：必须调旧规则的 Clear() 并广播 OnRuleExpired
            // ==============================================================
            h.Section("2 覆盖槽位");

            RuleRequestResult r2 = sys.Request(AttributeId.Mass, RelationId.Weaken, RuleSource.Player);
            h.Check("请求成功", r2.Succeeded, r2.Status.ToString());
            h.Check("覆盖时调用了旧规则的 Clear()", probe.ClearCount == 1, $"ClearCount={probe.ClearCount}");
            h.Check("覆盖时触发 OnRuleExpired(Mass)",
                    expired.Count == 1 && expired[0] == AttributeId.Mass,
                    $"expired=[{string.Join(",", expired)}]");
            h.Check("槽位换成 Weaken",
                    sys.Table.IsActive(AttributeId.Mass, RelationId.Weaken), SlotText(sys));
            h.Check("规则类最后应用的是 Weaken",
                    probe.LastApplied == RelationId.Weaken, probe.LastApplied.ToString());

            // ==============================================================
            //  ③ 失败回滚：还原旧槽位，而不是清空
            // ==============================================================
            h.Section("3 失败回滚");

            expired.Clear();
            int clearBefore = probe.ClearCount;
            int changedBefore = changed;
            probe.FailNext = true;

            RuleRequestResult r3 = sys.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player);
            h.Check("请求返回失败", !r3.Succeeded, r3.Status.ToString());
            h.Check("回滚前确实清过旧状态", probe.ClearCount == clearBefore + 1,
                    $"ClearCount={probe.ClearCount}");
            h.Check("槽位还原成 Weaken（不是被清空）",
                    sys.Table.IsActive(AttributeId.Mass, RelationId.Weaken), SlotText(sys));
            h.Check("回滚重新应用了旧关系",
                    probe.LastApplied == RelationId.Weaken, probe.LastApplied.ToString());
            h.Check("回滚不触发 OnRuleExpired（旧规则还在）", expired.Count == 0,
                    $"expired=[{string.Join(",", expired)}]");
            // 回滚后净变化为零 —— 状态和请求前一样，就不该广播
            h.Check("回滚不触发 OnRulesChanged", changed == changedBefore,
                    $"changed={changed}, 期望 {changedBefore}");

            // ==============================================================
            //  ④ ClearAll：每个原本活跃的属性各发一次 OnRuleExpired
            // ==============================================================
            h.Section("4 ClearAll");

            expired.Clear();
            sys.ClearAll();
            h.Check("ClearAll 触发 OnRuleExpired(Mass)",
                    expired.Count == 1 && expired[0] == AttributeId.Mass,
                    $"expired=[{string.Join(",", expired)}]");
            h.Check("ClearAll 后槽位为空", !sys.Table.IsActive(AttributeId.Mass), SlotText(sys));
            h.Check("ClearAll 调了规则类的 Clear()", probe.ClearCount == clearBefore + 2,
                    $"ClearCount={probe.ClearCount}");
            h.Check("ClearAll 触发一次 OnRulesChanged", changed == changedBefore + 1,
                    $"changed={changed}, 期望 {changedBefore + 1}");

            // ==============================================================
            //  ⑤ 未定义格位：走不到规则类
            // ==============================================================
            h.Section("5 未定义格位");

            int applyBefore = probe.ApplyCount;
            RuleRequestResult r5 = sys.Request(AttributeId.Mass, RelationId.Revert, RuleSource.Player);
            // defs 里五个关系都建了，所以这里应该成功；换一个真没定义的属性来测
            h.Check("目录里有定义的格位能走到规则类", r5.Succeeded, r5.Status.ToString());

            RuleRequestResult r6 = sys.Request(AttributeId.Causality, RelationId.Revert, RuleSource.Player);
            h.Check("目录里没定义的格位返回 Undefined",
                    !r6.Succeeded && r6.Status == RuleRequestStatus.Undefined, r6.Status.ToString());
            h.Check("未定义格位不会碰规则类", probe.ApplyCount == applyBefore + 1,
                    $"ApplyCount={probe.ApplyCount}");

            // ==============================================================
            //  ⑥ 主动结束（玩家能力）
            // ==============================================================
            h.Section("6 主动结束（Cancel）");

            sys.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player);
            h.Check("铺垫：Mass/Enhance 生效中",
                    sys.Table.IsActive(AttributeId.Mass, RelationId.Enhance), SlotText(sys));

            int cancelClearBefore = probe.ClearCount;
            expired.Clear();
            changed = 0;

            bool cancelled = sys.Cancel(AttributeId.Mass);
            h.Check("Cancel 返回 true", cancelled, "false");
            h.Check("槽位被清空", !sys.Table.IsActive(AttributeId.Mass), SlotText(sys));
            h.Check("调了规则类的 Clear()（写在引擎上的外部状态要撤掉）",
                    probe.ClearCount == cancelClearBefore + 1, $"ClearCount={probe.ClearCount}");
            h.Check("触发 OnRuleExpired(Mass)",
                    expired.Count == 1 && expired[0] == AttributeId.Mass,
                    $"expired=[{string.Join(",", expired)}]");
            h.Check("触发一次 OnRulesChanged", changed == 1, $"changed={changed}");

            expired.Clear();
            changed = 0;
            bool cancelAgain = sys.Cancel(AttributeId.Mass);
            h.Check("没有生效中的规则时返回 false", !cancelAgain, "true");
            h.Check("空操作不广播 OnRuleExpired", expired.Count == 0, $"expired=[{string.Join(",", expired)}]");
            h.Check("空操作不广播 OnRulesChanged", changed == 0, $"changed={changed}");

            // 取消免费：经由 RuleReleaseService 走一遍，能量与冷却都不该动
            var energyHost = new GameObject("CancelEnergyProbe");
            var energyStatus = energyHost.AddComponent<RuleGame.Gameplay.Player.EntityStatus>();
            var service = new RuleGame.Gameplay.Player.RuleReleaseService(
                sys, energyStatus, RuleGame.Gameplay.Player.AllUnlockedRuleUnlocks.Instance);

            sys.Request(AttributeId.Mass, RelationId.Weaken, RuleSource.Player);
            float energyBefore = energyStatus.Energy;

            bool viaService = service.TryCancel(AttributeId.Mass);
            h.Check("RuleReleaseService.TryCancel 成功", viaService, "false");
            h.Check("✅ 取消**不消耗能量**",
                    Mathf.Abs(energyStatus.Energy - energyBefore) < RuleVerifyHarness.Tolerance,
                    $"{energyBefore} → {energyStatus.Energy}");
            h.Check("✅ 取消不开始冷却",
                    !energyStatus.IsOnCooldown(AttributeId.Mass, RelationId.Weaken),
                    energyStatus.GetCooldownRemaining(AttributeId.Mass, RelationId.Weaken).ToString("0.##"));
            h.Check("取消后槽位为空", !sys.Table.IsActive(AttributeId.Mass), SlotText(sys));

            Object.DestroyImmediate(energyHost);

            // ==============================================================
            //  ⑦ 轮盘二次释放 = 取消（RuleSelectionSession）
            // ==============================================================
            h.Section("7 轮盘二次释放（取消，且不扣能量）");

            // 只给 Enhance 一个真实价格，才能验证「取消不扣费」不是靠 0 费蒙过去的。
            foreach (RuleDef d in defs) if (d.Relation == RelationId.Enhance) d.ActivationCost = 25f;

            sys.ClearAll();
            var wheelHost = new GameObject("WheelSessionProbe");
            var wheelStatus = wheelHost.AddComponent<RuleGame.Gameplay.Player.EntityStatus>();
            var session = new RuleSelectionSession(sys, catalog, wheelStatus,
                RuleGame.Gameplay.Player.AllUnlockedRuleUnlocks.Instance);

            session.Open();
            h.Check("首次打开处于属性阶段", session.Stage == RuleSelectionStage.Attribute, session.Stage.ToString());
            h.Check("选中 Mass 进入关系阶段", session.SelectAttribute(AttributeId.Mass), session.Stage.ToString());

            float firstEnergy = wheelStatus.Energy;
            RuleReleaseResult first = session.Confirm(RelationId.Enhance);
            h.Check("首次释放成功", first.Succeeded, first.Status.ToString());
            h.Check("首次释放扣了能量", Mathf.Abs(firstEnergy - 25f - wheelStatus.Energy) < Tol,
                    $"{firstEnergy} → {wheelStatus.Energy}");
            h.Check("Mass/Enhance 生效中",
                    sys.Table.IsActive(AttributeId.Mass, RelationId.Enhance), SlotText(sys));
            h.Check("生效属性可查询", session.IsAttributeActive(AttributeId.Mass), "false");
            h.Check("生效关系可查询", session.IsActive(AttributeId.Mass, RelationId.Enhance), "false");
            h.Check("生效关系不被冷却 / 能量拦下",
                    session.GetBlockReason(RelationId.Enhance) == RuleReleaseStatus.Success,
                    session.GetBlockReason(RelationId.Enhance).ToString());

            // 把能量清空：此时任何正常释放都该被拦下，取消仍要放行。
            wheelStatus.TryConsumeEnergy(wheelStatus.Energy);
            float cancelEnergy = wheelStatus.Energy;
            session.Open();
            session.SelectAttribute(AttributeId.Mass);
            h.Check("能量为 0 时生效关系仍可点（取消免费）",
                    session.GetBlockReason(RelationId.Enhance) == RuleReleaseStatus.Success,
                    session.GetBlockReason(RelationId.Enhance).ToString());

            RuleReleaseResult second = session.Confirm(RelationId.Enhance);
            h.Check("二次释放成功", second.Succeeded, second.Status.ToString());
            h.Check("二次释放 = 取消（槽位清空）", !sys.Table.IsActive(AttributeId.Mass), SlotText(sys));
            h.Check("✅ 取消不消耗能量",
                    Mathf.Abs(cancelEnergy - wheelStatus.Energy) < Tol,
                    $"{cancelEnergy} → {wheelStatus.Energy}");
            h.Check("取消后高亮消失", !session.IsAttributeActive(AttributeId.Mass), "仍有生效规则");
            h.Check("二次释放后回到关闭状态", session.Stage == RuleSelectionStage.Closed, session.Stage.ToString());

            Object.DestroyImmediate(wheelHost);

            RuleSystem.Reset();
            foreach (RuleDef d in defs) Object.DestroyImmediate(d);

            return h.Report("规则系统契约验证");
        }

        private static string SlotText(RuleSystem sys)
        {
            ActiveRule? slot = sys.Table.GetSlot(AttributeId.Mass);
            return slot.HasValue ? slot.Value.Relation.ToString() : "(空)";
        }
    }
}
