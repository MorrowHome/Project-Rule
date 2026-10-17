using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Enemy
{
    /// <summary>只验证小怪破韧、击退保护和弹簧空中丢失目标，不运行 Boss 套件。</summary>
    public static class MinionResilienceValidation
    {
        private const string Pending = "RuleGame.MinionResilienceValidation";
        private static int _passed, _failed;
        private static GameObject _root;
        private static EnemyActor _actor;
        private static Transform _target;

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅用于隔离工程。");
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
                if (state == PlayModeStateChange.EnteredPlayMode) Run();
            };
        }

        private static void Run()
        {
            TimeManager.Initialize();
            WorldRegistry.Initialize();
            var catalog = ScriptableObject.CreateInstance<RuleCatalog>();
            RuleSystem.Initialize(catalog);
            Physics2D.simulationMode = SimulationMode2D.Script;
            foreach (EnemyKind kind in new[] { EnemyKind.BrickMinion, EnemyKind.BatMinion, EnemyKind.PlushSeal })
            {
                Case(kind, () =>
                {
                    float hp = _actor.Status.Health;
                    int flashes = 0;
                    _actor.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.Hit) flashes++; };
                    Hit(1f);
                    Check(_actor.Status.Health == hp - 1 && flashes == 1, "轻击正常扣血与命中反馈");
                    Check(State != "Hurt", "轻击未破韧时保持行动");
                    Check(_actor.Motor.CaptureImpulse() == Vector2.zero, "轻击不能绕过抗硬直叠加击退");
                    Step(.14f);
                    Hit(_actor.Definition.MinionStaggerThreshold);
                    Check(State == "Hurt", "达到阈值进入硬直");
                    Check(_actor.Motor.CaptureImpulse().sqrMagnitude > 0f, "真正破韧保留击退");
                    Check(_actor.AnimationDuration <= _actor.Definition.MinionHurtDuration, "长受击动画受小怪硬直上限约束");
                    Step(.14f);
                    Vector2 impulse = _actor.Motor.CaptureImpulse();
                    Hit(1f);
                    Check(_actor.Motor.CaptureImpulse() == impulse, "受击中追加命中不反复叠加击退");
                    Step(.14f);
                    Check(State != "Hurt", "短硬直结束后恢复行动");
                    object saved = _actor.CaptureSnapshotState();
                    hp = _actor.Status.Health;
                    Hit(_actor.Definition.MinionStaggerThreshold);
                    Check(State != "Hurt" && _actor.Status.Health < hp, "恢复后的完整保护期仍承受伤害");
                    Check(_actor.Motor.CaptureImpulse() == Vector2.zero, "保护期不接受普通击退");
                    _actor.Status.SetPaused(true);
                    Step(3f);
                    _actor.Status.SetPaused(false);
                    Check(State != "Hurt", "暂停不推进保护计时");
                    Step(_actor.Definition.HurtCooldown + .1f);
                    Hit(_actor.Definition.MinionStaggerThreshold);
                    Check(State == "Hurt", "保护结束后仍能再次破韧");
                    _actor.RestoreSnapshotState(saved);
                    Hit(_actor.Definition.MinionStaggerThreshold);
                    Check(State != "Hurt", "回溯恢复剩余保护期");
                });
            }
            Case(EnemyKind.BatMinion, () =>
            {
                Hit(1f, 0f);
                Step(_actor.Definition.StaggerResetDelay + .1f);
                Hit(_actor.Definition.MinionStaggerThreshold - 1f, 0f);
                Check(State != "Hurt", "断续轻击不会无限保留累计破韧");
                object saved = _actor.CaptureSnapshotState();
                Step(.14f); Hit(1f, 0f);
                Check(State == "Hurt", "窗口内追加伤害达到阈值");
                _actor.RestoreSnapshotState(saved);
                Step(.14f); Hit(1f, 0f);
                Check(State == "Hurt", "召唤物快照恢复累计破韧");
            });
            Case(EnemyKind.StoneGuard, () =>
            {
                _target.position = _actor.transform.position + Vector3.right;
                Step(.02f); Step(.24f);
                Check(State == "Attack", "近身守卫开始有前摇的攻击");
                Hit(_actor.Definition.MinionStaggerThreshold);
                Check(State == "Attack" && _actor.Motor.CaptureImpulse() == Vector2.zero,
                    "已锁定的攻击同时抵抗硬直与普通击退");
            });
            Case(EnemyKind.PlushSeal, () =>
            {
                Hit(0f);
                Check(State != "Hurt" && _actor.Motor.CaptureImpulse().sqrMagnitude > 0f,
                    "挂账零伤害仍保留规则约定的纯击退");
            });
            Case(EnemyKind.PlushSeal, () =>
            {
                _actor.GetComponent<Rigidbody2D>().mass = .2f;
                Check(_actor.Motor.MassReduced, "质量降低由真实规则身体判定");
                Hit(1f);
                Check(State != "Hurt" && _actor.Motor.CaptureImpulse().sqrMagnitude > 0f,
                    "质量降低时轻击仍可推动小怪");
            });
            foreach (bool removeTarget in new[] { false, true })
                Case(EnemyKind.SpringToy, () =>
                {
                    _target.position = _actor.transform.position + Vector3.right * 4f;
                    for (int i = 0; i < 40 && _actor.Motor.Velocity.y <= .1f; i++) PhysicalStep();
                    Check(_actor.Motor.Velocity.y > .1f && _actor.Motor.Intent.x > 0f, "实际起跳后朝目标前进");
                    _actor.Definition.MinionTargetMemory = .01f;
                    if (removeTarget) _target.gameObject.SetActive(false);
                    else _target.position += Vector3.right * 20f;
                    Step(.04f);
                    Check(_actor.Motor.Intent.x > 0f, "目标消失或记忆到期不取消本跳方向");
                    for (int i = 0; i < 180 && (!_actor.Motor.Grounded || _actor.Motor.Velocity.y > .1f); i++) PhysicalStep();
                    PhysicalStep();
                    Check(_actor.Motor.Grounded && _actor.Motor.Intent == Vector2.zero, "落地后停止追击且不盲目续跳");
                });
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/MinionResilienceValidation.txt", $"Passed: {_passed}\nFailed: {_failed}\n");
            Debug.Log($"MinionResilienceValidation: {_passed} passed, {_failed} failed");
            EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }

        private static string State => _actor.Brain.StateName;
        private static void Hit(float damage, float knockback = 6f) =>
            _actor.ApplyDamage(new DamageInfo { Amount = damage, Knockback = knockback, Attacker = _target });
        private static void Step(float dt)
        {
            for (int i = 0; i < Mathf.CeilToInt(dt / .02f); i++) PhysicalStep();
        }
        private static void PhysicalStep() { _actor.SendMessage("FixedUpdate"); Physics2D.Simulate(.02f); }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            _passed++;
            Debug.Log("PASS: " + message);
        }

        private static void Case(EnemyKind kind, Action test)
        {
            _root = new GameObject(kind + "ResilienceValidation");
            try
            {
                var floor = new GameObject("Floor"); floor.transform.SetParent(_root.transform); floor.layer = 8;
                floor.transform.position = Vector2.down * .5f;
                floor.AddComponent<BoxCollider2D>().size = new Vector2(60f, 1f);
                var target = new GameObject("Target"); target.transform.SetParent(_root.transform);
                _target = target.transform; _target.position = new Vector2(20f, 1f);
                var go = new GameObject("Actor"); go.SetActive(false); go.transform.SetParent(_root.transform); go.layer = 9;
                _actor = go.AddComponent<EnemyActor>();
                go.AddComponent<RuleAffectedBody>();
                string folder = kind == EnemyKind.BrickMinion || kind == EnemyKind.BatMinion ? "" : "/FieldMinions";
                var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/_Project/Data/Enemies{folder}/{kind}_Definition.asset");
                _actor.Definition = UnityEngine.Object.Instantiate(definition);
                _actor.Definition.Health = 500f;
                _actor.GetComponent<BoxCollider2D>().size = _actor.Definition.BodySize;
                go.transform.position = new Vector2(0f, _actor.Definition.Flying ? 3.5f : _actor.Definition.BodySize.y * .5f + .02f);
                _actor.Target = _target; _actor.enabled = false; go.SetActive(true);
                _actor.SendMessage("Start"); Physics2D.SyncTransforms();
                test();
            }
            catch (Exception error) { _failed++; Debug.LogError(kind + ": " + error); }
            finally
            {
                if (_actor != null) UnityEngine.Object.DestroyImmediate(_actor.Definition);
                UnityEngine.Object.DestroyImmediate(_root); Physics2D.SyncTransforms();
            }
        }
    }
}
