using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Rules
{
    /// <summary>
    /// 属性规则验证器的一键入口。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>必须在 Play 模式下运行</b>：<c>RuleAffectedBody</c> 的登记与应用发生在
    /// <c>Awake</c> / <c>OnEnable</c>，编辑模式下不会触发，跑出来是空的。</para>
    /// <para>各验证器结束时都会 <c>RuleSystem.Reset()</c>，所以跑完之后当前 Play 会话的规则系统
    /// 处于「未初始化」状态 —— 看完结果重进 Play 即可恢复正常游玩。</para>
    /// <para>失败的断言按 <c>LogError</c> 输出（控制台标红、并计入错误数），全绿则普通日志。</para>
    /// </remarks>
    public static class RuleVerifyMenu
    {
        [MenuItem("规则游戏/验证/跑全部属性规则")]
        public static void RunAll()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[规则验证] 先进入 Play 模式再跑：RuleAffectedBody 的登记与应用发生在 Awake / OnEnable，编辑模式不触发。");
                return;
            }

            string report = RuleVerifyHarness.RunAll();

            if (report.Contains("✗")) Debug.LogError(report);
            else Debug.Log(report);
        }
    }
}
