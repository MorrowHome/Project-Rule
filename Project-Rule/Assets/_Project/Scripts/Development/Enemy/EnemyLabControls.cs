using UnityEngine;
using UnityEngine.InputSystem;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;
using RuleGame.Infrastructure;
using RuleGame.Gameplay.Combat;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Development.Enemy
{
    /// <summary>只用于独立白盒：阶段快捷键和重开入口，不进入正式敌人 Prefab。</summary>
    public sealed class EnemyLabControls : MonoBehaviour
    {
        public EnemyActor Enemy;
        public EntityStatus Player;
        public Font Font;
        public bool Invincible = false;
        private GUIStyle _style;
        private GUIStyle _buttonStyle;
        private void Start() => SetInvincible(Invincible);
        public void SetInvincible(bool invincible)
        {
            Invincible = invincible;
            Player?.GetComponent<EntityDamageable>()?.SetInvulnerable(invincible);
        }
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) AdvancePhase();
            if (keyboard.f2Key.wasPressedThisFrame && Enemy.WeakPoint != null && Enemy.WeakPoint.Opened)
                Enemy.WeakPoint.ApplyDamage(new DamageInfo { Amount = Enemy.Definition.ClockCoreHealth });
            if (keyboard.f3Key.wasPressedThisFrame) Restart();
            if (keyboard.f5Key.wasPressedThisFrame) SetInvincible(!Invincible);
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
                case EnemyKind.RabbitBoss:
                    health = Enemy.Definition.Health * (Enemy.Brain.Phase == 1 ? .5f : .19f); break;
                default: health = Mathf.Max(0f, Enemy.Status.Health - Enemy.Definition.Health * 0.26f); break;
            }
            Enemy.Status.ApplyHealthDelta(health - Enemy.Status.Health);
        }
        private void OnGUI()
        {
            if (Enemy == null || Enemy.Brain == null) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { font = Font, fontSize = 16, normal = { textColor = new Color(0.12f, 0.12f, 0.1f) } };
                _buttonStyle = new GUIStyle(GUI.skin.button) { font = Font, fontSize = 16 };
            }
            GUILayout.BeginArea(new Rect(14f, 14f, 540f, 220f));
            GUILayout.Label($"{Enemy.Definition.DisplayName}  |  {Enemy.Brain.StateName}  |  阶段 {Enemy.Brain.Phase}", _style);
            GUILayout.Label($"生命 {Enemy.Status.Health:0}/{Enemy.Definition.Health:0}    能量 {Enemy.Status.Energy:0}", _style);
            GUILayout.Label($"场上弹体 {EnemyProjectile.ActiveCount(Enemy)}/{Enemy.Definition.ProjectileBudget}", _style);
            if (Enemy.Brain is ClockBossBrain clock)
            {
                var slot = RuleSystem.Instance?.Table.GetSlot(AttributeId.Time);
                string word = slot?.Relation == RelationId.Enhance ? "加快" : slot?.Relation == RelationId.Weaken ? "减慢" : "其它/常速";
                GUILayout.Label($"世界时间：{word}    Boss 已施放 {clock.TimeRuleCastCount} 次变速", _style);
            }
            GUILayout.Label($"主角生命 {Player.Health:0}/{Player.HealthMax:0}    无敌{(Invincible ? "开启" : "关闭")}", _style);
            GUILayout.Label("F1 调阶段   F2 强制击破发条（调试）", _style);
            GUILayout.Label("F3 重开   F4 暂停   F5 无敌开关   Shift 规则轮盘", _style);
            if (Enemy.Brain is ClockBossBrain clockControls && GUILayout.Button(
                $"额外环形弹幕：{(clockControls.ExtraBarrageEnabled ? "开启" : "关闭")}",
                _buttonStyle, GUILayout.Width(240f), GUILayout.Height(28f)))
                clockControls.SetExtraBarrageEnabled(!clockControls.ExtraBarrageEnabled);
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
