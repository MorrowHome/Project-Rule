using System;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Presentation.Enemy;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>副本内使用真实蝙蝠配置与物理步检查空战、预警、撤离、边界和暂停恢复。</summary>
    public static class BatFlightValidation
    {
        private const string Pending = "RuleGame.Enemy.BatFlightValidation";
        private static EnemyActor _bat;
        private static Rigidbody2D _body;
        private static int _step, _passed;
        private static float _due, _windupAt, _peakSpeed, _age;
        private static double _deadline;
        private static Vector2 _start, _evadeStart, _savedPosition, _savedVelocity;
        private static object _snapshot;
        private static SpriteRenderer _visual;
        private static EnemySpriteSequence _windupClip, _spreadClip;
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/BatFlightValidation.txt"));

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
            TimeManager.Initialize(); WorldRegistry.Initialize();
            RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
            EnemyDefinition config = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/BatBoss_Definition.asset");
            if (config == null) { Finish("缺少真实蝙蝠配置"); return; }
            var combat = new GameObject("Combat").AddComponent<CombatSystem>();
            var floor = new GameObject("Floor"); floor.layer = 8;
            floor.transform.position = new Vector3(0, -0.5f, 0);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(40, 1);
            var target = new GameObject("Target"); target.transform.position = new Vector3(0, 0.6f, 0);
            target.AddComponent<BoxCollider2D>().size = new Vector2(0.7f, 1.2f);
            target.AddComponent<EntityStatus>();
            var damageable = target.AddComponent<EntityDamageable>(); damageable.SetInvulnerable(true);
            var go = new GameObject("Bat"); go.layer = 9; go.SetActive(false);
            go.transform.position = new Vector3(7, 6, 0);
            _bat = go.AddComponent<EnemyActor>(); _bat.Definition = config;
            _bat.Target = target.transform; _bat.Combat = combat;
            go.GetComponent<BoxCollider2D>().size = config.BodySize;
            var animations = AssetDatabase.LoadAssetAtPath<EnemyAnimationSet>("Assets/_Project/Data/Enemies/BatBoss_Animations.asset");
            if (animations == null) { Finish("缺少真实蝙蝠动画配置"); return; }
            _windupClip = Array.Find(animations.Sequences, sequence => sequence.Action == "DashWindup");
            _spreadClip = Array.Find(animations.Sequences, sequence => sequence.Action == "WingSpread");
            var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false);
            _visual = visual.AddComponent<SpriteRenderer>();
            var animator = visual.AddComponent<EnemySpriteAnimator>(); animator.Actor = _bat; animator.AnimationSet = animations;
            go.SetActive(true); _body = go.GetComponent<Rigidbody2D>(); _start = _body.position;
            _deadline = EditorApplication.timeSinceStartup + 35;
            _due = Time.fixedTime + 0.65f;
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup > _deadline) { Finish("飞行验证超时，状态=" + _bat.Brain?.StateName); return; }
            if (_bat.Brain == null) return;
            _peakSpeed = Mathf.Max(_peakSpeed, _bat.Motor.Velocity.magnitude);
            if (Time.fixedTime < _due) return;
            try
            {
                switch (_step)
                {
                    case 0:
                        Check(_bat.Definition.MoveSpeed >= 5 && _bat.Definition.FlightAcceleration >= 60, "真实配置提高飞行速度和加速度");
                        Check(_windupClip != null && _windupClip.Frames.Length == 4 && Mathf.Approximately(_windupClip.Duration, 0.5f),
                            "俯冲预警四帧按正常八帧每秒播放");
                        Check(_spreadClip != null && _spreadClip.Frames.Length == 23, "原完整展翅施法动画保持不变");
                        Check(Vector2.Distance(_body.position, _start) > 1f && _body.position.y < _start.y - 0.5f, "正常物理步完成空中换位");
                        _step++; break;
                    case 1:
                        if (_bat.Brain.StateName != "Windup" || _bat.Brain.ActionElapsed < 0.06f) return;
                        Check(_bat.Brain.Phase == 1, "第一阶段即可发动俯冲");
                        Check(_bat.AnimationName == "DashWindup" && Array.IndexOf(_windupClip.Frames, _visual.sprite) >= 0,
                            "实际渲染使用短展翅帧，未加速播放完整拍翼动画");
                        _windupAt = _bat.Brain.SimulationAge - _bat.Brain.ActionElapsed;
                        _bat.Target.position = new Vector3(8, 0.6f, 0); Physics2D.SyncTransforms();
                        _step++; break;
                    case 2:
                        if (_bat.Brain.StateName != "Dash" || _bat.Brain.ActionElapsed < 0.04f) return;
                        Check(_bat.Brain.SimulationAge - _windupAt >= _bat.Definition.BatDashWindup, "俯冲前保留完整预警");
                        Check(_bat.Motor.Intent.x < 0 && _bat.Facing < 0, "预警后玩家换边不会使俯冲自动转向");
                        _step++; break;
                    case 3:
                        if (_bat.Brain.StateName != "Recover") return;
                        Check(_peakSpeed > _bat.Definition.MoveSpeed * 1.5f, "俯冲达到显著高于巡航的速度");
                        Check(_bat.AnimationDuration >= 0.3f, "俯冲后有可追击的恢复窗口");
                        _step++; break;
                    case 4:
                        if (_bat.Brain.StateName != "Rest") return;
                        Check(_bat.Motor.Intent == Vector2.zero && _bat.Definition.BossRestDuration >= 1.8f,
                            "真实蝙蝠攻击后进入可配置的完整休息窗口");
                        _savedPosition = _body.position; _due = Time.fixedTime + .3f; _step = 40; break;
                    case 40:
                        Check(_bat.Brain.StateName == "Rest" && Vector2.Distance(_body.position, _savedPosition) < .02f,
                            "休息时实际悬停，不继续高速撤离");
                        _step = 41; break;
                    case 41:
                        if (_bat.Brain.StateName != "Fly") return;
                        _bat.Target.position = _bat.transform.position + Vector3.right * 2.1f;
                        Physics2D.SyncTransforms(); _step = 5; break;
                    case 5:
                        if (_bat.Brain.StateName != "Evade") return;
                        Check(_bat.Motor.SpeedMultiplier > 1f, "玩家逼近时主动高速撤离");
                        _evadeStart = _body.position; _due = Time.fixedTime + 0.35f; _step++; break;
                    case 6:
                        Check(Vector2.Distance(_body.position, _evadeStart) > 1f, "撤离实际改变空间位置");
                        _bat.Target.position = new Vector3(30, 20, 0); Physics2D.SyncTransforms();
                        _due = Time.fixedTime + 3f; _step++; break;
                    case 7:
                        Vector2 half = _bat.Definition.BodySize * 0.5f;
                        Check(_body.position.x + half.x <= _bat.ArenaX.y + 0.05f
                            && _body.position.y + half.y <= _bat.ArenaY.y + 0.05f, "飞行目标受水平和高度边界约束");
                        _age = _bat.Brain.SimulationAge; _savedPosition = _body.position;
                        TimeManager.Instance.SetPaused(true); _due = Time.fixedTime + 0.2f; _step++; break;
                    case 8:
                        Check(_bat.Brain.SimulationAge == _age && Vector2.Distance(_body.position, _savedPosition) < 0.01f, "暂停冻结飞行与战斗时钟");
                        TimeManager.Instance.SetPaused(false);
                        _snapshot = _bat.CaptureSnapshotState(); _age = _bat.Brain.SimulationAge;
                        _savedPosition = _body.position; _savedVelocity = _bat.Motor.Velocity;
                        _due = Time.fixedTime + 0.3f; _step++; break;
                    case 9:
                        _body.position = _savedPosition; _bat.RestoreSnapshotState(_snapshot);
                        Check(Mathf.Approximately(_bat.Brain.SimulationAge, _age) && _bat.Motor.Velocity == _savedVelocity,
                            "快照恢复飞行状态、时钟与速度");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name + "，状态=" + _bat.Brain.StateName);
            _passed++; Debug.Log("PASS: " + name);
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, $"Passed: {_passed}\nFailed: {(error == null ? 0 : 1)}\n{error}\n");
            if (error != null) Debug.LogError(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
