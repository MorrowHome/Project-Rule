using RuleGame.Infrastructure.Rules;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 因果属性规则。
    /// </summary>
    /// <remarks>
    /// <para><b>五个关系全是参数型</b> —— 只往 <c>IAttributeManager</c> 写标志位与倍率，
    /// 不需要快照、计时器或输入。真正的行为（掷骰子、互换对象、查扩散候选集）
    /// 全部在伤害处理模块里。</para>
    /// <para>⚠️ <b>规则矩阵里「失效」这个名字和它的内容对不上</b>：
    /// 它现在的效果是「倍率随机 + 作用对象随机 + 恢复共享」，而不是「什么都不生效」。
    /// 文案是策划定的，这里只在注释里点明，不改名。</para>
    /// </remarks>
    public class CausalityRule : AttributeRuleBase
    {
        /// <summary>削弱：因果效果减弱的倍率。⚠️ 待策划确认。</summary>
        public const float WeakenedScale = 0.5f;

        public CausalityRule(IAttributeManager attributes) : base(attributes) { }

        /// <summary>反转：伤害和治疗的作用对象转换（攻击敌人自己扣血）。</summary>
        /// <remarks>一次替换，不是反弹 —— 不会递归。</remarks>
        protected override bool OnRevert() => Apply(inverted: true);

        /// <summary>失效：倍率随机 + 作用对象随机 + 恢复不分敌我。</summary>
        protected override bool OnNullify() => Apply(random: true, recoveryShared: true);

        /// <summary>恒定：因果恢复正常。</summary>
        protected override bool OnConstant() => Apply();

        /// <summary>增强：攻击扩散到周围、治疗扩散到全队。</summary>
        protected override bool OnEnhance() => Apply(spread: true);

        /// <summary>削弱：效果减弱。</summary>
        protected override bool OnWeaken() => Apply(scale: WeakenedScale);

        /// <summary>
        /// 到期 / 被覆盖 / 重开关卡：因果恢复默认。
        /// </summary>
        /// <remarks>覆盖前 <c>RuleSystem</c> 会调本方法，那时表里还是旧关系，
        /// 所以这里直接写死默认值，不能去对表。</remarks>
        public override void Clear() => Apply();

        /// <summary>
        /// 一次性写全五个字段。
        /// </summary>
        /// <remarks><b>必须写全</b>：只写自己那几个的话，上一次规则的残留会留下来
        /// （例如「失效」的 <c>CausalityRandom</c> 会污染后面「增强」的效果）。</remarks>
        private bool Apply(float scale = 1f, bool inverted = false, bool random = false,
                           bool recoveryShared = false, bool spread = false)
        {
            IAttributeManager a = Attributes;
            a.SetCausalityScale(scale);
            a.SetCausalityInverted(inverted);
            a.SetCausalityRandom(random);
            a.SetCausalityRecoveryShared(recoveryShared);
            a.SetCausalitySpread(spread);
            return true;
        }
    }
}
