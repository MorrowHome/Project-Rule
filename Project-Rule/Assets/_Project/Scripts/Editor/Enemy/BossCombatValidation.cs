using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RuleGame.Presentation.Enemy;
using RuleGame.Infrastructure.Snapshot;
using UnityEngine.Rendering;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using RuleGame.Infrastructure.Rules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>在可丢弃的工程副本中进入 Play Mode，验证真实组件与 HFSM，不保存生产场景。</summary>
    public static class BossCombatValidation
    {
        private const string Pending = "RuleGame.Enemy.CombatValidation";
        private const string Tactics = "RuleGame.Enemy.TacticsValidation";
        private const string RabbitTactics = "RuleGame.Enemy.RabbitTacticsValidation";
        private const string ClockHands = "RuleGame.Enemy.ClockHandsValidation";
        private const string ClockRewind = "RuleGame.Enemy.ClockRewindValidation";
        private const string SnapshotCamera = "RuleGame.Enemy.SnapshotCameraValidation";
        private static readonly List<EnemyActor> Actors = new List<EnemyActor>();
        private static readonly List<Action<EnemyActor>> Cases = new List<Action<EnemyActor>>();
        private static int _passed, _failed;
        private static double _deadline;
        private static bool _tactics;
        private static string Output => Environment.GetEnvironmentVariable("RULE_BOSS_OUTPUT")
            ?? Path.GetFullPath(Path.Combine(Application.dataPath, _tactics ? "../Logs/BossTacticsValidation.txt" : "../Logs/BossCombatValidation.txt"));

        public static void RunRabbitTacticsBatch()
        {
            SessionState.SetBool(RabbitTactics, true);
            RunBatch();
        }

        public static void RunClockHandsBatch()
        {
            SessionState.SetBool(ClockHands, true);
            RunBatch();
        }

        public static void RunClockRewindBatch()
        {
            SessionState.SetBool(ClockRewind, true);
            RunBatch();
        }

        public static void RunSnapshotCameraBatch()
        {
            SessionState.SetBool(SnapshotCamera, true);
            RunBatch();
        }

        public static void RunTacticsBatch()
        {
            SessionState.SetBool(Tactics, true);
            RunBatch();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本中运行。");
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
            TimeManager.Initialize(); WorldRegistry.Initialize();
            if (SessionState.GetBool(ClockRewind, false) || SessionState.GetBool(ClockHands, false))
                RuleSystem.Initialize(new TimeRevertCatalog());
            else RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
            _deadline = EditorApplication.timeSinceStartup + 30;
            _tactics = SessionState.GetBool(Tactics, false);
            if (SessionState.GetBool(RabbitTactics, false))
            {
                AddRabbitTacticsCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            if (SessionState.GetBool(ClockHands, false))
            {
                AddClockHandCases();
                AddClockRewindCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            if (SessionState.GetBool(ClockRewind, false))
            {
                AddClockRewindCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            if (SessionState.GetBool(SnapshotCamera, false))
            {
                AddSnapshotCameraCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            if (_tactics)
            {
                AddTacticsCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            Add(EnemyKind.BrickBoss, a => { Target(a, 2); Step(a); Check(a.Brain.StateName == "HammerPlayer", "积木近距锤击"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 5); Step(a); Check(a.Brain.StateName == "Advance", "积木中距主动压近"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 8); Step(a); Check(a.Brain.StateName == "Throw", "积木远距投掷"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 11); Step(a); Check(a.Brain.StateName == "Walk" && a.Motor.Intent.x > 0, "超射程追击"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 2, 5); Step(a); Check(a.Brain.StateName == "Throw", "高处目标使用投掷"); });
            Add(EnemyKind.BrickBoss, a =>
            {
                Cooldown(a); Target(a, 1); Step(a);
                Check(a.Motor.Intent.x < 0 && a.Facing > 0, "冷却时面向玩家后撤");
                Target(a, 7); Step(a);
                Check(a.Motor.Intent.x > 0 && a.Brain.StateName == "Walk", "冷却时靠近远处玩家");
                Step(a, a.Definition.AttackCooldown + 0.01f);
                Check(a.Brain.StateName == "Throw", "冷却结束重新按距离选招");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                Cooldown(a); a.transform.position = new Vector3(-11f, 0, 0); Target(a, 1); Step(a);
                Check(a.Motor.Intent.x == 0, "后撤受场地边界约束");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                Cooldown(a); Target(a, 4); Step(a); Target(a, 3.3f); Step(a);
                Check(a.Brain.StateName == "Walk", "进入较窄停止区前继续移动");
                Target(a, 3.1f); Step(a); Target(a, 3.35f); Step(a);
                Check(a.Brain.StateName == "Idle" && a.Motor.Intent == Vector2.zero, "距离边缘不反复启停");
            });
            Add(EnemyKind.BatBoss, a => { Target(a, 2); Step(a); Check(a.Brain.StateName == "Slash", "蝙蝠近身优先斩击，不先逃走"); });
            Add(EnemyKind.BatBoss, a => { Target(a, 6); Step(a); Check(a.Brain.StateName == "Fly" && a.Motor.Intent.sqrMagnitude > 0, "蝙蝠先空中换位"); });
            Add(EnemyKind.BatBoss, a => { Target(a, 11); Step(a); Check(a.Brain.StateName == "Fly" && a.Motor.Intent.x > 0, "蝙蝠远距飞行追击"); });
            Add(EnemyKind.BatBoss, a => { Target(a, 2, 6); Step(a); Check(a.Brain.StateName != "Slash", "蝙蝠不向高差过大目标空斩"); });
            Add(EnemyKind.BatBoss, a =>
            {
                Target(a, 6, 4); AdvanceTo(a, "Windup");
                Check(a.Brain.Phase == 1, "蝙蝠一阶段解锁俯冲");
                Check(a.AnimationName == "DashWindup", "俯冲预警使用独立短展翅序列");
                Step(a, 0.2f);
                Check(a.Brain.StateName == "Windup", "俯冲预警不会提前结束");
                Target(a, -6, 4); Step(a, 0.31f); Step(a);
                Check(a.Brain.StateName == "Dash" && a.Motor.Intent.x > 0 && a.Facing > 0, "俯冲保持预警时锁定的路线和朝向");
                Vector2 direction = a.Motor.Intent; object saved = a.CaptureSnapshotState();
                Step(a, 0.2f); a.RestoreSnapshotState(saved); Step(a);
                Check(a.Brain.StateName == "Dash" && a.Motor.Intent == direction, "快照恢复锁定的俯冲路线");
            });
            Add(EnemyKind.BatBoss, a =>
            {
                Target(a, 6, 4); AdvanceTo(a, "Windup"); a.Brain.OnHit(30);
                Check(a.Brain.StateName == "Hurt", "达到破韧门槛可打断俯冲蓄力");
                Step(a, 0.46f);
                Check(a.Brain.StateName == "Evade", "蝙蝠从硬直恢复后主动脱离");
            });
            Add(EnemyKind.BatBoss, a =>
            {
                Target(a, 6, 4); a.Status.ApplyHealthDelta(-334); Step(a); Step(a, 2.6f);
                AdvanceTo(a, "Windup");
                Check(a.Brain.Phase == 2 && a.AnimationDuration < a.Definition.BatDashWindup, "后续阶段缩短俯冲预警");
                object saved = a.CaptureSnapshotState(); float duration = a.AnimationDuration;
                Step(a, 1); a.RestoreSnapshotState(saved);
                Check(Mathf.Approximately(a.AnimationDuration, duration), "恢复快照保留阶段对应的攻击节奏");
            });
            Add(EnemyKind.ClockBoss, a => { Target(a, 2); Step(a, 2.4f); Check(a.Brain.StateName == "JumpShake", "发条贴近且冲撞就绪时响铃反击"); });
            Add(EnemyKind.ClockBoss, a =>
            {
                Target(a, 5); Step(a);
                Check(a.Brain.StateName == "Idle", "发条保持激光作战距离");
                Target(a, 11); Step(a);
                Check(a.Motor.Intent.x > 0, "发条远处追击");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                Target(a, 2); Step(a, 0.3f); Step(a, 0.8f);
                Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Burrow" && a.Motor.Intent.x < 0, "兔子近身藏身撤退");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                Target(a, 11); Step(a, .3f); Step(a, 2.1f);
                Step(a, a.Definition.BossRestDuration + .01f); Step(a);
                Check(a.Motor.Intent.x > 0, "兔子超射程靠近");
            });
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BatBoss })
                Add(kind, a =>
                {
                    a.Brain.OnHit(10);
                    Check(a.Brain.StateName != "Hurt", a.Definition.Kind + " 普通单次命中不打断");
                    a.Brain.OnHit(10); a.Brain.OnHit(10);
                    Check(a.Brain.StateName == "Hurt", a.Definition.Kind + " 累计伤害触发硬直");
                    Check(a.AnimationDuration <= 0.45f, a.Definition.Kind + " 硬直时长上限");
                    Step(a, 0.46f); a.Brain.OnHit(100);
                    Check(a.Brain.StateName != "Hurt", a.Definition.Kind + " 恢复后冷却禁止再打断");
                    Step(a, 4.01f); a.Brain.OnHit(10);
                    Check(a.Brain.StateName != "Hurt", a.Definition.Kind + " 冷却期间不积攒硬直");
                    a.Brain.OnHit(20);
                    Check(a.Brain.StateName == "Hurt", a.Definition.Kind + " 冷却结束可再次破韧");
                });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(20); Step(a, 1.6f); a.Brain.OnHit(10);
                Check(a.Brain.StateName != "Hurt", "久未命中清空累计伤害");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(10); object snapshot = a.CaptureSnapshotState();
                a.Brain.OnHit(20); a.RestoreSnapshotState(snapshot); a.Brain.OnHit(10);
                Check(a.Brain.StateName != "Hurt", "快照恢复累计硬直进度");
                a.Brain.OnHit(10); snapshot = a.CaptureSnapshotState();
                Step(a, 5); a.RestoreSnapshotState(snapshot); Step(a, 0.46f); a.Brain.OnHit(100);
                Check(a.Brain.StateName != "Hurt", "快照恢复硬直冷却");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(30); Step(a, 0.46f); float age = a.Brain.SimulationAge;
                a.Status.SetPaused(true); a.Brain.Tick(10);
                Check(a.Brain.SimulationAge == age, "暂停冻结 AI 与硬直时钟");
                a.Status.SetPaused(false); a.Brain.OnHit(100);
                Check(a.Brain.StateName != "Hurt", "暂停未消耗硬直冷却");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                float health = a.Status.Health;
                a.ApplyDamage(new DamageInfo { Amount = 10 });
                Check(a.Status.Health == health - 10 && a.Brain.StateName != "Hurt", "伤害入口正常扣血且单次不硬直");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(30); Step(a, 0.46f); float health = a.Status.Health;
                a.ApplyDamage(new DamageInfo { Amount = 10 });
                Check(a.Status.Health == health - 10 && a.Brain.StateName != "Hurt", "硬直冷却期间正常扣血");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                Target(a, 11); Step(a); a.Target.gameObject.SetActive(false); Step(a);
                Check(a.Brain.StateName == "Idle" && a.Motor.Intent == Vector2.zero, "目标停用后停止追击");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(20); a.Status.ApplyHealthDelta(-260); Step(a);
                Check(a.Brain.StateName == "Split", "阶段分裂优先于普通行为");
                Step(a, 1.1f); a.Brain.OnHit(10);
                Check(a.Brain.StateName != "Hurt", "阶段过渡清空累计伤害");
            });
            Add(EnemyKind.ClockBoss, a =>
            {
                a.Status.ApplyHealthDelta(-334); Step(a);
                a.Status.ApplyHealthDelta(-333); Step(a);
                Check(a.Brain.StateName == "Stun" && a.Brain.DamageLocked, "发条阶段眩晕窗口仍有效");
                a.Brain.OnHit(100);
                Check(a.Brain.StateName == "Stun", "普通硬直不覆盖阶段眩晕");
                Step(a, a.Definition.ClockCoreWindow + 0.01f); Step(a, 1);
                Check(a.Brain.Phase == 2 && Mathf.Approximately(a.Status.Health, 600), "发条窗口失败仍回溯自身生命");
            });
            Add(EnemyKind.BatBoss, a =>
            {
                a.Status.ApplyHealthDelta(-334); Step(a);
                Check(a.Brain.StateName == "Weak", "蝙蝠阶段虚弱仍有效");
                a.Brain.OnHit(100); Step(a, 2.6f);
                Check(a.Brain.Phase == 2 && a.Brain.StateName != "Hurt", "阶段虚弱不被普通受击覆盖");
            });
            Add(EnemyKind.BrickMinion, a => { a.Brain.OnHit(1); Check(a.Brain.StateName == "Hurt", "小怪保留普通受击"); });
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BatBoss })
                Add(kind, a =>
                {
                    a.Definition.BossRestDuration = 1.8f;
                    Target(a, 3); AdvanceTo(a, "Rest");
                    string label = a.Definition.Kind.ToString();
                    bool bat = a.Definition.Kind == EnemyKind.BatBoss;
                    Check(bat ? a.Motor.Intent.y > 0f : a.Motor.Intent == Vector2.zero && a.Motor.Velocity == Vector2.zero,
                        label + (bat ? " 完成攻击后上升至休息航线" : " 完成一套攻击后停下休息"));
                    a.Combat = new GameObject("RestCombat_" + label).AddComponent<RuleGame.Gameplay.Combat.CombatSystem>();
                    int attacks = 0;
                    a.Feedback += cue =>
                    {
                        if (cue.Kind == EnemyFeedbackKind.Strike || cue.Kind == EnemyFeedbackKind.ProjectileSpawn
                            || cue.Kind == EnemyFeedbackKind.BeamSpawn || cue.Kind == EnemyFeedbackKind.RuleCast
                            || cue.Kind == EnemyFeedbackKind.Summon) attacks++;
                    };
                    Target(a, 0.1f);
                    for (int i = 0; i < 10; i++) Step(a);
                    Check(a.Brain.StateName == "Rest" && attacks == 0 && (bat || a.Motor.Intent == Vector2.zero),
                        label + " 玩家贴近也不提前进攻或施法");
                    float remaining = a.Brain.RestRemaining;
                    a.Status.SetPaused(true); Step(a, 10); a.Status.SetPaused(false);
                    Check(Mathf.Approximately(a.Brain.RestRemaining, remaining), label + " 暂停冻结休息计时");
                    object saved = a.CaptureSnapshotState(); Step(a, 0.3f); a.RestoreSnapshotState(saved);
                    Check(a.Brain.StateName == "Rest" && Mathf.Approximately(a.Brain.RestRemaining, remaining),
                        label + " 快照恢复休息进度");
                    if (bat)
                    {
                        Step(a); Vector2 intent = a.Motor.Intent;
                        a.RestoreSnapshotState(saved); Step(a);
                        Check(a.Motor.Intent == intent, "快照恢复高空休息巡飞路线与方向");
                        a.RestoreSnapshotState(saved);
                    }
                    float health = a.Status.Health;
                    a.ApplyDamage(new DamageInfo { Amount = 10f });
                    Check(a.Status.Health == health - 10f && a.Brain.StateName == "Rest", label + " 休息中可以正常承受反击");
                    a.Brain.OnHit(30f); Step(a, .46f);
                    Check(a.Brain.StateName == "Rest" && (bat || a.Motor.Intent == Vector2.zero), label + " 破韧恢复不会跳过剩余休息");
                    float left = a.Brain.RestRemaining;
                    Step(a, left - .05f);
                    Check(a.Brain.StateName == "Rest" && attacks == 0, label + " 保留完整休息窗口");
                    Step(a, .06f);
                    Check(a.Brain.StateName != "Rest", label + " 到时恢复行动");
                    Target(a, 3);
                    for (int i = 0; i < 200 && attacks == 0; i++) Step(a);
                    Check(attacks > 0, label + " 休息后能重新发动攻击");
                });
            Add(EnemyKind.ClockBoss, a =>
            {
                Target(a, 6); int volleys = 0;
                a.Definition.ClockBeamVolleysBeforeRest = 2;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.BeamSpawn) volleys++; };
                AdvanceTo(a, "Rest");
                Check(volleys == 2, "发条远程指针攻击两轮后也会休息");
                for (int i = 0; i < 15; i++) Step(a, .1f);
                Check(volleys == 2, "发条休息期间不从并行指针时钟偷发激光");
            });
            Add(EnemyKind.BatBoss, a =>
            {
                a.Definition.BossRestDuration = 2.5f;
                Target(a, 3); AdvanceTo(a, "Rest");
                Step(a, 2.3f);
                Check(a.Brain.StateName == "Rest", "Inspector 配置可延长蝙蝠休息时间");
                a.Status.ApplyHealthDelta(-334); Step(a);
                Check(a.Brain.StateName == "Weak", "阶段过渡优先于普通休息");
            });
            EditorApplication.update += AwaitReady;
        }

        private static void AddRabbitCase(Action<EnemyActor> test)
            => Add(EnemyKind.RabbitBoss, test, config =>
            {
                var delivered = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/RabbitBoss_Definition.asset");
                if (delivered == null) throw new InvalidOperationException("缺少已交付兔子定义");
                config.Health = 100f; config.Energy = 0f;
                config.BodySize = delivered.BodySize;
                config.Timings = (EnemyActionTiming[])delivered.Timings.Clone();
            });
        private static void RabbitFlip(EnemyActor actor)
        {
            actor.Status.ApplyHealthDelta(50f - actor.Status.Health);
            Step(actor); Step(actor, actor.AnimationDuration + .01f);
        }
        private static void AddRabbitTacticsCases()
        {
            AddRabbitCase(a =>
            {
                Target(a, 14); int shots = 0;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shots++; };
                Step(a, a.AnimationDuration + .01f); Step(a, .1f);
                Check(a.Brain.StateName == "Reposition" && a.Motor.Intent.x > 0f, "兔子远处先主动追近，不站桩播放空投掷");
                Step(a, a.Definition.RabbitRepositionDuration + .01f);
                Check(a.Brain.StateName == "Reposition" && shots == 0, "目标仍超射程时持续换位，不进入攻击休息死循环");
            });
            AddRabbitCase(a =>
            {
                Target(a, 6); EnemyProjectile shot = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                Step(a, a.AnimationDuration + .01f); Target(a, 8); Step(a, .8f);
                Target(a, -8); Step(a, .4f);
                Check(shot == null, "原画出手前留出可躲避的锁定窗口");
                Step(a, .31f);
                Check(shot != null && shot.Velocity.x > 0f && shot.Velocity.y > 0f && shot.VisualIndex == 0,
                    "低弧弹珠先向上抛向锁定位置，锁定后不追踪玩家换边");
            });
            AddRabbitCase(a =>
            {
                Target(a, 6, 4); EnemyProjectile shot = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                Step(a, a.AnimationDuration + .01f); Step(a, 1.51f);
                Check(shot != null && shot.VisualIndex == 1 && shot.Gravity > .5f && shot.Velocity.y > 0f,
                    "高处目标改用跳棋高抛，不只按固定轮次换弹");
            });
            AddRabbitCase(a =>
            {
                a.transform.position = new Vector3(10.8f, 2f, 0f); Target(a, -2);
                Step(a, .6f); Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "HideThrow", "墙角无撤退空间时原地反击，不倒退穿过玩家");
                EnemyProjectile shot = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                Step(a, 5.14f);
                Check(shot != null && shot.VisualIndex == 1 && shot.Velocity.x < 0f,
                    "墙角反击完整冒出后向玩家高抛");
            });
            AddRabbitCase(a =>
            {
                Target(a, 2); Step(a, .6f); Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Burrow" && a.Motor.Intent.x < 0f, "近身先缩回再用空帽跳跃撤退");
                object saved = a.CaptureSnapshotState(); Target(a, -2); Step(a);
                Check(a.Motor.Intent.x < 0f, "撤退路线不随玩家穿越而抽搐换向");
                Target(a, 2); Step(a, a.Definition.RabbitBurrowDuration + .01f);
                Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Throw", "长冒出动画结束后保留撤退冷却，避免无限藏帽不反击");
                a.RestoreSnapshotState(saved); Step(a);
                Check(a.Brain.StateName == "Burrow" && a.Motor.Intent.x < 0f && !a.IsFlying,
                    "快照恢复地面撤退路线与移动模式");
            });
            AddRabbitCase(a =>
            {
                a.transform.position = new Vector3(0f, 2f, 0f);
                var body = a.GetComponent<Rigidbody2D>(); body.position = a.transform.position;
                a.GetComponent<BoxCollider2D>().size = a.Definition.BodySize;
                Target(a, 6); RabbitFlip(a);
                Check(a.Brain.StateName == "AirReposition" && a.IsFlying && !a.Definition.Flying,
                    "翻转后启用实例飞行，不改共享定义或只有飞行外观");
                var visual = new GameObject("RabbitVisual"); visual.SetActive(false);
                visual.transform.SetParent(a.transform, false); visual.transform.localPosition = Vector3.down * 1.5f;
                var renderer = visual.AddComponent<SpriteRenderer>();
                var animator = visual.AddComponent<EnemySpriteAnimator>(); animator.Actor = a;
                animator.AnimationSet = AssetDatabase.LoadAssetAtPath<EnemyAnimationSet>("Assets/_Project/Data/Enemies/RabbitBoss_Animations.asset");
                visual.SetActive(true); animator.SendMessage("LateUpdate");
                var flight = Array.Find(animator.AnimationSet.Sequences, sequence => sequence.Action == "Flight");
                Check(flight.NativeFrameRate && flight.FrameRate == 8f && Array.IndexOf(flight.Frames, renderer.sprite) >= 0,
                    "翻转换侧使用交付的原生八帧飞行序列");
                var snapshots = SnapshotsFor(a); var saved = snapshots.Capture();
                Vector2 start = body.position;
                var floor = new GameObject("RabbitFlightFloor"); floor.layer = 8;
                floor.transform.position = new Vector3(0f, 0f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(40f, 1f);
                a.PlayerMask = 0; Physics2D.SyncTransforms();
                SimulationMode2D previousMode = Physics2D.simulationMode;
                try
                {
                    Physics2D.simulationMode = SimulationMode2D.Script;
                    for (int i = 0; i < 20; i++) { Step(a); a.Motor.Tick(.02f); Physics2D.Simulate(.02f); }
                    Check(body.position.y > start.y + .6f && body.position.y < a.ArenaY.y - a.Definition.BodySize.y * .5f,
                        "真实物理升空换位且留在场地高度范围");
                }
                finally { Physics2D.simulationMode = previousMode; }
                snapshots.Restore(saved); Step(a); Vector2 route = a.Motor.Intent;
                Target(a, -6); Step(a);
                Check(a.IsFlying && a.Brain.StateName == "AirReposition" && Vector2.Dot(route, a.Motor.Intent) > .99f,
                    "飞行快照恢复后保持锁定路线，不瞬移追踪换边");
                a.Target.gameObject.SetActive(false); Step(a);
                Check(a.Motor.Intent == Vector2.zero, "丢失目标停止飞行追击");
            });
            AddRabbitCase(a =>
            {
                a.transform.position = new Vector3(0f, 4f, 0f);
                a.GetComponent<Rigidbody2D>().position = a.transform.position;
                a.GetComponent<BoxCollider2D>().size = a.Definition.BodySize;
                Physics2D.SyncTransforms();
                Target(a, 6); RabbitFlip(a); Target(a, 5, -2);
                Step(a, a.Definition.RabbitAirMoveDuration + .01f);
                var velocities = new List<Vector2>(); var variants = new List<int>();
                a.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.ProjectileSpawn) return;
                    var shot = (EnemyProjectile)cue.Subject; velocities.Add(shot.Velocity); variants.Add(shot.VisualIndex);
                };
                Step(a, .36f);
                Check(velocities.Count == 3 && variants.TrueForAll(index => index == 2), "第二阶段用鸭子打三发扇形压制");
                Check(Mathf.Abs(velocities[0].x - velocities[2].x) > 2f, "斜向攻击时三发仍有角度展开，不重叠为一条线");
                Step(a, a.Definition.RabbitBurstInterval + .01f);
                Check(velocities.Count == 6, "散射仅按配置发两轮，保留间隔");
                Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "Rest" && a.Motor.Intent == Vector2.zero && a.IsFlying,
                    "空中散射结束悬停休息，不连续追打");
                float health = a.Status.Health;
                a.ApplyDamage(new DamageInfo { Amount = 10f });
                Check(a.Status.Health == health - 10f && a.Brain.StateName == "Rest", "空中休息仍可被玩家反击，没有新增无敌");
                float age = a.Brain.SimulationAge; object saved = a.CaptureSnapshotState();
                a.Status.SetPaused(true); Step(a, 5); a.Status.SetPaused(false);
                Check(a.Brain.SimulationAge == age, "暂停冻结兔子换位和散射时钟");
                Step(a, a.Brain.RestRemaining + .01f); Step(a, .1f); a.RestoreSnapshotState(saved);
                Check(a.IsFlying && a.Brain.StateName == "Rest", "快照恢复飞行阶段及剩余喘息");
            });
        }

        private static void AddClockHandCase(Action<EnemyActor> test)
            => Add(EnemyKind.ClockBoss, test, config =>
            {
                config.ClockHourInterval = 2f;
                config.ClockMinuteInterval = 1.2f;
                config.ClockSecondInterval = .6f;
                config.ClockBeamVolleysBeforeRest = 100;
            });
        private static void ClockPhaseTwo(EnemyActor actor)
        {
            actor.Status.ApplyHealthDelta(666f - actor.Status.Health);
            Step(actor);
        }
        private static void ClockOpenCore(EnemyActor actor)
        {
            var core = new GameObject("ClockValidationCore");
            core.AddComponent<BoxCollider2D>();
            core.AddComponent<SpriteRenderer>();
            actor.WeakPoint = core.AddComponent<EnemyWeakPoint>();
            actor.WeakPoint.Owner = actor;
            actor.Status.ApplyHealthDelta(330f - actor.Status.Health);
            Step(actor);
        }
        private static ClockHandView CreateClockHandView(EnemyActor actor)
        {
            var go = new GameObject("ClockHands"); go.SetActive(false);
            var view = go.AddComponent<ClockHandView>(); view.Actor = actor;
            view.Hands = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                view.Hands[i] = new GameObject("Hand" + i).transform;
                view.Hands[i].SetParent(go.transform);
            }
            go.SetActive(true);
            view.SendMessage("LateUpdate");
            return view;
        }
        private static void AddClockHandCases()
        {
            AddClockHandCase(a =>
            {
                var brain = (ClockBossBrain)a.Brain;
                var view = CreateClockHandView(a);
                Check(view.Hands[2].gameObject.activeSelf && !view.Hands[1].gameObject.activeSelf && !view.Hands[0].gameObject.activeSelf,
                    "一阶段只显示真正的时针资源");
                Step(a, .3f); float hour = brain.HandProgress(0);
                ClockPhaseTwo(a); view.SendMessage("LateUpdate");
                Check(a.Brain.Phase == 2 && view.Hands[2].gameObject.activeSelf && view.Hands[1].gameObject.activeSelf && !view.Hands[0].gameObject.activeSelf,
                    "二阶段保留时针并加入分针");
                Check(brain.HandProgress(0) > hour && brain.HandProgress(1) < .05f && brain.HandProgress(2) == 0f,
                    "解锁分针从自身周期起算，不重置时针或预跑秒针");
                ClockOpenCore(a);
                float h = brain.HandProgress(0), m = brain.HandProgress(1);
                a.WeakPoint.ApplyDamage(new DamageInfo { Amount = a.Definition.ClockCoreHealth });
                Step(a); Step(a, .61f); view.SendMessage("LateUpdate");
                Check(a.Brain.Phase == 3 && view.Hands[0].gameObject.activeSelf && view.Hands[1].gameObject.activeSelf && view.Hands[2].gameObject.activeSelf,
                    "击破发条进入三阶段才加入秒针");
                Check(brain.HandProgress(0) == h && brain.HandProgress(1) == m && brain.HandProgress(2) == 0f,
                    "阶段过渡冻结旧指针，秒针不继承全局时间");
            });
            AddClockHandCase(a =>
            {
                ClockPhaseTwo(a); Target(a, 6);
                var brain = (ClockBossBrain)a.Brain;
                var widths = new List<float>();
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.BeamSpawn) widths.Add(((EnemyBeam)cue.Subject).Width); };
                Step(a, 1.19f);
                Check(widths.Count == 1 && Mathf.Approximately(widths[0], .18f), "分针先按自己的周期发射");
                float hour = brain.HandProgress(0);
                Step(a, .8f);
                Check(widths.Count == 2 && Mathf.Approximately(widths[1], .1f), "时针在分针预警期间仍可独立到期出手");
                Check(hour > .5f && brain.HandProgress(1) > .6f, "单针出手不清空另一根的剩余进度");
                var view = CreateClockHandView(a);
                Quaternion rotation = view.Hands[1].localRotation;
                object saved = a.CaptureSnapshotState();
                float h = brain.HandProgress(0), m = brain.HandProgress(1);
                a.Status.SetPaused(true); Step(a, 5); a.Status.SetPaused(false);
                Check(brain.HandProgress(0) == h && brain.HandProgress(1) == m, "暂停冻结每根针的独立计时");
                Step(a, .2f); a.RestoreSnapshotState(saved); view.SendMessage("LateUpdate");
                Check(brain.HandProgress(0) == h && brain.HandProgress(1) == m && Quaternion.Angle(rotation, view.Hands[1].localRotation) < .01f,
                    "快照恢复各针计时与对应显示角度");
            });
            AddClockHandCase(a =>
            {
                ClockPhaseTwo(a); ClockOpenCore(a);
                a.WeakPoint.ApplyDamage(new DamageInfo { Amount = a.Definition.ClockCoreHealth });
                Step(a); Step(a, .61f); Target(a, 6);
                var brain = (ClockBossBrain)a.Brain;
                int beams = 0, hours = 0;
                a.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.BeamSpawn) return;
                    beams++;
                    if (Mathf.Approximately(((EnemyBeam)cue.Subject).Width, .1f)) hours++;
                };
                Step(a, .61f);
                Check(beams == 2 && hours == 0, "三阶段秒针按快周期触发双线封路");
                Step(a, .6f);
                Check(beams == 5 && brain.HandProgress(0) > .6f, "秒针二次触发时分针独立加入，时针仍继续计时");
                Step(a, .8f);
                Check(hours == 1 && beams == 8, "三根针均能按自身周期攻击，不被最快针抢占");
                int before = beams; Step(a, 50f);
                Check(beams - before == 4, "长帧每根针最多触发一次，不补发积压连射");
            });
            AddClockHandCase(a =>
            {
                Target(a, 2); Step(a, 2.4f); Step(a, 1.06f);
                var brain = (ClockBossBrain)a.Brain;
                Check(a.Brain.StateName == "JumpShake", "激光收尾后身体仍可响铃");
                float before = brain.HandProgress(0); Step(a, .3f);
                Check(brain.HandProgress(0) > before, "响铃身体动作期间独立指针继续计时");
                Step(a, 1.8f);
                Check(a.Brain.StateName == "Charge" && brain.HandProgress(0) == 1f, "身体忙时到期指针保留一次待发，不覆盖冲撞");
            });
            AddClockHandCase(a =>
            {
                a.Definition.ClockBeamVolleysBeforeRest = 1;
                Target(a, 6, 4); Step(a, 2.01f); Step(a, 1.06f);
                var brain = (ClockBossBrain)a.Brain;
                Check(a.Brain.StateName == "Rest", "独立指针攻击仍保留完整休息窗口");
                float progress = brain.HandProgress(0);
                Step(a, .2f);
                Check(brain.HandProgress(0) == progress, "休息统一冻结指针，不在后台积压攻击");
                Step(a, .31f); Step(a, .1f);
                Check(a.Brain.StateName != "Rest" && brain.HandProgress(0) > progress, "休息后继续各自剩余进度");
            });
            AddClockHandCase(a =>
            {
                ClockPhaseTwo(a); Step(a, .3f);
                var brain = (ClockBossBrain)a.Brain;
                float h = brain.HandProgress(0), m = brain.HandProgress(1);
                ClockOpenCore(a); Step(a, a.Definition.ClockCoreWindow + .01f); Step(a, 1f);
                Check(a.Brain.Phase == 2 && a.Status.Health == 600f && brain.HandProgress(2) == 0f,
                    "发条窗口失败仍回到二阶段，不提前解锁秒针");
                Check(brain.HandProgress(0) == h && brain.HandProgress(1) == m, "失败恢复生命不重置已有指针周期");
            });
        }

        private sealed class TimeRevertCatalog : IRuleCatalog
        {
            private readonly RuleDef _definition = AssetDatabase.LoadAssetAtPath<RuleDef>("Assets/_Project/Data/Rules/TimeRevert.asset");
            public RuleDef Get(AttributeId attribute, RelationId relation)
                => attribute == AttributeId.Time && relation == RelationId.Revert ? _definition : null;
        }
        private static void AddClockRewindCases()
        {
            foreach (bool available in new[] { true, false })
                Add(EnemyKind.ClockBoss, a =>
                {
                    a.Definition.RuleAttribute = AttributeId.Time;
                    a.Definition.RuleRelation = RelationId.Revert;
                    var snapshots = SnapshotsFor(a);
                    SnapshotSystem.SetInstanceForTesting(snapshots);
                    var body = a.GetComponent<Rigidbody2D>();
                    a.GetComponent<BoxCollider2D>().size = a.Definition.BodySize;
                    Vector2 oldPosition = new Vector2(-6f, 2f);
                    body.position = oldPosition;
                    a.transform.position = oldPosition;
                    if (available)
                    {
                        snapshots.CaptureToBuffer();
                        body.position = new Vector2(-2f, 2f);
                        a.transform.position = body.position;
                        snapshots.CaptureToBuffer();
                    }
                    Vector2 currentPosition = new Vector2(4f, 2f);
                    body.position = currentPosition;
                    a.transform.position = currentPosition;
                    Target(a, 2f);
                    Step(a, 2.4f);
                    Check(a.Brain.StateName == "JumpShake", "时间反转配置下仍先响铃预警");
                    float energy = a.Status.Energy;
                    Step(a, a.AnimationDuration + .01f);
                    Check(a.Brain.StateName == "Charge" && body.position == currentPosition,
                        "有无历史快照都从响铃结束的当前位置起跑");
                    Check(a.Status.Energy == energy, "乱跑不隐式施法或扣除规则能量");
                    Step(a);
                    Check(a.Motor.Intent.x > 0f && a.Facing > 0f, "冲撞使用本次预警锁定的方向");
                    var floor = new GameObject("ClockChargeFloor"); floor.layer = 8;
                    floor.transform.position = new Vector3(0f, currentPosition.y - a.Definition.BodySize.y * .5f - .5f, 0f);
                    floor.AddComponent<BoxCollider2D>().size = new Vector2(60f, 1f);
                    a.PlayerMask = 0;
                    Physics2D.SyncTransforms();
                    SimulationMode2D previousMode = Physics2D.simulationMode;
                    try
                    {
                        Physics2D.simulationMode = SimulationMode2D.Script;
                        for (int i = 0; i < 15; i++)
                        {
                            Vector2 previous = body.position;
                            Step(a); a.Motor.Tick(.02f); Physics2D.Simulate(.02f);
                            if (Vector2.Distance(body.position, previous) > .3f)
                                throw new InvalidOperationException("冲撞位移不连续：" + previous + " -> " + body.position);
                        }
                        Check(body.position.x > currentPosition.x && a.Brain.StateName == "Charge",
                            "真实物理冲撞逐步向前移动，没有跳到历史坐标");
                    }
                    finally { Physics2D.simulationMode = previousMode; }
                    Check(snapshots.TryPeekSecondLast(out _) == available, "冲撞不会消耗或清空时间回溯缓冲");
                    Vector2 beforeCast = body.position;
                    bool cast = a.CastRule();
                    Check(available ? cast && body.position == oldPosition && a.Status.Energy < energy
                        : !cast && body.position == beforeCast && a.Status.Energy == energy,
                        "显式释放时间反转仍遵守原有回溯与扣费语义");
                });
        }

        private sealed class SnapshotBody : IRuleAffected
        {
            public Rigidbody2D Body { get; }
            public Collider2D[] Colliders { get; }
            public SnapshotBody(EnemyActor actor)
            {
                Body = actor.GetComponent<Rigidbody2D>();
                Colliders = actor.GetComponents<Collider2D>();
            }
        }
        private static SnapshotSystem SnapshotsFor(EnemyActor actor)
        {
            var registry = new WorldRegistry();
            registry.Register(new SnapshotBody(actor));
            return new SnapshotSystem(RuleSystem.Instance, registry);
        }
        private static void AddSnapshotCameraCases()
        {
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BatBoss })
            {
                Add(kind, a =>
                {
                    var snapshots = SnapshotsFor(a);
                    var alive = snapshots.Capture();
                    string initial = a.Brain.StateName;
                    float health = kind == EnemyKind.RabbitBoss ? 40f : kind == EnemyKind.ClockBoss ? 330f : 650f;
                    a.Status.ApplyHealthDelta(health - a.Status.Health);
                    Step(a);
                    string transition = kind == EnemyKind.BrickBoss ? "Split" : kind == EnemyKind.ClockBoss ? "Stun"
                        : kind == EnemyKind.RabbitBoss ? "Flip" : "Weak";
                    Check(a.Brain.StateName == transition, kind + " 进入阶段过渡");
                    float elapsed = a.Brain.ActionElapsed;
                    var saved = snapshots.Capture();
                    snapshots.CaptureToBuffer();
                    Check(saved.Entities.Count == 1, kind + " 阶段过渡中可采集实体和时间缓冲");
                    Step(a, .1f);
                    snapshots.Restore(saved);
                    Check(a.Brain.StateName == transition && Mathf.Approximately(a.Brain.ActionElapsed, elapsed),
                        kind + " 恢复阶段过渡及计时");
                    Step(a, .02f);
                    snapshots.Restore(alive);
                    Check(a.Brain.StateName == initial && a.Status.Health == a.Definition.Health,
                        kind + " 过渡后可回溯 Alive 动作与生命");
                });
                Add(kind, a =>
                {
                    var snapshots = SnapshotsFor(a);
                    a.Status.ApplyHealthDelta(-a.Status.Health);
                    Step(a);
                    Check(a.Brain.StateName == "Dead", kind + " 进入死亡");
                    var saved = snapshots.Capture();
                    snapshots.CaptureToBuffer();
                    Check(saved.Entities.Count == 1, kind + " 死亡中可采集实体和时间缓冲");
                    Step(a, 2f);
                    snapshots.Restore(saved);
                    Check(a.gameObject.activeSelf && a.Defeated && a.Brain.StateName == "Dead",
                        kind + " 回溯死亡动作且不重开 Alive");
                });
            }
            Add(EnemyKind.BrickMinion, _ => CheckCameraRelease(false));
            Add(EnemyKind.BrickMinion, _ => CheckCameraRelease(true));
        }
        private static void CheckCameraRelease(bool disable)
        {
            var go = new GameObject("CameraImpulseValidation");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                go.transform.position = new Vector3(0f, 0f, -10f);
                var impulse = go.AddComponent<BossCameraImpulse>();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var before = typeof(BossCameraImpulse).GetMethod("BeforeRender", flags);
                var after = typeof(BossCameraImpulse).GetMethod("AfterRender", flags);
                for (int i = 0; i < 2; i++)
                {
                    Vector3 position = go.transform.position;
                    BossCameraImpulse.Pulse(.3f, .2f);
                    before.Invoke(impulse, new object[] { default(ScriptableRenderContext), camera });
                    Check(Vector3.Distance(camera.worldToCameraMatrix.MultiplyPoint3x4(position), Vector3.zero) > .01f,
                        "震屏在渲染阶段生效");
                    if (disable) impulse.enabled = false;
                    else after.Invoke(impulse, new object[] { default(ScriptableRenderContext), camera });
                    go.transform.position += new Vector3(3f, 2f, 0f);
                    Check(camera.worldToCameraMatrix.MultiplyPoint3x4(go.transform.position).sqrMagnitude < .00001f,
                        disable ? "停用震屏后视图继续跟随摄像机位置" : "震屏结束后视图继续跟随摄像机位置");
                    impulse.enabled = true;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void AddTacticsCases()
        {
            Add(EnemyKind.BrickBoss, a =>
            {
                Target(a, 5); Step(a); Step(a);
                Check(a.Brain.StateName == "Advance" && a.Motor.Intent.x > 0 && a.Motor.SpeedMultiplier > 1f, "积木主动快步压近");
                Target(a, -6); Step(a);
                Check(a.Brain.StateName == "HammerGround", "玩家绕后时积木停步重新预警地面重击");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                EnemyProjectile shot = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                Target(a, 8); Step(a); Target(a, -8); Step(a, a.AnimationDuration * .65f);
                Check(shot != null && shot.Velocity.x > 0f, "积木投掷锁定起手时目标，玩家换边可以躲避");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Status.ApplyHealthDelta(-260); Step(a); Step(a, 1.1f);
                Target(a, 2); Step(a); Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "HammerGround", "积木分裂阶段解锁锤人接地面重击");
                Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "Rest", "积木有限连招结束后进入休息");
            });
            Add(EnemyKind.ClockBoss, a =>
            {
                EnemyBeam beam = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.BeamSpawn) beam = (EnemyBeam)cue.Subject; };
                Target(a, 6); Step(a, a.Definition.ClockHourInterval + .01f);
                Check(beam != null && beam.Direction.x > .9f && beam.WarningDuration >= .7f, "发条瞄准激光保留预警");
                Vector2 direction = beam.Direction; Target(a, -6); Step(a, .1f);
                Check(beam.Direction == direction, "激光预警后不追着玩家转向");
            });
            Add(EnemyKind.ClockBoss, a =>
            {
                Target(a, 2); Step(a, 2.4f); Target(a, -6);
                Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Charge" && a.Motor.Intent.x > 0 && a.Facing > 0, "发条冲撞保持响铃时锁定方向");
                a.transform.position = new Vector3(11.9f, 0, 0); Step(a);
                Check(a.Brain.StateName == "Rest", "发条到边界停止冲撞，不来回折返追杀");
            });
            Add(EnemyKind.ClockBoss, a =>
            {
                int beams = 0;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.BeamSpawn) beams++; };
                Target(a, 6); Step(a, a.Definition.ClockHourInterval + .01f); Step(a, a.Definition.ClockHourInterval + .01f);
                Step(a, a.Definition.ClockBeamWarning + .36f);
                Check(beams == 2 && a.Brain.StateName == "JumpShake", "两轮激光后抓机会进入响铃冲撞");
                Step(a, .2f);
                Check(beams == 2, "响铃期间不再叠加新激光");
                Step(a, .7f); Step(a, a.Definition.ClockChargeDuration + .01f);
                Check(a.Brain.StateName == "Rest", "发条冲撞后保留喘息窗口");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                Target(a, 2); Step(a, .21f); Step(a); Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Burrow" && a.Motor.Intent.x < 0 && a.Motor.SpeedMultiplier > 1, "兔子被逼近时藏身撤退");
                var visual = new GameObject("BurrowVisual"); visual.SetActive(false);
                var renderer = visual.AddComponent<SpriteRenderer>();
                var animator = visual.AddComponent<RuleGame.Presentation.Enemy.EnemySpriteAnimator>(); animator.Actor = a;
                visual.SetActive(true); animator.SendMessage("LateUpdate");
                Check(renderer.color.a < .5f, "白盒也用半透明显示藏身状态");
                object saved = a.CaptureSnapshotState(); Target(a, -8); Step(a);
                Check(a.Motor.Intent.x < 0, "藏身路线不随玩家换边反复抖动");
                a.RestoreSnapshotState(saved); Step(a, a.Definition.RabbitBurrowDuration + .01f); animator.SendMessage("LateUpdate");
                Check(a.Brain.StateName == "Pop" && renderer.color.a == 1f, "藏身结束重新现身，外观恢复");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                a.transform.position = new Vector3(-10, 0, 0); Target(a, 2);
                Step(a, .21f); Step(a); Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "HideThrow", "兔子被堵在墙角时改用高抛反击");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                float first = -1f, second = -1f;
                a.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.ProjectileSpawn) return;
                    if (first < 0f) first = ((EnemyProjectile)cue.Subject).Gravity;
                    else second = ((EnemyProjectile)cue.Subject).Gravity;
                };
                Target(a, 6); Step(a, .21f); Step(a, .75f); Step(a, 1.3f);
                Step(a, a.Definition.BossRestDuration + .01f); Step(a, .65f); Step(a, .75f);
                Check(first > 0 && second > first, "兔子休息后切换快弹与高抛，避免重复同一招");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                int shots = 0;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shots++; };
                Target(a, 6); a.Status.ApplyHealthDelta(-950); Step(a); Step(a, .81f);
                Step(a, .36f); Step(a, .36f); Step(a, .4f);
                Check(shots == 6 && a.Brain.StateName == "Rest", "兔子翻转后两轮三发散射，结束后休息");
            });
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss })
                Add(kind, a =>
                {
                    a.Definition.BossRestDuration = 1.8f; Target(a, 6); AdvanceTo(a, "Rest");
                    Step(a, .4f);
                    Check(a.Brain.StateName == "Rest" && a.Motor.Intent == Vector2.zero, kind + " 新战术保留完整休息");
                    object saved = a.CaptureSnapshotState(); float remaining = a.Brain.RestRemaining;
                    a.Status.SetPaused(true); Step(a, 10); a.Status.SetPaused(false); Step(a, .2f); a.RestoreSnapshotState(saved);
                    Check(Mathf.Approximately(a.Brain.RestRemaining, remaining), kind + " 新战术兼容暂停与快照");
                });
        }

        private static void Add(EnemyKind kind, Action<EnemyActor> test, Action<EnemyDefinition> configure = null)
        {
            var config = ScriptableObject.CreateInstance<EnemyDefinition>();
            config.Kind = kind; config.Health = 1000; config.HurtCooldown = 4;
            config.BossRestDuration = .5f;
            config.Flying = kind == EnemyKind.BatBoss;
            config.Timings = new[]
            {
                new EnemyActionTiming { Action = "Hurt", Duration = 2 },
                new EnemyActionTiming { Action = "HammerPlayer", Duration = 1 },
                new EnemyActionTiming { Action = "Pop", Duration = 0.2f },
                new EnemyActionTiming { Action = "Split", Duration = 1 }
            };
            configure?.Invoke(config);
            var go = new GameObject("BossValidation_" + Actors.Count); go.layer = 9; go.SetActive(false);
            var actor = go.AddComponent<EnemyActor>(); actor.Definition = config;
            actor.Target = new GameObject("Target_" + Actors.Count).transform;
            Target(actor, 5); actor.GetComponent<RuleGame.Gameplay.Player.EntityStatus>().SetPaused(true);
            go.SetActive(true); Actors.Add(actor); Cases.Add(test);
        }

        private static void AwaitReady()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            foreach (EnemyActor actor in Actors)
                if (actor.Brain == null)
                {
                    if (EditorApplication.timeSinceStartup > _deadline) Finish("等待组件 Start 超时");
                    return;
                }
            EditorApplication.update -= AwaitReady;
            for (int i = 0; i < Actors.Count; i++)
            {
                EnemyActor actor = Actors[i]; actor.enabled = false; actor.Status.SetPaused(false);
                try { Cases[i](actor); }
                catch (Exception error) { _failed++; Debug.LogError("Case " + i + ": " + error); }
            }
            Finish(null);
        }

        private static void Cooldown(EnemyActor actor)
        {
            Target(actor, 2); Step(actor); Step(actor, actor.AnimationDuration + 0.01f);
            Step(actor, actor.Definition.BossRestDuration + .01f);
        }
        private static void Target(EnemyActor actor, float x, float y = 0)
            => actor.Target.position = actor.transform.position + new Vector3(x, y, 0);
        private static void Step(EnemyActor actor, float dt = 0.02f) => actor.Brain.Tick(dt);
        private static void AdvanceTo(EnemyActor actor, string state)
        {
            for (int i = 0; i < 2000 && actor.Brain.StateName != state; i++) Step(actor);
            Check(actor.Brain.StateName == state, "能够进入 " + state);
        }
        private static void Check(bool passed, string name)
        {
            if (!passed) throw new InvalidOperationException(name);
            _passed++; Debug.Log("PASS: " + name);
        }
        private static void Finish(string error)
        {
            SessionState.SetBool(Pending, false);
            SessionState.SetBool(Tactics, false);
            SessionState.SetBool(SnapshotCamera, false);
            SessionState.SetBool(ClockRewind, false);
            SessionState.SetBool(ClockHands, false);
            SessionState.SetBool(RabbitTactics, false);
            if (error != null) { _failed++; Debug.LogError(error); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output)));
            File.WriteAllText(Output, $"Passed: {_passed}\nFailed: {_failed}\n{error}\n");
            EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
    }
}
