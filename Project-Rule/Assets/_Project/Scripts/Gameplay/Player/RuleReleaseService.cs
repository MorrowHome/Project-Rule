using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Player
{
    /// <summary><see cref="RuleReleaseService.TryRelease"/> 的失败原因。</summary>
    public enum RuleReleaseStatus
    {
        Success,
        Locked,           // 词条未解锁
        OnCooldown,       // 冷却中
        NotEnoughEnergy,  // 能量不足
        Undefined,        // 该 属性×关系 未定义
        NoResource,       // 行为型规则缺资源（如没有可用快照）
    }

    /// <summary><see cref="RuleReleaseService.TryRelease"/> 的返回值。</summary>
    public readonly struct RuleReleaseResult
    {
        public readonly RuleReleaseStatus Status;

        public bool Succeeded => Status == RuleReleaseStatus.Success;

        public RuleReleaseResult(RuleReleaseStatus status) => Status = status;
    }

    /// <summary>
    /// 规则判定中枢。<b>预检 → 请求 → 成功后才结算。</b>
    /// </summary>
    /// <remarks>
    /// <para>纯 C# 类，不继承 MonoBehaviour —— 玩家与敌人共用同一份逻辑：
    /// 玩家走「拼装界面 → 本服务」，敌人走「AI → 本服务」（传自己的
    /// <see cref="IEntityStatus"/> 与 <see cref="RuleSource.Enemy"/>）。</para>
    /// <para><b>它不认识具体属性</b>，也不认识 UI。</para>
    /// <para>只依赖 Infrastructure 层（<see cref="IRuleSystem"/> 与 <see cref="IEntityStatus"/>
    /// 都定义在那里），<b>不依赖 Core</b>。</para>
    /// </remarks>
    public class RuleReleaseService
    {
        private readonly IRuleSystem   _rules;
        private readonly IEntityStatus _status;
        private readonly IRuleUnlocks  _unlocks;

        public RuleReleaseService(IRuleSystem rules, IEntityStatus status, IRuleUnlocks unlocks)
        {
            _rules   = rules;
            _status  = status;
            _unlocks = unlocks;
        }

        /// <summary>UI 用：决定按钮灰不灰。</summary>
        /// <remarks>只做展示判断，<b>不改变任何状态</b>。</remarks>
        public bool CanRelease(AttributeId attr, RelationId rel)
        {
            if (!_unlocks.IsUnlocked(attr, rel)) return false;
            if (_status.IsOnCooldown(attr, rel)) return false;
            return _status.Energy >= _rules.GetCost(attr, rel).Energy;
        }

        /// <summary>
        /// 释放一条规则。<b>只有规则真正生效后，才扣除能量与开始冷却。</b>
        /// </summary>
        public RuleReleaseResult TryRelease(AttributeId attr, RelationId rel, RuleSource source)
        {
            // ① 预检（再查一次，防止 UI 显示后状态变了）
            if (!_unlocks.IsUnlocked(attr, rel))
                return Fail(RuleReleaseStatus.Locked);
            if (_status.IsOnCooldown(attr, rel))
                return Fail(RuleReleaseStatus.OnCooldown);

            var cost = _rules.GetCost(attr, rel);
            if (_status.Energy < cost.Energy)
                return Fail(RuleReleaseStatus.NotEnoughEnergy);

            // ② 尝试生效（此时【不扣任何东西】）
            var req = _rules.Request(attr, rel, source);
            if (!req.Succeeded)
            {
                return Fail(req.Status == RuleRequestStatus.NoResource
                    ? RuleReleaseStatus.NoResource
                    : RuleReleaseStatus.Undefined);
            }

            // ③ 只有成功之后才结算
            _status.TryConsumeEnergy(cost.Energy);
            if (cost.Cooldown > 0f)
                _status.BeginCooldown(attr, rel, cost.Cooldown);

            return new RuleReleaseResult(RuleReleaseStatus.Success);
        }

        /// <summary>
        /// 主动结束某个属性当前生效的规则。
        /// </summary>
        /// <remarks>
        /// <para><b>不消耗能量、不影响冷却、也不做任何预检</b> —— 取消是免费的。</para>
        /// <para>放在本服务里而不是让调用方直接拿 <c>IRuleSystem</c>，
        /// 是为了守住「写入一律经由判定中枢」这条规矩。</para>
        /// </remarks>
        /// <returns>确实结束了一条规则时返回 true。</returns>
        public bool TryCancel(AttributeId attr) => _rules.Cancel(attr);

        private static RuleReleaseResult Fail(RuleReleaseStatus s) => new RuleReleaseResult(s);
    }
}
