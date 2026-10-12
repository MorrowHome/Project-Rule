using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Rules
{
    /// <summary>
    /// 时间规则的验证器。
    /// </summary>
    /// <remarks>
    /// <para>覆盖三块：三个参数关系对时间管理器的写、<b>快照恢复后自己「对表」</b>
    /// （§3.2 契约：恢复不触发任何规则事件）、以及恒定 / 反转两个行为关系。</para>
    /// <para>⚠️ 必须在 <b>Play 模式</b>下调用（与其它验证器一致）。</para>
    /// </remarks>
    public static class TimeRuleVerifier
    {
        private const float Tol = RuleVerifyHarness.Tolerance;

        public static string Run()
        {
            var h = new RuleVerifyHarness();
            RuleDef[] defs = RuleVerifyHarness.MakeDefs(AttributeId.Time, 10f);

            WorldRegistry.Initialize();
            TimeManager.Initialize();                       // 时间规则依赖它
            RuleSystem.Initialize(new RuleVerifyHarness.TestCatalog(defs));
            RuleSystem sys = RuleSystem.Instance;
            if (sys == null) return "✗ RuleSystem 初始化失败";

            SnapshotSystem.Initialize(sys, WorldRegistry.Instance);   // 恒定 / 反转依赖它
            TimeManager tm = TimeManager.Instance;

            var tsc = sys as ITimeRewindControl;

            // ==============================================================
            //  ① 三个参数关系
            // ==============================================================
            h.Section("1 参数关系（写时间管理器）");

            sys.Request(AttributeId.Time, RelationId.Enhance, RuleSource.Player);
            h.Check($"增强 → TimeScale = {TimeRule.EnhancedScale}",
                    Mathf.Abs(tm.TimeScale - TimeRule.EnhancedScale) < Tol, tm.TimeScale.ToString("0.###"));
            h.Check("增强不动时间机制开关", tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());

            sys.Request(AttributeId.Time, RelationId.Weaken, RuleSource.Player);
            h.Check($"削弱 → TimeScale = {TimeRule.WeakenedScale}",
                    Mathf.Abs(tm.TimeScale - TimeRule.WeakenedScale) < Tol, tm.TimeScale.ToString("0.###"));

            sys.Request(AttributeId.Time, RelationId.Nullify, RuleSource.Player);
            h.Check("失效 → 时间机制开关关闭", !tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());
            h.Check("失效不动 TimeScale（动画要照常播）",
                    Mathf.Abs(tm.TimeScale - 1f) < Tol, tm.TimeScale.ToString("0.###"));

            // ==============================================================
            //  ② 对表：快照恢复直接覆盖槽位、不调 Clear()，残留必须自己消失
            // ==============================================================
            h.Section("2 对表（快照恢复后的残留）");

            sys.Request(AttributeId.Time, RelationId.Enhance, RuleSource.Player);
            h.Check("铺垫：TimeScale = 2",
                    Mathf.Abs(tm.TimeScale - 2f) < Tol, tm.TimeScale.ToString("0.###"));

            // 模拟快照恢复：直接覆盖槽位，不经过 Request（所以不会调 Clear()）
            sys.Table.RestoreSlots(new ActiveRule?[6]);
            h.Check("槽位被悄悄清空后，时间管理器上仍留着残留",
                    Mathf.Abs(tm.TimeScale - 2f) < Tol, tm.TimeScale.ToString("0.###"));

            sys.Tick(1f);
            h.Check("Tick 一次后 TimeScale 自己回到 1",
                    Mathf.Abs(tm.TimeScale - 1f) < Tol, tm.TimeScale.ToString("0.###"));

            sys.Request(AttributeId.Time, RelationId.Nullify, RuleSource.Player);
            h.Check("铺垫：时间机制开关关闭", !tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());
            sys.Table.RestoreSlots(new ActiveRule?[6]);
            sys.Tick(1f);
            h.Check("Tick 一次后开关自己回到 true", tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());

            // ==============================================================
            //  ③ 恒定：定点回溯窗口
            // ==============================================================
            h.Section("3 恒定（定点回溯窗口）");

            h.Check("RuleSystem 实现了 ITimeRewindControl", tsc != null, "没实现");
            if (tsc == null) return "✗ ITimeRewindControl 缺失，后续无法验证";

            h.Check("初始窗口是关的", !tsc.IsWindowOpen, tsc.IsWindowOpen.ToString());
            h.Check("没开窗口时回溯返回 false", !tsc.TryRewind(), "返回了 true");

            RuleRequestResult rc = sys.Request(AttributeId.Time, RelationId.Constant, RuleSource.Player);
            h.Check("恒定请求成功", rc.Succeeded, rc.Status.ToString());
            h.Check("窗口打开了", tsc.IsWindowOpen, tsc.IsWindowOpen.ToString());
            h.Check("恒定不碰 TimeScale", Mathf.Abs(tm.TimeScale - 1f) < Tol, tm.TimeScale.ToString("0.###"));

            h.Check("回溯成功", tsc.TryRewind(), "失败");
            h.Check("回溯不限次数（第二次也成功）", tsc.TryRewind(), "失败");
            h.Check("回溯后窗口仍然开着", tsc.IsWindowOpen, tsc.IsWindowOpen.ToString());

            // ==============================================================
            //  ④ 反转：环形缓冲
            // ==============================================================
            h.Section("4 反转（环形缓冲）");

            SnapshotSystem.Instance.ClearBuffer();
            RuleRequestResult rr0 = sys.Request(AttributeId.Time, RelationId.Revert, RuleSource.Player);
            h.Check("缓冲不足两张 → 失败且状态是 NoResource（RuleReleaseService 据此不扣能量）",
                    !rr0.Succeeded && rr0.Status == RuleRequestStatus.NoResource, rr0.Status.ToString());
            h.Check("失败回滚后恒定槽位还在（不是被清空）",
                    sys.Table.IsActive(AttributeId.Time, RelationId.Constant), SlotText(sys));

            SnapshotSystem.Instance.CaptureToBuffer();
            SnapshotSystem.Instance.CaptureToBuffer();

            RuleRequestResult rr = sys.Request(AttributeId.Time, RelationId.Revert, RuleSource.Player);
            h.Check("缓冲够了 → 反转成功", rr.Succeeded, rr.Status.ToString());

            // ==============================================================
            //  ⑤ ClearAll 收尾
            // ==============================================================
            h.Section("5 ClearAll");

            sys.Request(AttributeId.Time, RelationId.Weaken, RuleSource.Player);
            sys.Request(AttributeId.Time, RelationId.Nullify, RuleSource.Player);
            sys.ClearAll();

            h.Check("ClearAll 后 TimeScale 回 1",
                    Mathf.Abs(tm.TimeScale - 1f) < Tol, tm.TimeScale.ToString("0.###"));
            h.Check("ClearAll 后时间机制开关回 true", tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());
            h.Check("ClearAll 后回溯窗口关闭", !tsc.IsWindowOpen, tsc.IsWindowOpen.ToString());

            // ==============================================================
            //  清理：别把全局时间状态留给下一个验证器
            // ==============================================================
            tm.RemoveScale(TimeSource.Rule);
            tm.SetTimeMechanicsEnabled(true);
            SnapshotSystem.Reset();
            TimeManager.Reset();
            RuleSystem.Reset();
            WorldRegistry.Reset();
            foreach (RuleDef d in defs) Object.DestroyImmediate(d);

            return h.Report("时间规则验证");
        }

        private static string SlotText(RuleSystem sys)
        {
            ActiveRule? slot = sys.Table.GetSlot(AttributeId.Time);
            return slot.HasValue ? slot.Value.Relation.ToString() : "(空)";
        }
    }
}
