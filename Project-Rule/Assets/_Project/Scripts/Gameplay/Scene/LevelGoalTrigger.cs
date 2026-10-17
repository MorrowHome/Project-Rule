using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.LevelFlow;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 关卡终点：玩家进入触发区即通知关卡流程通关。
    /// </summary>
    /// <remarks>
    /// <para><b>只做一件事</b>：调 <see cref="ILevelFlow.NotifyGoalReached"/>。
    /// 结算 UI 订阅 <c>OnLevelComplete</c>，场景切换由结算界面调 <c>RequestNextLevel</c> 触发 ——
    /// 终点自己不认识 UI，也不切场景。</para>
    /// <para><b>触发回调可用</b>：玩家是 Kinematic 刚体，<c>MotionMover</c> 已开
    /// <c>useFullKinematicContacts</c>，所以 <c>OnTriggerEnter2D</c> 会正常触发。</para>
    /// <para><b>防重入</b>：通关事件本身不去重（结算 UI 每次都要收到），
    /// 但一个终点反复触发没有意义，所以这里自己记 <c>_fired</c>。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class LevelGoalTrigger : MonoBehaviour
    {
        [Tooltip("是否要求进入者属于玩家阵营（读 EntityDamageable 的 Faction）")]
        [SerializeField] private bool _playerFactionOnly = true;

        private bool _fired;

        private void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_fired) return;

            if (_playerFactionOnly)
            {
                IFactionMember member = other.GetComponentInParent<IFactionMember>();
                if (member == null || member.Faction != Faction.Player) return;
            }

            if (GameRoot.Instance == null)
            {
                Debug.LogError("[LevelGoalTrigger] 找不到 GameRoot（不是从 Boot 场景启动的？），终点无效。", this);
                return;
            }

            _fired = true;
            GameRoot.Instance.LevelFlow.NotifyGoalReached();
        }
    }
}
