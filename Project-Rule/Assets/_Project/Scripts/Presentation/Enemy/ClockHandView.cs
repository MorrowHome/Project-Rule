using UnityEngine;
using RuleGame.Gameplay.Enemy;

namespace RuleGame.Presentation.Enemy
{
    [DefaultExecutionOrder(20)] // 等身体帧与重力方向更新后，再摆三根针的轴心。
    public sealed class ClockHandView : MonoBehaviour
    {
        public EnemyActor Actor;
        [Tooltip("保留现有资源槽位：0 为秒针占位，1 为分针，2 为时针。阶段按时针、分针、秒针解锁。")]
        public Transform[] Hands;
        private EnemySpriteAnimator _visual;
        private void Awake() => _visual = GetComponentInChildren<EnemySpriteAnimator>();
        private void LateUpdate()
        {
            if (Actor == null || !(Actor.Brain is ClockBossBrain brain)) return;
            for (int i = 0; i < Hands.Length; i++)
            {
                if (Hands[i] == null) continue;
                int hand = 2 - i;
                Hands[i].gameObject.SetActive(hand >= 0 && hand < brain.Phase && !Actor.Defeated);
                if (_visual != null) Hands[i].position = _visual.ClockFaceWorld;
                // 美术的分针原图朝下，时针及其细长秒针朝上，统一到十二点起始方向。
                float orientation = i == 1 ? 180f : 0f;
                Hands[i].localRotation = Quaternion.Euler(0f, 0f, orientation + (Actor.ClockBeams?.HandRotation(hand) ?? 0f));
            }
        }
    }
}
