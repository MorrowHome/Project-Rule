using UnityEngine;
using UnityEngine.InputSystem;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;
using RuleGame.Infrastructure;

namespace RuleGame.Development.Enemy
{
    /// <summary>只用于独立白盒：阶段快捷键和重开入口，不进入正式敌人 Prefab。</summary>
    public sealed class EnemyLabControls : MonoBehaviour
    {
        public EnemyActor Enemy;
        public EntityStatus Player;
        public Font Font;
        private GUIStyle _style;
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) AdvancePhase();
            if (keyboard.f2Key.wasPressedThisFrame && Enemy.WeakPoint != null && Enemy.WeakPoint.Opened)
                Enemy.WeakPoint.ApplyDamage(new DamageInfo { Amount = Enemy.Definition.ClockCoreHealth });
            if (keyboard.f3Key.wasPressedThisFrame) Restart();
            if (keyboard.f4Key.wasPressedThisFrame)
            {
                bool pause = TimeManager.Instance?.IsPaused != true;
                TimeManager.Instance?.SetPaused(pause);
                Player.SetPaused(pause);
            }
        }
        private void AdvancePhase()
        {
            if (Enemy == null || Enemy.Brain == null || Enemy.Defeated) return;
            float health;
            switch (Enemy.Definition.Kind)
            {
                case EnemyKind.ClockBoss:
                case EnemyKind.BatBoss: health = Enemy.Definition.Health * (Enemy.Brain.Phase == 1 ? 2f / 3f : Enemy.Brain.Phase == 2 ? 1f / 3f : 0f); break;
                case EnemyKind.RabbitBoss: health = Enemy.Status.Health > 50f ? 50f : 19f; break;
                default: health = Mathf.Max(0f, Enemy.Status.Health - Enemy.Definition.Health * 0.26f); break;
            }
            Enemy.Status.ApplyHealthDelta(health - Enemy.Status.Health);
        }
        private void OnGUI()
        {
            if (Enemy == null || Enemy.Brain == null) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { font = Font, fontSize = 16, normal = { textColor = new Color(0.12f, 0.12f, 0.1f) } };
            GUILayout.BeginArea(new Rect(14f, 14f, 540f, 180f));
            GUILayout.Label($"{Enemy.Definition.DisplayName}  |  {Enemy.Brain.StateName}  |  阶段 {Enemy.Brain.Phase}", _style);
            GUILayout.Label($"生命 {Enemy.Status.Health:0}/{Enemy.Definition.Health:0}    能量 {Enemy.Status.Energy:0}    主角无敌", _style);
            GUILayout.Label("F1 进入下一阶段   F2 击破露出的发条", _style);
            GUILayout.Label("F3 重开白盒   F4 暂停   Shift 规则轮盘", _style);
            if (Enemy.Definition.Kind == EnemyKind.RabbitBoss) GUILayout.Label("兔子美术文件夹为空；当前展示白盒动作。", _style);
            GUILayout.EndArea();
        }
        private void Restart()
        {
#if UNITY_EDITOR
            string path = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
        }
    }
}
