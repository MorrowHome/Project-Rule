namespace RuleGame.Infrastructure.Rules
{
    /// <summary>规则请求的结果状态。</summary>
    public enum RuleRequestStatus
    {
        /// <summary>成功生效。</summary>
        Success,

        /// <summary>该 属性×关系 未定义（规则矩阵里的空格）。</summary>
        Undefined,

        /// <summary>行为型规则缺资源，例如「没有可用快照」。</summary>
        NoResource,
    }

    /// <summary>
    /// <see cref="IRuleSystem.Request"/> 的返回值。
    /// </summary>
    /// <remarks>
    /// 释放方（<c>RuleReleaseService</c>）**只有在 <see cref="Succeeded"/> 时才结算费用**。
    /// </remarks>
    public readonly struct RuleRequestResult
    {
        public readonly RuleRequestStatus Status;

        public bool Succeeded => Status == RuleRequestStatus.Success;

        public RuleRequestResult(RuleRequestStatus status) => Status = status;

        public static RuleRequestResult Ok() => new RuleRequestResult(RuleRequestStatus.Success);

        public static RuleRequestResult Fail(RuleRequestStatus status) => new RuleRequestResult(status);
    }
}
