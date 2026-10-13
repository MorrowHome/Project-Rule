using UnityEngine;
using UnityEditor;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Rules
{
    /// <summary>
    /// 时间规则的**实际效果**验证器。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="TimeRuleVerifier"/> 的分工：那个验「值写对没有」，这个验
    /// <b>「世界真的变了没有」</b> —— 用**真实 RuleCatalog**（含策划配好的 5 个资产）
    /// 和真实 GameObject，看位置 / 生命 / 能量 / <c>Time.timeScale</c> 的实际变化。</para>
    ///
    /// <para><b>关键设计</b>：回溯那两条每次都<b>同时</b>断言
    /// 「不该回退的能量没动」+「该回退的生命回退了」。
    /// 只测前者的坏处是：一个完全不工作的 <c>Restore</c> 也能让断言通过。</para>
    ///
    /// <para>⚠️ 必须在 <b>Play 模式</b>下调用。它会把单例换成真实目录、结束后全部 Reset，
    /// 所以请在一次专门的验证会话里跑，别在正常游玩中途跑。</para>
    /// </remarks>
    public static class TimeEffectVerifier
    {
        private const float Tol = 0.001f;

        private const string CatalogPath = "Assets/_Project/Data/Rules/RuleCatalog.asset";

        public static string Run()
        {
            var h = new RuleVerifyHarness();

            var catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>(CatalogPath);
            if (catalog == null) return "✗ 找不到 RuleCatalog.asset";

            // ---- 先把 5 个时间资产确实存在查一遍（这是策划刚配的）----
            h.Section("0 目录里 5 个时间资产");
            RelationId[] rels =
            {
                RelationId.Revert, RelationId.Nullify, RelationId.Constant,
                RelationId.Enhance, RelationId.Weaken,
            };
            foreach (RelationId rel in rels)
            {
                RuleDef d = catalog.Get(AttributeId.Time, rel);
                h.Check($"Time/{rel} 有资产", d != null, "目录里查不到");
                if (d != null)
                {
                    h.Check($"Time/{rel} 时长在 5-15 秒档（实际 {d.Duration:0.#}）",
                            d.Duration >= 5f && d.Duration <= 15f, d.Duration.ToString("0.#"));
                }
            }

            // ---- 起环境 ----
            WorldRegistry.Initialize();
            TimeManager.Initialize();
            RuleSystem.Initialize(catalog);
            RuleSystem sys = RuleSystem.Instance;
            if (sys == null) return "✗ RuleSystem 初始化失败";

            TimeManager tm = TimeManager.Instance;
            var tsc = sys as ITimeRewindControl;

            float savedTimeScale = UnityEngine.Time.timeScale;
            var savedSimMode = Physics2D.simulationMode;

            // ---- 造一个真实实体 ----
            var go = new GameObject("TimeEffectBox");
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;     // 位置完全由我们决定
            rb.position = new Vector2(0f, 0f);
            go.AddComponent<BoxCollider2D>();
            var status = go.AddComponent<EntityStatus>();
            var body = go.AddComponent<RuleAffectedBody>();   // 登记进 WorldRegistry

            SnapshotSystem.Initialize(sys, WorldRegistry.Instance);

            h.Section("0b 实体就位");
            h.Check("实体已登记", body.IsRegistered && WorldRegistry.Instance.Count >= 1,
                    $"Count={WorldRegistry.Instance.Count}");

            // ==============================================================
            //  ① 参数关系：看引擎真正读的那个值
            // ==============================================================
            h.Section("1 参数关系对引擎的实际影响");

            sys.Request(AttributeId.Time, RelationId.Enhance, RuleSource.Player);
            h.Check("增强 → UnityEngine.Time.timeScale = 2（物理与动画都会跟着快）",
                    Mathf.Abs(UnityEngine.Time.timeScale - TimeRule.EnhancedScale) < Tol,
                    UnityEngine.Time.timeScale.ToString("0.###"));
            h.Check("增强不动 simulationMode（暂停才是它的事）",
                    Physics2D.simulationMode == savedSimMode, Physics2D.simulationMode.ToString());

            sys.Request(AttributeId.Time, RelationId.Weaken, RuleSource.Player);
            h.Check("削弱 → timeScale = 0.5",
                    Mathf.Abs(UnityEngine.Time.timeScale - TimeRule.WeakenedScale) < Tol,
                    UnityEngine.Time.timeScale.ToString("0.###"));

            sys.Request(AttributeId.Time, RelationId.Nullify, RuleSource.Player);
            h.Check("失效 → 时间机制开关关闭", !tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());
            h.Check("失效 → timeScale 回到 1（动画不受影响，这正是矩阵说的「动画不变」）",
                    Mathf.Abs(UnityEngine.Time.timeScale - 1f) < Tol,
                    UnityEngine.Time.timeScale.ToString("0.###"));

            sys.ClearAll();

            // ==============================================================
            //  ② 恒定：定点回溯 —— 世界退回去，能量不退
            // ==============================================================
            h.Section("2 恒定：定点回溯的实际效果");

            var p0 = new Vector2(-30f, 7f);
            rb.position = p0;
            status.TryConsumeEnergy(10f);        // 能量 100 → 90
            status.ApplyHealthDelta(-20f);       // 生命 100 → 80
            h.Check("铺垫：能量 90 / 生命 80",
                    Mathf.Abs(status.Energy - 90f) < Tol && Mathf.Abs(status.Health - 80f) < Tol,
                    $"能量={status.Energy}, 生命={status.Health}");

            RuleRequestResult rc = sys.Request(AttributeId.Time, RelationId.Constant, RuleSource.Player);
            h.Check("恒定生效", rc.Succeeded, rc.Status.ToString());
            h.Check("回溯窗口打开", tsc != null && tsc.IsWindowOpen, tsc == null ? "无接口" : tsc.IsWindowOpen.ToString());
            if (tsc == null) { Cleanup(go, savedTimeScale, savedSimMode); return h.Report("时间效果验证"); }

            // 世界大幅改变：位置、生命、能量
            rb.position = new Vector2(120f, -45f);
            status.TryConsumeEnergy(60f);        // 能量 90 → 30
            status.ApplyHealthDelta(-30f);       // 生命 80 → 50
            h.Check("铺垫：位置 / 生命 / 能量 都变了",
                    rb.position.x > 100f && Mathf.Abs(status.Health - 50f) < Tol && Mathf.Abs(status.Energy - 30f) < Tol,
                    $"pos={rb.position}, 生命={status.Health}, 能量={status.Energy}");

            bool rewound = tsc.TryRewind();
            h.Check("回溯执行成功", rewound, "失败");

            h.Check("✅ 位置退回定点 —— 世界真的回退了",
                    Vector2.Distance(rb.position, p0) < Tol, rb.position.ToString("0.###"));
            h.Check("✅ 生命退回 80 —— 世界确实回退了（不是 Restore 没干活）",
                    Mathf.Abs(status.Health - 80f) < Tol, status.Health.ToString("0.###"));
            h.Check("✅ 能量仍是 30 —— 回溯不退能量（KeepEnergy 生效）",
                    Mathf.Abs(status.Energy - 30f) < Tol, status.Energy.ToString("0.###"));

            // 不限次数
            rb.position = new Vector2(200f, 200f);
            h.Check("第二次回溯也成功（不限次数）", tsc.TryRewind(), "失败");
            h.Check("✅ 第二次也回到同一个定点",
                    Vector2.Distance(rb.position, p0) < Tol, rb.position.ToString("0.###"));

            sys.ClearAll();

            // ==============================================================
            //  ③ 反转：倒回 5-10 秒前 —— 退世界，不退能量
            // ==============================================================
            h.Section("3 反转：环形缓冲的实际效果");

            SnapshotSystem.Instance.ClearBuffer();

            // 采三张：这样「倒数第二张」才有意义 —— 既不是最新那张（那等于没回溯），
            // 也不是最旧那张。§4.10「回溯范围 5-10 秒」就是靠跳过最新那张实现的。
            var pa = new Vector2(-100f, 0f);
            var pb = new Vector2(50f, 20f);
            var pc = new Vector2(300f, -80f);
            var now = new Vector2(500f, 400f);

            rb.position = pa;
            SnapshotSystem.Instance.CaptureToBuffer();      // 最旧
            rb.position = pb;
            SnapshotSystem.Instance.CaptureToBuffer();      // 倒数第二张 ← 应该回到这里
            rb.position = pc;
            SnapshotSystem.Instance.CaptureToBuffer();      // 最新

            // 最新那张之后世界又变了（真实玩法里「现在」总是在最后一张之后）
            rb.position = now;
            status.TryConsumeEnergy(status.Energy);         // 能量 30 → 0
            h.Check("铺垫：位置在 now、能量 0",
                    Vector2.Distance(rb.position, now) < Tol && Mathf.Abs(status.Energy) < Tol,
                    $"pos={rb.position}, 能量={status.Energy}");

            RuleRequestResult rr = sys.Request(AttributeId.Time, RelationId.Revert, RuleSource.Player);
            h.Check("反转生效", rr.Succeeded, rr.Status.ToString());
            h.Check("✅ 位置回到 PB（倒数第二张）—— 不是最新那张 PC，也不是最旧的 PA",
                    Vector2.Distance(rb.position, pb) < Tol, rb.position.ToString("0.###"));
            h.Check("✅ 能量仍是 0 —— 反转同样不退能量",
                    Mathf.Abs(status.Energy) < Tol, status.Energy.ToString("0.###"));

            // ==============================================================
            //  ④ 收尾：窗口关掉后必须彻底失效
            // ==============================================================
            h.Section("4 收尾");

            sys.ClearAll();
            h.Check("ClearAll 后窗口关闭", !tsc.IsWindowOpen, tsc.IsWindowOpen.ToString());
            h.Check("ClearAll 后回溯失效", !tsc.TryRewind(), "返回 true");
            h.Check("ClearAll 后 timeScale 回 1",
                    Mathf.Abs(UnityEngine.Time.timeScale - 1f) < Tol,
                    UnityEngine.Time.timeScale.ToString("0.###"));
            h.Check("ClearAll 后时间机制开关回 true", tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());

            Cleanup(go, savedTimeScale, savedSimMode);
            return h.Report("时间效果验证");
        }

        /// <summary>别把全局时间状态和单例留给下一个验证器。</summary>
        private static void Cleanup(GameObject go, float savedTimeScale, SimulationMode2D savedSimMode)
        {
            if (go != null) Object.DestroyImmediate(go);

            UnityEngine.Time.timeScale = savedTimeScale;
            Physics2D.simulationMode = savedSimMode;

            SnapshotSystem.Reset();
            TimeManager.Reset();
            RuleSystem.Reset();
            WorldRegistry.Reset();
        }
    }
}
