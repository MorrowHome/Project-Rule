using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    [DefaultExecutionOrder(20)] // 等身体帧与重力方向更新后，再摆三根针的轴心。
    public sealed class ClockHandView : MonoBehaviour
    {
        public EnemyActor Actor;
        public Transform[] Hands;
        private float _clock;
        private EnemySpriteAnimator _visual;
        private void Awake() => _visual = GetComponentInChildren<EnemySpriteAnimator>();
        private void OnEnable() { if (Actor != null) Actor.Restored += ResetClock; }
        private void OnDisable() { if (Actor != null) Actor.Restored -= ResetClock; }
        private void ResetClock() => _clock = Actor.Brain?.SimulationAge ?? 0f;
        private void LateUpdate()
        {
            if (Actor == null || Actor.Brain == null) return;
            if (TimeManager.Instance?.IsPaused != true && Actor.Status?.IsPaused != true) _clock += Time.deltaTime;
            for (int i = 0; i < Hands.Length; i++)
            {
                if (Hands[i] == null) continue;
                Hands[i].gameObject.SetActive(i < Actor.Brain.Phase && !Actor.Defeated);
                if (_visual != null) Hands[i].position = _visual.ClockFaceWorld;
                // 美术的分针原图朝下，时针及其细长秒针朝上，统一到十二点起始方向。
                float orientation = i == 1 ? 180f : 0f;
                Hands[i].localRotation = Quaternion.Euler(0f, 0f, orientation - _clock / (Actor.Definition.ClockHourInterval * (i + 1)) * 360f);
            }
        }
    }
}
