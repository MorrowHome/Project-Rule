using UnityEditor;
using UnityEngine;
using RuleGame.Editor.Rules;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 滑轨档位映射的验证器：把「质量规则那 5 个关系」与「三档高度」的对应关系钉死。
    /// </summary>
    /// <remarks>
    /// <para><b>不需要 Play 模式</b>：判据是纯函数 <see cref="RailLift.ResolveTier"/>（不读字段、不碰 Unity 对象），
    /// 摆位那部分也只是给三段高度排序。所以改完规则数值或档位映射，随时点一下就能回归。</para>
    /// <para>用法：菜单 规则游戏/验证/滑轨档位映射。失败的断言按 <c>LogError</c> 输出。</para>
    /// </remarks>
    public static class RailLiftVerifier
    {
        [MenuItem("规则游戏/验证/滑轨档位映射")]
        public static void Run()
        {
            string report = Verify();

            if (report.Contains("✗")) Debug.LogError(report);
            else Debug.Log(report);
        }

        /// <summary>跑一遍并返回报告（不含菜单入口，方便别处复用）。</summary>
        public static string Verify()
        {
            var h = new RuleVerifyHarness();

            // ==============================================================
            //  ① 五个关系 → 三档
            // ==============================================================
            h.Section("1 质量关系 → 档位");

            // 取值与 MassRule 的常量一致：正常 1 / 增强 3 / 削弱 0.3 / 失效 0.05。
            var cases = new (string Name, float Scale, bool Inverted, float BaseMass, RailLift.Tier Want)[]
            {
                ("无规则 / 恒定（1 倍）",     1f,    false, 1f,   RailLift.Tier.Middle),
                ("增强（3 倍 = 变重）",        3f,    false, 1f,   RailLift.Tier.Low),
                ("削弱（0.3 倍 = 变轻）",      0.3f,  false, 1f,   RailLift.Tier.High),
                ("失效（0.05 倍 = 极轻）",     0.05f, false, 1f,   RailLift.Tier.High),
                ("增强：档位与基准质量无关",    3f,    false, 0.3f, RailLift.Tier.Low),
                ("反转 + 基准 3（重的变轻）",   1f,    true,  3f,   RailLift.Tier.High),
                ("反转 + 基准 0.3（轻的变重）", 1f,    true,  0.3f, RailLift.Tier.Low),
                ("反转 + 基准 1（不偏不倚）",   1f,    true,  1f,   RailLift.Tier.Middle),
                ("反转优先于残留倍率",          3f,    true,  3f,   RailLift.Tier.High),
            };

            foreach ((string name, float scale, bool inverted, float baseMass, RailLift.Tier want) in cases)
            {
                AttributeValues values = AttributeValues.Default;
                values.MassScale = scale;
                values.MassInverted = inverted;

                RailLift.Tier got = RailLift.ResolveTier(values, baseMass);
                h.Check($"{name} → {want}", got == want, $"得到 {got}");
            }

            // ==============================================================
            //  ② 三档高度：低 < 中 < 高
            // ==============================================================
            h.Section("2 三档高度");

            // ⚠️ 先建**未激活**的物体再加组件：否则 OnEnable 会在编辑模式下跑，
            //    组件找不到滑块会往控制台丢一条「没有滑块」的报错，污染验证输出。
            var go = new GameObject("RailLiftVerify") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            var lift = go.AddComponent<RailLift>();

            lift.SetTiers(2f, 1.5f, 4f);
            h.Check("中档 = 2", Mathf.Abs(lift.HeightOf(RailLift.Tier.Middle) - 2f) < RuleVerifyHarness.Tolerance,
                    lift.HeightOf(RailLift.Tier.Middle).ToString("0.###"));
            h.Check("高档 = 中 + 升 = 3.5",
                    Mathf.Abs(lift.HeightOf(RailLift.Tier.High) - 3.5f) < RuleVerifyHarness.Tolerance,
                    lift.HeightOf(RailLift.Tier.High).ToString("0.###"));
            h.Check("低档 = 中 − 沉 = −2（上下可以不对称）",
                    Mathf.Abs(lift.HeightOf(RailLift.Tier.Low) + 2f) < RuleVerifyHarness.Tolerance,
                    lift.HeightOf(RailLift.Tier.Low).ToString("0.###"));

            lift.SetTiers(0f, -1f, -1f);
            h.Check("负的高度差被夹到 0（不会倒过来走）",
                    lift.RiseHeight == 0f && lift.SinkDepth == 0f,
                    $"升={lift.RiseHeight}, 沉={lift.SinkDepth}");

            Object.DestroyImmediate(go);

            return h.Report("滑轨档位映射验证");
        }
    }
}
