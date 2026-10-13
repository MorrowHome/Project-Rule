namespace RuleGame.Infrastructure
{
    /// <summary>
    /// 阵营。
    /// </summary>
    /// <remarks>
    /// <para><b>刻意只有两个值。</b>玩家是<b>单独一个阵营、没有友方</b>；
    /// 玩家之外的一切可攻击实体同属另一个阵营（对玩家而言都是敌人）。</para>
    /// <para>「因果 × 增强」的攻击扩散靠它过滤 —— 不打自己人
    /// （否则对玩家的强化过头了）；治疗扩散则相反，只打自己人。</para>
    /// </remarks>
    public enum Faction
    {
        /// <summary>
        /// 玩家之外的一切可攻击实体。
        /// </summary>
        /// <remarks>枚举值为 <b>0</b>：让新增组件的默认值就是「敌人」，
        /// 现有场景不用逐个体检。</remarks>
        Enemy = 0,

        /// <summary>玩家。单独一个阵营。</summary>
        Player = 1,
    }
}
