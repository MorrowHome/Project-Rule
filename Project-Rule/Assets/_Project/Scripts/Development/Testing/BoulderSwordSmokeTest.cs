using System.Collections;
using System.Text;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Development.Testing
{
    /// <summary>
    /// 巨石断剑机关的**冒烟测试**：在 Play 模式里把策划要的整条流程走一遍，逐条打 ✓ / ✗。
    /// </summary>
    /// <remarks>
    /// <para>流程 = ①「重力 × 反转」把巨石举起来 → ② 撞响天花板按钮并被震裂 →
    /// ③ 重力恢复、落地碎开撒碎块 → ④ 露出剑 → ⑤「摩擦 × 反转」之后走近把剑拔出来。</para>
    ///
    /// <para>它住在 Development 程序集（白盒代码，与生产代码隔离），由试验场景
    /// <c>Scenes/Test_BoulderSword.unity</c> 里的 <c>SmokeTest</c> 物体挂载 ——
    /// 场景由菜单 <c>Tools/规则游戏/地图组件/⑫b 生成试验场景（巨石断剑）</c> 生成。
    /// 跑法：打开该场景 → Play → 看 Console。</para>
    ///
    /// <para>结果打到 Console：全过是普通 <c>Log</c>，有一条失败就是 <c>LogError</c>（免得被忽略）。</para>
    /// </remarks>
    public sealed class BoulderSwordSmokeTest : MonoBehaviour
    {
        [Header("被测对象（由试验场景生成工具接线）")]
        [Tooltip("巨石。")]
        [SerializeField] private GravityBoulder _boulder;

        [Tooltip("天花板上的按钮（积木城的 CityButton，**不在机关预制体里**）：巨石顶到它就震裂。")]
        [SerializeField] private CitySwitch _armButton;

        [Tooltip("地面上的解锁按钮：玩家走进去，巨石才从静态变成动态。\n" +
                 "在那之前放多少次重力反转，它都不会动。")]
        [SerializeField] private CitySwitch _releaseButton;

        [Tooltip("藏在石头里的剑。")]
        [SerializeField] private SwordInStone _sword;

        [Tooltip("假玩家：站在剑边上那个。**故意不是真玩家** —— 真玩家会被重力规则一起掀飞，\n" +
                 "而本测试只想验证「有人站在剑边」这一条。")]
        [SerializeField] private Transform _puller;

        [Header("规则")]
        [Tooltip("规则目录。本测试用它**独占**初始化规则系统：先把场景里别的 RuleSystemDriver 关掉，" +
                 "否则两个驱动会各 Tick 一次，规则时限以两倍速度流逝，结果不可复现。")]
        [SerializeField] private RuleCatalog _catalog;

        [Header("节奏")]
        [Tooltip("每一步最多等多久（秒）。超过就判失败，不会卡死。")]
        [SerializeField, Min(1f)] private float _timeout = 12f;

        private readonly StringBuilder _log = new StringBuilder();
        private int _pass;
        private int _fail;

        // ==================================================================
        //  主流程
        // ==================================================================

        private IEnumerator Start()
        {
            // 本测试要**独占**规则系统。正式关卡里常驻着 RuleSystemDriver（GameRoot / LevelRuntime 那套），
            // 如果它也活着，两个驱动会各 Tick 一次 —— 规则时限以两倍速度流逝，测试就不可复现了。
            // ⚠️ 只关**运行期**的组件，一个场景文件都不改。
            foreach (RuleSystemDriver driver in Object.FindObjectsByType<RuleSystemDriver>())
            {
                driver.enabled = false;
            }

            if (_catalog != null) RuleSystem.Initialize(_catalog);
            TimeManager.EnsureInitialized();

            yield return WaitForRules();

            IRuleSystem rules = RuleSystem.Instance;
            if (!Check("规则系统就绪（场景里有 RuleSystemDriver + RuleCatalog）", rules != null, "RuleSystem.Instance 是 null"))
            {
                Finish();
                yield break;
            }

            if (!Check("试验场景接好了线（巨石 / 两个按钮 / 剑 / 假玩家）",
                       _boulder != null && _armButton != null && _releaseButton != null
                       && _sword != null && _puller != null,
                       "有引用是空的，重新跑一遍生成试验场景的菜单"))
            {
                Finish();
                yield break;
            }

            // ---- 开局 ----------------------------------------------------
            Check("开局：巨石完整（没被震裂）", _boulder.CurrentState == GravityBoulder.State.Intact, StateText());
            Check("开局：巨石**锁死**（静态、不吃重力）", _boulder.IsAnchored, "开局应该是锁着的");
            Check("开局：剑藏着看不见", !_sword.gameObject.activeSelf, "剑是启用状态");
            Check("开局：天花板按钮没被按下", !_armButton.IsOn, "按钮一开始就是打开的");
            Check("开局：解锁按钮没被按下", !_releaseButton.IsOn, "解锁按钮一开始就是打开的");
            Check("开局：剑锁死（摩擦正常）", !_sword.IsFree, "IsFree 一开始就是 true");

            float startY = _boulder.transform.position.y;

            // ---- ① 锁着的时候，放多少次重力反转都举不动它 ------------------
            // ⚠️ 「不会提前碎掉」的回归断言：玩家赶路时会反复放重力反转，
            //    石头在解锁之前必须纹丝不动、也不能被打上「开裂」。
            RuleRequestResult gravity = rules.Request(AttributeId.Gravity, RelationId.Revert, RuleSource.Player);
            Check("①「重力 × 反转」生效", gravity.Succeeded, gravity.Status.ToString());

            yield return new WaitForSeconds(1.2f);

            Check("① 锁死时纹丝不动（没被举起来）",
                  Mathf.Abs(_boulder.transform.position.y - startY) < 0.05f,
                  $"锁死却移动到了 y = {_boulder.transform.position.y:0.##}（起点 {startY:0.##}）");
            Check("① 锁死时不会碎、也不会开裂",
                  _boulder.CurrentState == GravityBoulder.State.Intact && !_boulder.IsBreakable,
                  $"形态 {_boulder.CurrentState}，可碎={_boulder.IsBreakable}");

            rules.Cancel(AttributeId.Gravity);

            // ---- ② 玩家踩上解锁按钮 → 巨石解锁 ----------------------------
            _puller.position = _releaseButton.transform.position + new Vector3(0.1f, 0.1f, 0f);
            yield return WaitForFixedFrames(4);

            Check("② 解锁按钮被玩家踩下", _releaseButton.IsOn, "按钮没打开");
            Check("② 巨石解锁（静态 → 动态）", !_boulder.IsAnchored, "还锁着");

            // ---- ③ 现在放重力反转才举得起来 --------------------------------
            gravity = rules.Request(AttributeId.Gravity, RelationId.Revert, RuleSource.Player);
            Check("③「重力 × 反转」再次生效", gravity.Succeeded, gravity.Status.ToString());

            yield return WaitUntil(() => _boulder.transform.position.y > startY + 2f);

            Check("③ 这次被举起来了（比起点高 2 单位以上）",
                  _boulder.transform.position.y > startY + 2f,
                  $"y = {_boulder.transform.position.y:0.##}（起点 {startY:0.##}）");

            // ---- ④ 碰到天花板按钮，被震裂 --------------------------------
            yield return WaitUntil(() => _armButton.IsOn);

            Check("④ 巨石碰到天花板按钮（CityButton 被按下）", _armButton.IsOn, "按钮还没打开");
            Check("④ 按钮把巨石震裂", _boulder.CurrentState == GravityBoulder.State.Armed, StateText());

            // ---- ④′ 顶在天花板上那一段**不能碎** --------------------------
            // ⚠️ 这条是「砸到按钮上不碎」的回归断言：按钮是 Trigger，巨石穿过它继续上升、
            // 顶在天花板上；就在那个位置停 2.5 秒（重力还是反的），它必须还是「开裂」而不是「已碎」。
            yield return new WaitForSeconds(2.5f);

            float topY = _boulder.transform.position.y;
            Check("④′ 顶到天花板上了（升得比按钮还高）", topY > startY + 4.5f,
                  $"只升到 y = {topY:0.##}（起点 {startY:0.##}）");
            Check("④′ 顶在天花板上 2.5 秒没有碎（要落下来才碎）",
                  _boulder.CurrentState == GravityBoulder.State.Armed,
                  $"形态 {_boulder.CurrentState}（撞到「{_boulder.LastContactName}」，从下面接住={_boulder.LastContactWasFromBelow}）");

            // ---- ⑤ 重力恢复，落地碎开 ------------------------------------
            rules.Cancel(AttributeId.Gravity);

            yield return WaitUntil(() => _boulder.CurrentState == GravityBoulder.State.Shattered);

            Check("⑤ 落回地面碎开", _boulder.CurrentState == GravityBoulder.State.Shattered,
                  $"没碎（落地速度 {_boulder.LastImpactSpeed:0.##}，阈值看「碎裂条件」）");

            int fragments = CountFragments();
            Check("⑤ 撒出一圈碎块（≥ 4 块）", fragments >= 4, $"场上只有 {fragments} 块碎块");

            // ---- ⑥ 剑露出来了 --------------------------------------------
            Check("⑥ 碎块里露出剑", _sword.gameObject.activeSelf, "剑还是隐藏的");
            Check("⑥ 露出来时剑仍然是锁死的", !_sword.IsFree, "IsFree 不该因为碎开而变 true");

            // ---- ⑦ 摩擦反转之前，站在剑边也拔不动 ------------------------
            _puller.position = _sword.transform.position + new Vector3(0.6f, 0.6f, 0f);
            yield return WaitForFixedFrames(3);

            Check("⑦ 假玩家站到剑边（距离闸门打开）", _sword.PlayerInRange,
                  $"假玩家在 {_puller.position}，剑在 {_sword.transform.position}，取剑半径 1.6");
            Check("⑦ 摩擦正常时手动拔也被拒", !_sword.TryPull(ignoreDistance: true), "锁死状态不该拔得动");
            Check("⑦ 摩擦正常时不会自动拔出", !_sword.IsExtracted, "剑自己飞出来了");

            // ---- ⑧ 摩擦反转，走近就拔出来 --------------------------------
            RuleRequestResult friction = rules.Request(AttributeId.Friction, RelationId.Revert, RuleSource.Player);
            Check("⑧「摩擦 × 反转」生效", friction.Succeeded, friction.Status.ToString());

            yield return WaitForFixedFrames(2);
            Check("⑧ 剑解锁", _sword.IsFree, "FrictionInverted 应该是 true");

            yield return WaitUntil(() => _sword.IsExtracted);
            Check("⑧ 剑被拔出来（没调 TryPull，靠走近触发）", _sword.IsExtracted, "等超时了还没拔出来");

            // 收尾：把规则还回去，别把试验场景留在「摩擦反转」里。
            rules.Cancel(AttributeId.Friction);

            Finish();
        }

        // ==================================================================
        //  断言与等待
        // ==================================================================

        private bool Check(string label, bool ok, string detail = null)
        {
            if (ok)
            {
                _pass++;
                _log.Append("\n  ✓ ").Append(label);
            }
            else
            {
                _fail++;
                _log.Append("\n  ✗ ").Append(label);
                if (!string.IsNullOrEmpty(detail)) _log.Append("   → ").Append(detail);
            }

            return ok;
        }

        private IEnumerator WaitUntil(System.Func<bool> condition)
        {
            float deadline = Time.time + _timeout;
            while (!condition() && Time.time < deadline) yield return null;
        }

        private IEnumerator WaitForFixedFrames(int count)
        {
            for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate();
        }

        private IEnumerator WaitForRules()
        {
            float deadline = Time.time + 3f;
            while (RuleSystem.Instance == null && Time.time < deadline) yield return null;
        }

        private static int CountFragments()
        {
            return Object.FindObjectsByType<BoulderFragment>().Length;
        }

        private string StateText()
        {
            return _boulder == null ? "巨石引用是空的" : "当前形态 " + _boulder.CurrentState;
        }

        private void Finish()
        {
            string report = $"===== 巨石断剑冒烟测试：通过 {_pass}，失败 {_fail} ====={_log}";

            // 同时落一份到 Logs/：Console 会被别的系统刷屏（多场景同时加载时
            // 「2 audio listeners」是每帧一条），报告很容易被冲掉 —— 与其余验证脚本同一做法。
            try
            {
                string path = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "../Logs/BoulderSwordSmokeTest.txt"));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, report);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("[BoulderSwordSmokeTest] 写 Logs 失败：" + error.Message, this);
            }

            if (_fail == 0) Debug.Log(report, this);
            else Debug.LogError(report, this);
        }
    }
}
