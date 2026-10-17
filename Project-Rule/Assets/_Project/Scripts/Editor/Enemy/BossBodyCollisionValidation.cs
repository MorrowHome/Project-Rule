using System;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace RuleGame.Editor.Enemy
{
    /// <summary>副本内用真实输入、FixedUpdate 和 Slide 验证身体阻挡，不手动调用物理回调。</summary>
    public static class BossBodyCollisionValidation
    {
        private const string Pending = "RuleGame.Enemy.BodyCollisionValidation";
        private static PlayerController _player;
        private static EnemyActor _boss;
        private static EntityDamageable _damageable;
        private static Keyboard _keyboard;
        private static int _step, _passed;
        private static float _due, _playerX, _bossX, _health;
        private static double _deadline;
        private static string Output => Environment.GetEnvironmentVariable("RULE_BODY_OUTPUT")
            ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/BossBodyCollisionValidation.txt"));

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            };
        }

        private static void Begin()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>(); _keyboard.MakeCurrent();
            TimeManager.Initialize(); WorldRegistry.Initialize();
            RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
            var combat = new GameObject("Combat").AddComponent<CombatSystem>();
            var ground = new GameObject("Ground"); ground.layer = 8;
            ground.transform.position = new Vector3(0, -0.5f, 0);
            ground.AddComponent<BoxCollider2D>().size = new Vector2(60, 1);

            var inputs = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = inputs.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value).AddCompositeBinding("2DVector")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            map.AddAction("Attack", InputActionType.Button, "<Keyboard>/j");
            map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/c");
            var hero = new GameObject("Player"); hero.SetActive(false);
            hero.transform.position = new Vector3(0, 1.02f, 0);
            hero.AddComponent<BoxCollider2D>().size = new Vector2(1, 2);
            hero.AddComponent<EntityStatus>();
            _player = hero.AddComponent<PlayerController>();
            var serialized = new SerializedObject(_player);
            serialized.FindProperty("_inputActions").objectReferenceValue = inputs;
            serialized.FindProperty("_solidLayer").intValue = 1 << 8;
            serialized.FindProperty("_attackMask").intValue = 1 << 9;
            serialized.FindProperty("_combat").objectReferenceValue = combat;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _damageable = hero.AddComponent<EntityDamageable>();
            serialized = new SerializedObject(_damageable);
            serialized.FindProperty("_faction").intValue = (int)Faction.Player;
            serialized.FindProperty("_combat").objectReferenceValue = combat;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            hero.SetActive(true); _damageable.SetInvulnerable(true);

            var config = ScriptableObject.CreateInstance<EnemyDefinition>();
            config.Kind = EnemyKind.BrickBoss; config.ContactDamage = false;
            config.BodySize = new Vector2(2, 3.2f);
            var enemy = new GameObject("Boss"); enemy.layer = 9; enemy.SetActive(false);
            enemy.transform.position = new Vector3(4, 1.61f, 0);
            _boss = enemy.AddComponent<EnemyActor>(); _boss.Definition = config;
            _boss.Combat = combat;
            _boss.Target = new GameObject("DistantTarget").transform;
            _boss.Target.position = new Vector3(-20, 1, 0);
            enemy.GetComponent<BoxCollider2D>().size = config.BodySize;
            enemy.GetComponent<EntityStatus>().SetPaused(true);
            enemy.SetActive(true);
            Physics2D.SyncTransforms();
            _deadline = EditorApplication.timeSinceStartup + 30;
            _due = Time.fixedTime + 0.25f;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup > _deadline) { Finish("物理验证超时"); return; }
            if (Time.fixedTime < _due || _boss.Brain == null) return;
            try
            {
                switch (_step++)
                {
                    case 0:
                        Check(_player.IsGrounded, "真实物理步正常落地");
                        Keys(Key.D); Wait(0.9f); break;
                    case 1:
                        Check(PlayerX > 1 && PlayerX < 2.6f && !Overlapping, "玩家行走被 Boss 身体阻挡");
                        _playerX = PlayerX; Keys(Key.D, Key.C); Wait(0.3f); break;
                    case 2:
                        Check(PlayerX < 2.6f && !Overlapping, "高速冲刺不能穿过 Boss");
                        Keys(Key.A); Wait(0.45f); break;
                    case 3:
                        Check(PlayerX < _playerX - 1, "贴身后可向反方向脱离");
                        Keys(); _bossX = BossX; _boss.Status.SetPaused(false); Wait(2.5f); break;
                    case 4:
                        Check(BossX < _bossX - 1 && BossX > PlayerX && !Overlapping, "Boss 追击被玩家身体阻挡");
                        Keys(Key.D); Wait(0.5f); break;
                    case 5:
                        Check(PlayerX < BossX && !Overlapping, "双方迎面移动保持分离");
                        _health = _player.GetComponent<EntityStatus>().Health;
                        _damageable.SetInvulnerable(false); _boss.Definition.ContactDamage = true;
                        Wait(0.25f); break;
                    case 6:
                        Check(_player.GetComponent<EntityStatus>().Health < _health, "碰撞边缘接触伤害仍能扣血");
                        _damageable.SetInvulnerable(true); _boss.Definition.ContactDamage = false;
                        _boss.Status.SetPaused(true); _boss.Motor.Stop(); Keys();
                        var body = _player.GetComponent<Rigidbody2D>();
                        body.position = new Vector2(BossX, _boss.GetComponent<BoxCollider2D>().bounds.max.y + 1);
                        _player.AddImpulse(Vector2.zero); Physics2D.SyncTransforms(); Wait(0.3f); break;
                    case 7:
                        Check(!_player.IsGrounded && !_player.WallLeft && !_player.WallRight, "Boss 身体不授予接地或蹬墙能力");
                        _player.GetComponent<Rigidbody2D>().position = new Vector2(8, 1.02f);
                        var trigger = new GameObject("ProjectileTrigger"); trigger.layer = 9;
                        trigger.transform.position = new Vector3(10, 1, 0);
                        var collider = trigger.AddComponent<BoxCollider2D>();
                        collider.size = new Vector2(1, 2); collider.isTrigger = true;
                        Physics2D.SyncTransforms(); Keys(Key.D); Wait(0.85f); break;
                    case 8:
                        Check(PlayerX > 11 && _player.IsGrounded, "投射物触发器不阻挡移动且地面判定正常");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static float PlayerX => _player.GetComponent<Rigidbody2D>().position.x;
        private static float BossX => _boss.GetComponent<Rigidbody2D>().position.x;
        private static bool Overlapping => _player.GetComponent<Collider2D>()
            .Distance(_boss.GetComponent<Collider2D>()).distance < -0.02f;
        private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        private static void Wait(float seconds) => _due = Time.fixedTime + seconds;
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name + $" (player={PlayerX:F3}, boss={BossX:F3})");
            _passed++; Debug.Log("PASS: " + name);
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, $"Passed: {_passed}\nFailed: {(error == null ? 0 : 1)}\n{error}\n");
            if (error != null) Debug.LogError(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
