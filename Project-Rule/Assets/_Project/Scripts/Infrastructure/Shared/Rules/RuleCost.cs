namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 一次规则释放的费用。
    /// </summary>
    /// <remarks>
    /// **只有生效费用，没有「每次使用费」**——反转的「激活」就是「回退」本身。
    /// </remarks>
    public readonly struct RuleCost
    {
        /// <summary>能量消耗。</summary>
        public readonly float Energy;

        /// <summary>冷却秒数。0 = 无冷却。</summary>
        public readonly float Cooldown;

        public RuleCost(float energy, float cooldown)
        {
            Energy   = energy;
            Cooldown = cooldown;
        }
    }
}
