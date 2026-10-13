using UnityEngine;
using RuleGame.Gameplay.Combat;

namespace RuleGame.Development.Testing
{
    /// <summary>
    /// 验证用的击退接收器：只把冲量记下来，不真的推谁。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么放在 <c>Development</c> 而不是 <c>Editor</c></b>：
    /// Unity 的 <c>AddComponent</c> <b>加不上 Editor 程序集里的 MonoBehaviour</b>
    /// —— 实测返回 null。验证器是在 Play 模式下跑的，所以这个桩必须在运行时程序集里。
    /// <c>Development</c> 本来就是「demo / 白盒代码，与生产代码隔离」的位置。</para>
    /// <para>挂账验证靠它区分「击退立即施加」和「击退被一起推迟」。</para>
    /// </remarks>
    public class TestKnockbackReceiver : MonoBehaviour, IKnockbackReceiver
    {
        /// <summary>收到的冲量次数。</summary>
        public int Count { get; private set; }

        /// <summary>最后一次收到的冲量。</summary>
        public Vector2 Last { get; private set; }

        public void AddImpulse(Vector2 impulse)
        {
            Count++;
            Last = impulse;
        }

        public void Clear()
        {
            Count = 0;
            Last = Vector2.zero;
        }
    }
}
