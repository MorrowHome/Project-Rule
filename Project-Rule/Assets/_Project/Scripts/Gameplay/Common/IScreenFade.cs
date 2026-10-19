using System.Collections;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 全屏黑场的唯一契约：谁能把画面压黑、谁能放回来。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是接口</b>：实现住在 <c>Presentation</c>（黑场是表现，不是玩法），
    /// 而调用方住在 <c>Gameplay</c>（传送是玩法）。依赖方向只允许 Presentation → Gameplay，
    /// 所以玩法层只能认这个接口，不能认具体组件。</para>
    /// <para><b>为什么返回 IEnumerator</b>：工程里现有的黑场（<c>SwingCheckpointFade</c>）是
    /// 一个 <c>Alpha</c> 字段 + <c>OnGUI</c>，没有异步语义。让调用方自己 <c>yield return</c>
    /// 这段协程，比强行包一层 await / 回调都干净，也不会额外引入一个 Update 驱动。</para>
    /// <para><b>没接实现时必须能跑</b>：调用方拿到 null 就跳过黑场直接干活，
    /// 这样玩法可以先落地，表现后接。</para>
    /// </remarks>
    public interface IScreenFade
    {
        /// <summary>画面此刻是否已经完全被遮住（<c>Alpha ≈ 1</c>）。</summary>
        bool IsCovered { get; }

        /// <summary>把遮罩渐变到 <paramref name="alpha"/>（0 = 透明，1 = 全黑）。</summary>
        IEnumerator FadeTo(float alpha);

        /// <summary>立刻放回透明。<b>只能用于收尾</b>（组件被禁用 / 传送被打断），
        /// 否则会把黑屏留在画面上。</summary>
        void Clear();
    }
}
