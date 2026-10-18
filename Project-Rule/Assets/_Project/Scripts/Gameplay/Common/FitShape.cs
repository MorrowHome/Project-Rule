namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 拼装件的**形状标签**：凹位只认与自己相同的形状。
    /// </summary>
    /// <remarks>
    /// <para>它住在 <c>Gameplay/Common</c>，与 <c>IPushable</c> / <c>IMovingSurface</c> 同一层 ——
    /// 块与凹位互相认识的**唯一**依据就是它，两边都不需要知道对方是哪个具体组件。</para>
    /// <para>新增形状（方形 / 六边形）时只在这里加成员，并在预制体上换碰撞体。</para>
    /// </remarks>
    public enum FitShape
    {
        /// <summary>三角块 / 三角凹位（平底朝下，尖朝上）。</summary>
        Triangle = 0,

        /// <summary>圆球 / 圆窝。</summary>
        Round = 1,
    }
}
