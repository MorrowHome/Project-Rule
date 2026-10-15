using System;
using System.Collections.Generic;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>在可丢弃的工程副本中进入 Play Mode，验证真实组件与 HFSM，不保存生产场景。</summary>
    public static class BossCombatValidation
    {
        private const string Pending = "RuleGame.Enemy.CombatValidation";
        private static readonly List<EnemyActor> Actors = new List<EnemyActor>();
        private static readonly List<Action<EnemyActor>> Cases = new List<Action<EnemyActor>>();
        private static int _passed, _failed;
        private static double _deadline;
        private static string Output => Environment.GetEnvironmentVariable("RULE_BOSS_OUTPUT")
            ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/BossCombatValidation.txt"));

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
            RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
            _deadline = EditorApplication.timeSinceStartup + 30;
            Add(EnemyKind.BrickBoss, a => { Target(a, 2); Step(a); Check(a.Brain.StateName == "HammerPlayer", "积木近距锤击"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 5); Step(a); Check(a.Brain.StateName == "HammerGround", "积木中距冲击波"); });
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
            Add(EnemyKind.BatBoss, a => { Target(a, 2); Step(a); Check(a.Brain.StateName == "Evade" && a.Motor.SpeedMultiplier > 1, "蝙蝠近距主动脱离"); });
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
            Add(EnemyKind.ClockBoss, a => { Target(a, 2); Step(a); Check(a.Brain.StateName == "JumpShake", "发条贴近时响铃反击"); });
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
                Check(a.Motor.Intent.x < 0, "兔子投掷恢复时拉开距离");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                Target(a, 11); Step(a, 0.3f); Step(a);
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
            EditorApplication.update += AwaitReady;
        }

        private static void Add(EnemyKind kind, Action<EnemyActor> test)
        {
            var config = ScriptableObject.CreateInstance<EnemyDefinition>();
            config.Kind = kind; config.Health = 1000; config.HurtCooldown = 4;
            config.Flying = kind == EnemyKind.BatBoss;
            config.Timings = new[]
            {
                new EnemyActionTiming { Action = "Hurt", Duration = 2 },
                new EnemyActionTiming { Action = "HammerPlayer", Duration = 1 },
                new EnemyActionTiming { Action = "Pop", Duration = 0.2f },
                new EnemyActionTiming { Action = "Split", Duration = 1 }
            };
            var go = new GameObject("BossValidation_" + Actors.Count); go.SetActive(false);
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
        }
        private static void Target(EnemyActor actor, float x, float y = 0)
            => actor.Target.position = actor.transform.position + new Vector3(x, y, 0);
        private static void Step(EnemyActor actor, float dt = 0.02f) => actor.Brain.Tick(dt);
        private static void AdvanceTo(EnemyActor actor, string state)
        {
            for (int i = 0; i < 600 && actor.Brain.StateName != state; i++) Step(actor);
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
            if (error != null) { _failed++; Debug.LogError(error); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output)));
            File.WriteAllText(Output, $"Passed: {_passed}\nFailed: {_failed}\n{error}\n");
            EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
    }
}
