using System;

namespace RuleGame.Infrastructure.Interaction
{
    /// <summary>P1 的接管边界。倍率作为临时来源合成，释放令牌后恢复其它来源的结果。</summary>
    public interface IRuleSelectionTimeSource
    {
        IDisposable AcquireSelectionScale(float scale);
    }
}
