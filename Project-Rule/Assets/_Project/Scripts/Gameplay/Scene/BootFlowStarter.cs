using UnityEngine;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// Boot 场景里的启动开关：让常驻宿主开始世界流式加载。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不写进 <see cref="GameRoot"/>.Start</b>：<see cref="GameRoot"/> 是常驻对象，
    /// 调试时把它拖进任意分区场景也不该自动开始流式加载。启动是 Boot 场景的职责，所以显式成一个组件。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BootFlowStarter : MonoBehaviour
    {
        private void Start()
        {
            if (GameRoot.Instance == null)
            {
                Debug.LogError("[BootFlowStarter] 场景里没有 GameRoot，世界不会加载。", this);
                return;
            }

            GameRoot.Instance.StartStreaming();
        }
    }
}
