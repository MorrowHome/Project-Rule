using System;

namespace RuleGame.Infrastructure.Interaction
{
    /// <summary>P4 镜头接管边界。目标由镜头组件绑定，UI 不认识具体跟随实现。</summary>
    public interface IRuleSelectionFocus
    {
        IDisposable AcquireSelectionFocus();
    }
}
