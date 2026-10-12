using UnityEditor;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Rules
{
    /// <summary>
    /// 因果规则的验证器（规则侧：只验它写进属性管理器的值）。
    /// </summary>
    /// <remarks>
    /// <para>因果**没有行为型关系**，五个关系全是对 <c>IAttributeManager</c> 的写入，
    /// 所以这里不需要像时间那样造 GameObject / 快照。</para>
    /// <para>行为侧（掷骰子、互换对象、扩散）在伤害处理模块，由
    /// <c>DamageProcessorVerifier</c> 验证。</para>
    /// <para>⚠️ 必须在 <b>Play 模式</b>下调用。</para>
    /// </remarks>
    public static class CausalityRuleVerifier
    {
        private const float Tol = RuleVerifyHarness.Tolerance;

        private const string CatalogPath = "Assets/_Project/Data/Rules/RuleCatalog.asset";

        public static string Run()
        {
            var h = new RuleVerifyHarness();
            RuleDef[] defs = RuleVerifyHarness.MakeDefs(AttributeId.Causality, 10f);

            RuleSystem.Initialize(new RuleVerifyHarness.TestCatalog(defs));
            RuleSystem sys = RuleSystem.Instance;
            if (sys == null) return "✗ RuleSystem 初始化失败";

            IAttributeManager a = sys.Attributes;

            // ==============================================================
            //  ① 五个关系各写对字段
            // ==============================================================
            h.Section("1 五个关系");

            sys.Request(AttributeId.Causality, RelationId.Constant, RuleSource.Player);
            h.Check("恒定 → 因果恢复正常（五个字段全默认）", IsDefault(a.Values), Dump(a.Values));

            sys.Request(AttributeId.Causality, RelationId.Nullify, RuleSource.Player);
            h.Check("失效 → 倍率随机置位", a.Values.CausalityRandom, Dump(a.Values));
            h.Check("失效 → 恢复共享置位", a.Values.CausalityRecoveryShared, Dump(a.Values));
            h.Check("失效 → 不动缩放", Mathf.Abs(a.Values.CausalityScale - 1f) < Tol, Dump(a.Values));

            sys.Request(AttributeId.Causality, RelationId.Revert, RuleSource.Player);
            h.Check("反转 → 作用对象转换置位", a.Values.CausalityInverted, Dump(a.Values));
            h.Check("反转 → 随机没跟着被点亮", !a.Values.CausalityRandom, Dump(a.Values));

            sys.Request(AttributeId.Causality, RelationId.Enhance, RuleSource.Player);
            h.Check("增强 → 扩散置位", a.Values.CausalitySpread, Dump(a.Values));

            sys.Request(AttributeId.Causality, RelationId.Weaken, RuleSource.Player);
            h.Check($"削弱 → 缩放 = {CausalityRule.WeakenedScale}",
                    Mathf.Abs(a.Values.CausalityScale - CausalityRule.WeakenedScale) < Tol, Dump(a.Values));
            h.Check("削弱 → 扩散没跟着被点亮", !a.Values.CausalitySpread, Dump(a.Values));

            // ==============================================================
            //  ② 覆盖时不残留 —— 这是最容易被漏掉的一类 bug
            // ==============================================================
            h.Section("2 覆盖不残留");

            sys.Request(AttributeId.Causality, RelationId.Nullify, RuleSource.Player);
            h.Check("铺垫：失效三个标志全亮",
                    a.Values.CausalityRandom && a.Values.CausalityRecoveryShared && !a.Values.CausalityInverted,
                    Dump(a.Values));

            sys.Request(AttributeId.Causality, RelationId.Enhance, RuleSource.Player);
            h.Check("从「失效」覆盖到「增强」后，随机的两个标志被清掉",
                    !a.Values.CausalityRandom && !a.Values.CausalityRecoveryShared, Dump(a.Values));
            h.Check("且缩放回到 1（失效没写过缩放，但增强要保证它是 1）",
                    Mathf.Abs(a.Values.CausalityScale - 1f) < Tol, Dump(a.Values));

            // ==============================================================
            //  ③ ClearAll
            // ==============================================================
            h.Section("3 ClearAll");

            sys.Request(AttributeId.Causality, RelationId.Weaken, RuleSource.Player);
            sys.ClearAll();
            h.Check("ClearAll 后因果全默认", IsDefault(a.Values), Dump(a.Values));

            RuleSystem.Reset();
            foreach (RuleDef d in defs) Object.DestroyImmediate(d);

            // ==============================================================
            //  ④ 真实目录：30 个格位是否全部可达
            // ==============================================================
            h.Section("4 真实目录覆盖度（6 属性 × 5 关系）");

            var catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>(CatalogPath);
            if (catalog == null) { h.Check("找得到 RuleCatalog.asset", false, "找不到"); return h.Report("因果规则验证"); }

            int missing = 0;
            string missingList = "";
            foreach (AttributeId attr in System.Enum.GetValues(typeof(AttributeId)))
            {
                foreach (RelationId rel in System.Enum.GetValues(typeof(RelationId)))
                {
                    if (catalog.Get(attr, rel) != null) continue;
                    missing++;
                    missingList += (missingList.Length > 0 ? ", " : "") + attr + "/" + rel;
                }
            }
            h.Check("30 个格位全部有资产", missing == 0, missing == 0 ? "30/30" : $"缺 {missing} 个：{missingList}");

            return h.Report("因果规则验证");
        }

        private static bool IsDefault(AttributeValues v)
            => Mathf.Abs(v.CausalityScale - 1f) < Tol
            && !v.CausalityInverted && !v.CausalityRandom
            && !v.CausalityRecoveryShared && !v.CausalitySpread;

        private static string Dump(AttributeValues v)
            => $"缩放={v.CausalityScale:0.##} 反转={v.CausalityInverted} 随机={v.CausalityRandom} 恢复共享={v.CausalityRecoveryShared} 扩散={v.CausalitySpread}";
    }
}
