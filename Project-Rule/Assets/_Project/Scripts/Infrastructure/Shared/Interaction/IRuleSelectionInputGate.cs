using System;

namespace RuleGame.Infrastructure.Interaction
{
    /// <summary>只接管玩家输入，不暂停物理。令牌释放当帧仍需吞掉确认键，防止同时挥刀。</summary>
    public interface IRuleSelectionInputGate
    {
        IDisposable BlockGameplayInput();
    }
}
