using System;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Editor.Infrastructure
{
    /// <summary>
    /// 快照系统验证：单点采集 / 恢复、禁用对象、剩余时间回退、实体状态、规则效果重建、环形缓冲、定点。
    /// </summary>
    /// <remarks>
    /// <para>核心断言是恢复契约：<b>恢复不触发规则系统事件</b>（只触发 <c>OnRestored</c>）、
    /// <b>剩余时间连槽位一起回退</b>、<b>禁用的对象能复活</b>、
    /// <b>采集时就是禁用的对象恢复后仍是禁用</b>、<b>重力这类引擎效果会被重建</b>。</para>
    /// <para>实体用手写的 <see cref="TestEntity"/>（不用 <c>RuleAffectedBody</c>）：
    /// 那个组件靠 <c>OnEnable</c> 自动登记，编辑模式下不触发、Play 模式下会与手动登记重复。
    /// 手动登记在两种模式下行为一致。</para>
    /// <para>⚠️ 采集范围是<b>快照名册</b>（<see cref="ISnapshotRoster"/>），不是活动实体注册表。</para>
    /// </remarks>
    public static class SnapshotSystemVerifier
    {
        /// <summary>测试实体：不继承 MonoBehaviour，注册表只认接口。</summary>
        private sealed class TestEntity : IRuleAffected
        {
            public TestEntity(Rigidbody2D body) => Body = body;
            public Rigidbody2D  Body      { get; }
            public Collider2D[] Colliders => new Collider2D[0];
        }

        public static string Run()
        {
            var h = new RuleGame.Editor.Rules.RuleVerifyHarness();

            // ----------------------------------------------------------
            //  搭建：名册 + 注册表 + 规则系统 + 快照系统，两个测试实体
            // ----------------------------------------------------------
            RuleDef[] massDefs = RuleGame.Editor.Rules.RuleVerifyHarness.MakeDefs(AttributeId.Mass, 10f);
            RuleDef[] gravityDefs = RuleGame.Editor.Rules.RuleVerifyHarness.MakeDefs(AttributeId.Gravity, 10f);
            var defs = new RuleDef[massDefs.Length + gravityDefs.Length];
            Array.Copy(massDefs, defs, massDefs.Length);
            Array.Copy(gravityDefs, 0, defs, massDefs.Length, gravityDefs.Length);

            Vector2 savedGravity = Physics2D.gravity;

            WorldRegistry.Initialize();
            SnapshotRoster.Initialize();

            RuleSystem.Initialize(new RuleGame.Editor.Rules.RuleVerifyHarness.TestCatalog(defs));
            RuleSystem sys = RuleSystem.Instance;
            if (sys == null) return "✗ RuleSystem 初始化失败";

            SnapshotSystem.Initialize(sys, SnapshotRoster.Instance);
            SnapshotSystem ss = SnapshotSystem.Instance;
            if (ss == null) return "✗ SnapshotSystem 初始化失败";

            GameObject a = new GameObject("SnapA");
            Rigidbody2D bodyA = a.AddComponent<Rigidbody2D>();
            bodyA.position = new Vector2(1f, 2f);
            EntityStatus statusA = a.AddComponent<EntityStatus>();
            statusA.ApplyData(new EntityStatusData { Energy = 30f, Health = 100f });

            GameObject b = new GameObject("SnapB");
            Rigidbody2D bodyB = b.AddComponent<Rigidbody2D>();
            bodyB.position = new Vector2(3f, 4f);

            SnapshotRoster.Instance.Register(new TestEntity(bodyA));
            SnapshotRoster.Instance.Register(new TestEntity(bodyB));

            int expired = 0;
            int restored = 0;
            sys.OnRuleExpired += _ => expired++;
            ss.OnRestored += () => restored++;

            // ==============================================================
            //  ① 单点采集 / 恢复：物理量、激活状态、规则表
            // ==============================================================
            h.Section("1 单点采集与恢复");

            Snapshot snap1 = ss.Capture();
            h.Check("采集到两个实体", snap1.Entities.Count == 2, snap1.Entities.Count.ToString());
            h.Check("激活状态并入 EntityState（不再有独立列表）",
                    snap1.Entities[0].ActiveSelf && snap1.Entities[1].ActiveSelf,
                    $"[{snap1.Entities[0].ActiveSelf},{snap1.Entities[1].ActiveSelf}]");
            h.Check("采集前规则表为空", snap1.RuleSlots != null && snap1.RuleSlots[(int)AttributeId.Mass] == null,
                    "Mass 槽位应为 null");

            // 世界动起来：位移 / 速度 / 禁用 / 挂规则
            bodyA.position = new Vector2(9f, 9f);
            bodyA.linearVelocity = new Vector2(5f, 0f);
            b.SetActive(false);
            RuleRequestResult r = sys.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player);
            h.Check("前置：质量增强规则请求成功", r.Succeeded, r.Status.ToString());

            ss.Restore(snap1);
            h.Check("位置恢复", bodyA.position == new Vector2(1f, 2f), bodyA.position.ToString());
            h.Check("速度恢复", bodyA.linearVelocity == Vector2.zero, bodyA.linearVelocity.ToString());
            h.Check("禁用的对象复活", b.activeSelf, b.activeSelf.ToString());
            h.Check("规则槽位回到空", !sys.Table.IsActive(AttributeId.Mass), "Mass 槽位应已清空");
            h.Check("属性值回到默认", sys.Attributes.Values.MassScale == 1f,
                    sys.Attributes.Values.MassScale.ToString());
            h.Check("恢复不触发 OnRuleExpired（契约：快照恢复是唯一例外）", expired == 0, $"expired={expired}");
            h.Check("恢复触发 OnRestored 一次", restored == 1, $"restored={restored}");

            // ==============================================================
            //  ② 采集时就是禁用的对象 —— 名册成员资格 = 生命周期（G1 回归）
            // ==============================================================
            h.Section("2 采集时禁用 → 恢复后仍禁用");

            b.SetActive(false);
            Snapshot disabledSnap = ss.Capture();
            h.Check("禁用对象仍在快照里", disabledSnap.Entities.Count == 2,
                    disabledSnap.Entities.Count.ToString());
            h.Check("它的 ActiveSelf 记为 false",
                    disabledSnap.Entities[1].ActiveSelf == false,
                    disabledSnap.Entities[1].ActiveSelf.ToString());

            b.SetActive(true);      // 之后被重新启用（例如脚本复活）
            h.Check("铺垫：对象被重新启用", b.activeSelf, b.activeSelf.ToString());

            ss.Restore(disabledSnap);
            h.Check("✅ 恢复后它重新变回禁用（旧实现这里会失败）", !b.activeSelf, b.activeSelf.ToString());

            b.SetActive(true);      // 复原，别影响后面的用例

            // ==============================================================
            //  ③ 剩余时间连槽位一起回退
            // ==============================================================
            h.Section("3 剩余时间回退");

            sys.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player);
            Snapshot snap2 = ss.Capture();
            sys.Tick(3f);       // 规则时限 10 → 7

            ActiveRule? afterTick = sys.Table.GetSlot(AttributeId.Mass);
            h.Check("前置：Tick 后剩余时间 ≈ 7", afterTick.HasValue && Mathf.Abs(afterTick.Value.Remaining - 7f) < 0.001f,
                    afterTick.HasValue ? afterTick.Value.Remaining.ToString() : "(空)");

            ss.Restore(snap2);
            ActiveRule? back = sys.Table.GetSlot(AttributeId.Mass);
            h.Check("恢复后剩余时间回到 10（世界回到那一刻）",
                    back.HasValue && Mathf.Abs(back.Value.Remaining - 10f) < 0.001f,
                    back.HasValue ? back.Value.Remaining.ToString() : "(空)");
            h.Check("恢复后槽位仍是 Enhance",
                    sys.Table.IsActive(AttributeId.Mass, RelationId.Enhance), "关系应保持");

            // ==============================================================
            //  ④ 实体状态（能量 / 生命）恢复
            // ==============================================================
            h.Section("4 实体状态恢复");

            statusA.ApplyData(new EntityStatusData { Energy = 30f, Health = 100f });
            Snapshot snap3 = ss.Capture();

            statusA.TryConsumeEnergy(30f);          // 能量 30 → 0
            statusA.ApplyHealthDelta(-40f);         // 生命 100 → 60
            h.Check("前置：能量已扣空", statusA.Energy == 0f, statusA.Energy.ToString());
            h.Check("前置：生命已受伤", statusA.Health == 60f, statusA.Health.ToString());

            ss.Restore(snap3);
            h.Check("能量恢复", statusA.Energy == 30f, statusA.Energy.ToString());
            h.Check("生命恢复", statusA.Health == 100f, statusA.Health.ToString());

            // ==============================================================
            //  ⑤ 规则效果重建（G2 回归）：重力写在引擎上，恢复不发事件，必须补
            // ==============================================================
            h.Section("5 规则效果重建（重力）");

            sys.Request(AttributeId.Gravity, RelationId.Revert, RuleSource.Player);
            h.Check("前置：重力反转生效", Physics2D.gravity.y > 0f, Physics2D.gravity.ToString());
            Snapshot gravSnap = ss.Capture();

            sys.Request(AttributeId.Gravity, RelationId.Nullify, RuleSource.Player);
            h.Check("前置：重力归零", Mathf.Approximately(Physics2D.gravity.y, 0f), Physics2D.gravity.ToString());

            ss.Restore(gravSnap);
            h.Check("✅ 恢复后重力回到「反转」—— 效果被重建，不只是规则表回去",
                    Physics2D.gravity.y > 0f, Physics2D.gravity.ToString());

            sys.Cancel(AttributeId.Gravity);
            h.Check("取消后重力回默认", Physics2D.gravity.y < 0f, Physics2D.gravity.ToString());

            // ==============================================================
            //  ⑥ 环形缓冲：序号单调、只读倒数第二张、按序号裁剪
            // ==============================================================
            h.Section("6 环形缓冲");

            ss.ClearBuffer();
            h.Check("缓冲为空时读不到倒数第二张", !ss.TryPeekSecondLast(out _), "应返回 false");
            h.Check("缓冲为空时也读不到最旧的一张", !ss.TryPeekOldest(out _), "应返回 false");

            int s0 = ss.CaptureToBuffer();
            h.Check("只有一张时也读不到倒数第二张", !ss.TryPeekSecondLast(out _), "应返回 false");
            h.Check("只有一张时能读到最旧的一张（反转的兜底）",
                    ss.TryPeekOldest(out Snapshot only) && only.Seq == s0,
                    only != null ? only.Seq.ToString() : "(无)");

            int s1 = ss.CaptureToBuffer();
            int s2 = ss.CaptureToBuffer();
            // ⚠️ 断言用相对序号：TimeRule 的第一帧垫片会先占用一个序号，绝对值不可假设。
            h.Check("序号单调递增", s1 == s0 + 1 && s2 == s1 + 1, $"seq=[{s0},{s1},{s2}]");

            h.Check("倒数第二张是 s1", ss.TryPeekSecondLast(out Snapshot peek) && peek.Seq == s1,
                    peek != null ? peek.Seq.ToString() : "(无)");

            int s3 = ss.CaptureToBuffer();          // 比 s1 新
            ss.DiscardNewerThan(s1);                // 丢弃 s2、s3，保留 s0、s1
            h.Check("按序号丢弃更新的快照", ss.TryPeekSecondLast(out Snapshot afterDiscard)
                                              && afterDiscard.Seq == s0,
                    afterDiscard != null ? afterDiscard.Seq.ToString() : "(无)");

            int s4 = ss.CaptureToBuffer();
            h.Check("裁剪不影响序号连续性（s4 = s3 + 1）", s4 == s3 + 1, $"{s4} vs {s3}+1");

            ss.ClearBuffer();
            h.Check("清空后读不到", !ss.TryPeekSecondLast(out _), "应返回 false");

            // ==============================================================
            //  ⑦ 定点快照（恒定用）
            // ==============================================================
            h.Section("7 定点快照");

            bodyA.position = new Vector2(11f, 22f);
            int pin = ss.Pin();
            h.Check("Pin 返回非零句柄", pin > 0, pin.ToString());

            bodyA.position = new Vector2(33f, 44f);
            h.Check("恢复到定点成功", ss.TryRestorePinned(pin), "失败");
            h.Check("位置回到定点", bodyA.position == new Vector2(11f, 22f), bodyA.position.ToString());

            h.Check("错误句柄恢复失败", !ss.TryRestorePinned(pin + 999), "返回了 true");

            ss.Unpin(pin);
            h.Check("Unpin 后恢复失败", !ss.TryRestorePinned(pin), "返回了 true");

            // ==============================================================
            //  清理
            // ==============================================================
            Physics2D.gravity = savedGravity;
            UnityEngine.Object.DestroyImmediate(a);
            UnityEngine.Object.DestroyImmediate(b);
            foreach (RuleDef d in defs) UnityEngine.Object.DestroyImmediate(d);
            SnapshotSystem.Reset();
            SnapshotRoster.Reset();
            RuleSystem.Reset();
            WorldRegistry.Reset();

            return h.Report("快照系统验证");
        }
    }
}
