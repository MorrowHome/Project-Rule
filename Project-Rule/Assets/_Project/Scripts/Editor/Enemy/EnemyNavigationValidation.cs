using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Enemy
{
    /// <summary>平台导航定向物理验证；只在隔离工程中执行。</summary>
    public static class EnemyNavigationValidation
    {
        private const string Pending = "RuleGame.EnemyNavigationValidation";
        private const string CombatOnly = "RuleGame.EnemyNavigationCombatOnly";
        private const string RangeOnly = "RuleGame.EnemyNavigationRangeOnly";
        private static GameObject _root;
        private static EnemyActor _actor;
        private static float _age, _minX, _maxY;
        private static int _passed, _failed;
        private static bool _integrated;
        private static Vector2 _home;

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅用于隔离工程。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        public static void RunCombatBatch()
        {
            SessionState.SetBool(CombatOnly, true);
            RunBatch();
        }
        public static void RunRangeBatch()
        {
            SessionState.SetBool(RangeOnly, true);
            RunBatch();
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) Run(); };
        }
        private static void Run()
        {
            TimeManager.Initialize(); WorldRegistry.Initialize();
            RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
            Physics2D.simulationMode = SimulationMode2D.Script;
            if (SessionState.GetBool(CombatOnly, false)) { CombatCase(); Finish(); return; }
            if (SessionState.GetBool(RangeOnly, false)) { RangeCases(); Finish(); return; }
            Case("flat", () =>
            {
                Floor(); Spawn(new Vector2(-3f, .82f));
                Pump(new Vector2(3f, .82f), 5f);
                Check(Mathf.Abs(Position.x - 3f) < .45f && _maxY < 1f, "平地走近目标，不无故跳跃");
            });
            Case("step", () =>
            {
                Floor(); Box("Step", new Vector2(0f, .5f), new Vector2(1.2f, 1f));
                Spawn(new Vector2(-3f, .82f)); Pump(new Vector2(3f, .82f), 8f);
                Check(Position.x > 2.5f && _maxY > 1.7f, "实际跳上障碍并从另一侧继续追击");
            });
            Case("gap", () =>
            {
                Gap(); Spawn(new Vector2(-2f, .82f)); Pump(new Vector2(2f, .82f), 7f);
                Check(Position.x > 1.5f && Position.y > .7f && _maxY > 1.2f, "有可达落点时跳过沟壑");
            });
            Case("stairs-backtrack", () =>
            {
                Floor(); Box("First", new Vector2(-3f, .975f), new Vector2(1.4f, .25f));
                Box("Second", new Vector2(0f, 2.075f), new Vector2(2f, .25f));
                Box("Third", new Vector2(3f, 3.175f), new Vector2(1.4f, .25f));
                Spawn(new Vector2(1f, .82f)); Pump(new Vector2(3.2f, 4.12f), 14f);
                Check(_minX < -.8f && Position.y > 3.9f, "目标在右上方时先向左寻找逐级跳跃路线");
            });
            Case("safe-drop", () =>
            {
                Box("Upper", new Vector2(-3f, 1.75f), new Vector2(6f, .5f));
                Box("Lower", new Vector2(3.2f, -.25f), new Vector2(5.6f, .5f));
                Spawn(new Vector2(-2f, 2.82f)); Pump(new Vector2(3f, .82f), 8f);
                Check(Position.x > 2.5f && Position.y > .7f && Position.y < 1f, "验证落脚点后才允许越过平台边缘下落");
            });
            Case("unsafe-drop", () =>
            {
                Box("Upper", new Vector2(-3f, 4.75f), new Vector2(6f, .5f));
                Box("Lower", new Vector2(3f, -.25f), new Vector2(6f, .5f));
                Spawn(new Vector2(-2f, 5.82f)); Pump(new Vector2(3f, .82f), 6f);
                Check(Position.x < 0f && Position.y > 5.7f, "超过安全落差时留在平台，不盲目追落");
            });
            Case("low-roof", () =>
            {
                Floor(); Box("Obstacle", new Vector2(0f, .6f), new Vector2(1f, 1.2f));
                Box("Roof", new Vector2(0f, 2.05f), new Vector2(5f, .3f));
                Spawn(new Vector2(-3f, .82f)); Pump(new Vector2(3f, .82f), 6f);
                Check(Position.x < -.8f && Position.y > .7f, "身体净空不足时拒绝穿顶路线");
            });
            Case("dynamic-wall", () =>
            {
                Floor(); Spawn(new Vector2(-3f, .82f)); Pump(new Vector2(3f, .82f), .4f);
                var wall = Box("Wall", new Vector2(0f, 4f), new Vector2(.5f, 8f));
                Pump(new Vector2(3f, .82f), 3f);
                Check(Position.x < -.5f, "路线中途新增高墙时不能穿过");
                UnityEngine.Object.DestroyImmediate(wall); Physics2D.SyncTransforms();
                Pump(new Vector2(3f, .82f), 6f);
                Check(Position.x > 2.5f, "障碍消失后按重算间隔恢复追击");
            });
            Case("jump-disabled", () =>
            {
                Gap(); Spawn(new Vector2(-2f, .82f)); _actor.Definition.MinionJumpHeight = 0f;
                Pump(new Vector2(2f, .82f), 5f);
                Check(Position.x < 0f && Position.y > .7f, "没有跳跃能力的小怪保留悬崖保护");
            });
            Case("snapshot-midair", () =>
            {
                Gap(); Spawn(new Vector2(-2f, .82f)); var goal = new Vector2(2f, .82f);
                for (int i = 0; i < 180 && Position.y < 1.1f; i++) Tick(goal);
                Check(Position.y > 1.1f, "快照测试实际进入跳跃");
                object saved = _actor.CaptureSnapshotState(); Vector2 position = Position; float age = _age;
                Pump(goal, 3f);
                _actor.RestoreSnapshotState(saved); _age = age;
                _actor.transform.position = position; _actor.GetComponent<Rigidbody2D>().position = position; Physics2D.SyncTransforms();
                Pump(goal, 4f);
                Check(Position.x > 1.5f && Position.y > .7f, "回溯到半空后按恢复的落点完成跳跃");
            });
            Case("guard-integration", () =>
            {
                Floor(); Box("Step", new Vector2(0f, .4f), new Vector2(1f, .8f));
                Spawn(new Vector2(-3f, .82f));
                _actor.Target.position = new Vector2(-1.5f, 2.3f); Tick(_actor.Target.position, true);
                _actor.Target.position = new Vector2(3f, .82f);
                _actor.Definition.MinionTargetMemory = 10f; _integrated = true;
                // 已观察目标的最后位置由 Brain 持有；测试时保持实际视线可用。
                Pump(new Vector2(3f, .82f), 8f);
                Check(Position.x > .8f && _maxY > 1.4f, "真实 Actor/Brain 的追击入口调用平台寻路");
                _actor.Status.SetPaused(true); var saved = Position; float brainAge = _actor.Brain.SimulationAge;
                for (int i = 0; i < 20; i++) _actor.SendMessage("FixedUpdate");
                Check(Position == saved && _actor.Brain.SimulationAge == brainAge, "暂停冻结寻路与物理推进");
                _actor.Status.SetPaused(false);
                _actor.Target.gameObject.SetActive(false);
                Pump(new Vector2(3f, .82f), 3.2f);
                _actor.Navigation.MoveTo(Position + Vector2.right * 3f, _home, _actor.Brain.SimulationAge);
                _actor.ApplyDamage(new DamageInfo { Amount = 100f });
                Check(_actor.Brain.StateName == "Hurt" && !_actor.Navigation.HasRoute && !_actor.Motor.AllowLedgeTraversal,
                    "真实受击中断路线并撤销越崖许可");
            });
            Case("spring-integration", () =>
            {
                Floor(); Spawn(new Vector2(-2f, .42f), EnemyKind.SpringToy);
                _actor.Target.position = new Vector2(3f, .42f); _integrated = true;
                Pump(new Vector2(3f, .42f), 2f);
                Check(Position.x > -1f && _maxY > 1f, "弹簧怪通过导航保留自然跳跃追击");
            });
            Finish();
        }
        private static void Finish()
        {
            string name = SessionState.GetBool(CombatOnly, false) ? "EnemyHurtboxCompatibility" : "EnemyNavigationValidation";
            if (SessionState.GetBool(RangeOnly, false)) name = "EnemyDetectionRangeValidation";
            SessionState.SetBool(Pending, false); SessionState.SetBool(CombatOnly, false); SessionState.SetBool(RangeOnly, false);
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/{name}.txt", $"Passed: {_passed}\nFailed: {_failed}\n");
            Debug.Log($"{name}: {_passed} passed, {_failed} failed");
            EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
        private static void CombatCase()
        {
            Case("hurtbox-compatibility", () =>
            {
                Floor(); Spawn(new Vector2(0f, .82f));
                var target = _actor.Target.gameObject; target.transform.position = new Vector2(1f, .82f);
                target.AddComponent<BoxCollider2D>().size = new Vector2(.7f, 1.1f);
                var health = target.AddComponent<EntityStatus>();
                var receiver = target.AddComponent<EntityDamageable>();
                var serialized = new SerializedObject(receiver);
                serialized.FindProperty("_faction").intValue = (int)Faction.Player;
                serialized.FindProperty("_invincibleSeconds").floatValue = 0f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                _actor.Combat = _root.AddComponent<CombatSystem>(); _actor.FaceDirection(Vector2.right);
                Physics2D.SyncTransforms(); float hp = health.Health;
                _actor.Attack(1.8f);
                Check(health.Health == hp, "只有身体碰撞体时攻击不会绕过受击箱");
                HurtboxChild(target); HurtboxChild(_actor.gameObject); Physics2D.SyncTransforms();
                _actor.Attack(1.8f);
                Check(health.Health == hp - _actor.Definition.Damage, "近战通过新版受击箱接口命中");
                hp = health.Health; _actor.AreaAttack(2f);
                Check(health.Health == hp - _actor.Definition.Damage, "范围攻击识别 Trigger 受击箱");
                EnemyProjectile shot = null;
                _actor.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                hp = health.Health; _actor.Shoot(Vector2.right, 20f);
                for (int i = 0; i < 10 && shot != null && shot.gameObject.activeSelf; i++)
                {
                    shot.SendMessage("FixedUpdate"); Physics2D.Simulate(.02f); Physics2D.SyncTransforms();
                }
                Check(health.Health == hp - _actor.Definition.Damage, "弹体忽略自己的受击箱并命中敌对受击箱");
                hp = health.Health; _actor.Beam(0f, .3f, 0f);
                foreach (var beam in UnityEngine.Object.FindObjectsByType<EnemyBeam>())
                {
                    beam.SendMessage("FixedUpdate"); beam.gameObject.SetActive(false);
                }
                Check(health.Health == hp - _actor.Definition.Damage, "光束通过统一准入命中 Trigger 受击箱");
                int hitCues = 0; EnemyFeedback lastHit = default;
                _actor.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.Hit) { hitCues++; lastHit = cue; } };
                float bodyHealth = _actor.Status.Health;
                _actor.Combat.PerformAttack(receiver, _actor.transform.position, Vector2.one, 1f, 0f);
                Check(_actor.Status.Health == bodyHealth - 1f && hitCues == 1 && lastHit.Damage == 1f,
                    "新受击箱攻击与受伤视觉事件同时生效且不重复通知");
                bodyHealth = _actor.Status.Health; hitCues = 0;
                var weakObject = new GameObject("WeakPoint"); weakObject.transform.SetParent(_actor.transform, false);
                weakObject.transform.localPosition = Vector2.up * 2f;
                weakObject.AddComponent<BoxCollider2D>(); weakObject.AddComponent<SpriteRenderer>();
                var weak = weakObject.AddComponent<EnemyWeakPoint>(); weak.Owner = _actor;
                HurtboxChild(weakObject);
                var weakBox = weakObject.GetComponentInChildren<Hurtbox>();
                weak.Open(5f); weak.Close(); Physics2D.SyncTransforms();
                _actor.Combat.PerformAttack(receiver, weak.transform.position, Vector2.one, 3f, 0f);
                Check(!weakBox.Area.enabled && weak.Health == 5f && hitCues == 0,
                    "关闭的弱点同时关闭受击箱，攻击不扣血也不闪光");
                weak.Open(5f); Physics2D.SyncTransforms();
                Check(weakBox.Area.enabled, "打开弱点恢复真实受击箱");
                _actor.Combat.PerformAttack(receiver, weak.transform.position, Vector2.one, 3f, 0f);
                Check(weak.Health == 2f && hitCues == 1 && lastHit.Subject == weak && lastHit.Damage == 3f,
                    "弱点经统一结算扣血并向本体发送一次弱点受伤通知");
                Check(_actor.Status.Health == bodyHealth, "弱点命中不额外扣除本体生命");
                _actor.Combat.PerformAttack(receiver, weak.transform.position, Vector2.one, 4f, 0f);
                Check(weak.Broken && hitCues == 2 && lastHit.Staggered,
                    "弱点破坏保留受伤视觉的强化通知");
                weak.Close();
                Check(!weakBox.Area.enabled, "弱点再次收回不会留下可命中的判定框");
            });
        }
        private static void RangeCases()
        {
            Case("guard-leash", () =>
            {
                Floor(); Spawn(Vector2.zero);
                _actor.Definition.MinionLeashRange = 2f; _actor.Definition.MinionSenseRange = 8f;
                _actor.Target.position = _home + Vector2.right * 3f; _integrated = true;
                Pump(_actor.Target.position, .2f);
                Check(_actor.Brain.StateName == "Patrol", "索敌内但出生点追击圈外的玩家不会引起追击或攻击");
            });
            Case("guard-return", () =>
            {
                Floor(); Spawn(Vector2.zero); _integrated = true;
                _actor.Target.position = _home + Vector2.right * 4f; Pump(_actor.Target.position, .5f);
                Check(_actor.Brain.StateName == "Chase" && Position.x > .1f, "范围内有视线的玩家触发追击");
                _actor.Target.position = _home + Vector2.right * 20f;
                bool returning = false;
                for (int i = 0; i < 160; i++)
                {
                    Tick(_actor.Target.position, true);
                    if (_actor.Brain.StateName == "Patrol" && _actor.Motor.Intent.x < 0f) { returning = true; break; }
                }
                Check(returning, "失去目标超过短暂记忆后脱战向出生点返回");
            });
            foreach (var kind in new[] { EnemyKind.BrickMinion, EnemyKind.BatMinion })
                Case("summon-range-" + kind, () =>
                {
                    Floor(); Spawn(Vector2.zero, kind); _integrated = true;
                    _actor.Target.position = _home + Vector2.right * 20f; Pump(_actor.Target.position, 2f);
                    Check(Vector2.Distance(Position, _home) < .25f, kind + " 不会自动追踪索敌圈外的玩家");
                    _actor.Target.position = _home + Vector2.right * 3f; Pump(_actor.Target.position, .2f);
                    Check(Vector2.Distance(Position, _home) > .05f, kind + " 在范围内正常发现与接近玩家");
                    _actor.Target.position = _home + Vector2.right * 20f; Pump(_actor.Target.position, 5f);
                    Check(Vector2.Distance(Position, _home) < .6f, kind + " 丢失目标后返回而不是持续追踪");
                });
        }
        private static void HurtboxChild(GameObject owner)
        {
            var go = new GameObject("Hurtbox"); go.transform.SetParent(owner.transform, false); go.layer = Hurtbox.Layer;
            var box = go.AddComponent<BoxCollider2D>(); box.size = new Vector2(.7f, 1.1f); box.isTrigger = true;
            go.AddComponent<Hurtbox>();
        }
        private static Vector2 Position => _actor.GetComponent<Rigidbody2D>().position;
        private static void Pump(Vector2 goal, float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds / .02f); i++) Tick(goal, _integrated);
        }
        private static void Tick(Vector2 goal, bool integrated = false)
        {
            _age += .02f;
            if (integrated) _actor.SendMessage("FixedUpdate");
            else
            {
                _actor.Motor.AllowLedgeTraversal = false;
                _actor.Navigation.MoveTo(goal, _home, _age);
                _actor.Motor.Tick(.02f);
            }
            Physics2D.Simulate(.02f); Physics2D.SyncTransforms();
            _minX = Mathf.Min(_minX, Position.x); _maxY = Mathf.Max(_maxY, Position.y);
        }
        private static void Floor() => Box("Floor", new Vector2(0f, -.5f), new Vector2(24f, 1f));
        private static void Gap()
        {
            Box("Left", new Vector2(-3.3f, -.5f), new Vector2(5.4f, 1f));
            Box("Right", new Vector2(3.3f, -.5f), new Vector2(5.4f, 1f));
        }
        private static GameObject Box(string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name); go.transform.SetParent(_root.transform); go.layer = 8;
            go.transform.position = position; go.AddComponent<BoxCollider2D>().size = size;
            Physics2D.SyncTransforms(); return go;
        }
        private static void Spawn(Vector2 position, EnemyKind kind = EnemyKind.BrickSoldier)
        {
            var target = new GameObject("Target"); target.transform.SetParent(_root.transform); target.transform.position = new Vector2(6f, 3f);
            var go = new GameObject("Actor"); go.SetActive(false); go.layer = 9; go.transform.SetParent(_root.transform);
            _actor = go.AddComponent<EnemyActor>(); _actor.enabled = false;
            string folder = kind == EnemyKind.BrickMinion || kind == EnemyKind.BatMinion ? "" : "/FieldMinions";
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/_Project/Data/Enemies{folder}/{kind}_Definition.asset");
            _actor.Definition = UnityEngine.Object.Instantiate(definition); _actor.Definition.Health = 500f;
            if (position == Vector2.zero) position.y = _actor.Definition.Flying ? 3.5f : _actor.Definition.BodySize.y * .5f + .02f;
            _actor.GetComponent<BoxCollider2D>().size = _actor.Definition.BodySize;
            go.transform.position = position; _home = position;
            _actor.ArenaX = new Vector2(-10f, 10f); _actor.ArenaY = new Vector2(0f, 10f);
            _actor.Target = target.transform; go.SetActive(true); _actor.SendMessage("Start");
            _actor.Motor.Tick(.02f); Physics2D.Simulate(.02f); Physics2D.SyncTransforms();
            _minX = Position.x; _maxY = Position.y;
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message + $"; position={Position}; minX={_minX}; maxY={_maxY}; grounded={_actor.Motor.Grounded}; route={_actor.Navigation.HasRoute}; state={_actor.Brain.StateName}");
            _passed++; Debug.Log("PASS: " + message);
        }
        private static void Case(string name, Action test)
        {
            string selected = Environment.GetEnvironmentVariable("RULE_NAV_CASE");
            if (!string.IsNullOrEmpty(selected) && selected != name) return;
            _root = new GameObject(name); _age = 0f; _integrated = false;
            Physics2D.gravity = new Vector2(0f, -9.81f);
            try { test(); }
            catch (Exception error) { _failed++; Debug.LogError(name + ": " + error); }
            finally
            {
                if (_actor != null) UnityEngine.Object.DestroyImmediate(_actor.Definition);
                UnityEngine.Object.DestroyImmediate(_root); Physics2D.SyncTransforms();
            }
        }
    }
}
