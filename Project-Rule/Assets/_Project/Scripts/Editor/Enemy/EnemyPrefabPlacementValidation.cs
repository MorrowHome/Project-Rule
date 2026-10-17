using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Enemy
{
    /// <summary>仅在隔离工程验证拖放接入；不进入开发者正在使用的 Play Mode。</summary>
    public static class EnemyPrefabPlacementValidation
    {
        private const string Pending = "RuleGame.EnemyPrefabPlacementValidation";
        private static int _passed;

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅用于隔离工程。");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                FieldMinionSetup.RepairPlacement();
                var saved = new Dictionary<string, string>();
                foreach (var kind in FieldMinionSetup.Kinds)
                {
                    string path = FieldMinionSetup.PrefabFolder + "/" + kind + ".prefab";
                    var actor = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EnemyActor>();
                    var boxes = actor.GetComponentsInChildren<Hurtbox>(true);
                    var body = actor.GetComponent<BoxCollider2D>();
                    Check(actor.UseSpawnRelativeArena && boxes.Length == 1 && boxes[0].GetComponentInParent<IDamageable>(true) == actor
                        && boxes[0].gameObject.layer == Hurtbox.Layer && boxes[0].GetComponent<BoxCollider2D>().isTrigger
                        && boxes[0].GetComponent<BoxCollider2D>().size == body.size, kind + " 放置模式和独立受击箱正确");
                    saved[path] = File.ReadAllText(path);
                }
                FieldMinionSetup.RepairPlacement();
                foreach (var pair in saved)
                    if (File.ReadAllText(pair.Key) != pair.Value) throw new InvalidOperationException("重复补齐改变了预制体：" + pair.Key);
                Check(true, "重复补齐不增加受击箱、不改变预制体");
                RefreshDemo();
                SessionState.SetInt(Pending + ".Passed", _passed);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Fail(error); }
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                try { Run(); }
                catch (Exception error) { Fail(error); }
            };
        }

        private static void Run()
        {
            _passed = SessionState.GetInt(Pending + ".Passed", 0);
            TimeManager.Initialize(); WorldRegistry.Initialize();
            RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
            Physics2D.simulationMode = SimulationMode2D.Script;
            var floor = new GameObject("TranslatedGround"); floor.layer = 8;
            floor.transform.position = new Vector2(100f, 49.5f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(30f, 1f);
            var actor = Spawn(new Vector2(100f, 50.85f));
            Check(actor.Target == null && actor.Combat != null, "玩家尚未出现时，敌人安全等待并自动取得战斗服务");
            var another = Spawn(new Vector2(120f, 50.85f));
            Check(another.Combat == actor.Combat && UnityEngine.Object.FindObjectsByType<CombatSystem>(FindObjectsSortMode.None).Length == 1,
                "多只拖入的小怪共享同一个场景战斗服务");
            Check(actor.ArenaX == new Vector2(90f, 110f) && actor.ArenaY == new Vector2(40.85f, 60.85f),
                "远离世界原点时，以实际出生点生成活动边界");
            var player = Player(new Vector2(106f, 50.65f));
            actor.SendMessage("ResolveSceneBindings");
            Check(actor.Target == player.transform, "后出现的玩家可自动绑定，无需 Target 接线");
            player.SetActive(false);
            var replacement = Player(new Vector2(106f, 50.65f));
            actor.SendMessage("ResolveSceneBindings");
            Check(actor.Target == replacement.transform, "旧玩家停用后自动绑定新的玩家实例");
            float hp = actor.Status.Health;
            Physics2D.SyncTransforms();
            actor.Combat.PerformAttack(replacement.GetComponent<EntityDamageable>(), actor.transform.position, Vector2.one * 2.5f, 1f, 0f);
            Check(actor.Status.Health == hp - 1f, "实际预制体可通过玩家攻击和 Hurtbox 正常扣血");
            actor.Motor.Tick(.02f); Physics2D.Simulate(.02f); Physics2D.SyncTransforms();
            Vector2 home = actor.transform.position;
            for (int i = 0; i < 220; i++)
            {
                actor.Motor.AllowLedgeTraversal = false;
                actor.Navigation.MoveTo(new Vector2(103f, 50.85f), home, i * .02f);
                actor.Motor.Tick(.02f); Physics2D.Simulate(.02f); Physics2D.SyncTransforms();
            }
            Check(actor.transform.position.x > 102.5f && actor.transform.position.y > 50.7f,
                "出生点平移后仍能实际寻路移动，不受旧世界坐标边界限制");
            Check(actor.ArenaAnchor == new Vector2(100f, 50.85f) && actor.ArenaX.x == 90f,
                "活动范围固定在出生点，不随追击移动");
            var explicitTarget = new GameObject("ExplicitTarget").transform;
            var legacy = Spawn(new Vector2(30f, 2f), explicitTarget, actor.Combat);
            Check(legacy.Target == explicitTarget && legacy.Combat == actor.Combat
                && legacy.ArenaX == new Vector2(20f, 40f) && legacy.ArenaY == new Vector2(-2f, 8f),
                "保留旧场景显式引用和手动世界坐标范围");
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/EnemyPrefabPlacementValidation.txt", $"Passed: {_passed}\nFailed: 0\n");
            Debug.Log($"EnemyPrefabPlacementValidation: {_passed} passed, 0 failed");
            EditorApplication.Exit(0);
        }

        private static EnemyActor Spawn(Vector2 position, Transform target = null, CombatSystem combat = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FieldMinionSetup.PrefabFolder + "/BrickSoldier.prefab");
            var go = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            var actor = go.GetComponent<EnemyActor>(); actor.enabled = false;
            if (target != null)
            {
                actor.Target = target; actor.Combat = combat; actor.UseSpawnRelativeArena = false;
                actor.ArenaX = new Vector2(20f, 40f); actor.ArenaY = new Vector2(-2f, 8f);
            }
            actor.SendMessage("Start");
            return actor;
        }

        private static GameObject Player(Vector2 position)
        {
            var go = new GameObject("AutoBoundPlayer"); go.SetActive(false); go.transform.position = position;
            go.AddComponent<BoxCollider2D>(); go.AddComponent<EntityStatus>();
            go.AddComponent<PlayerController>().enabled = false;
            var receiver = go.AddComponent<EntityDamageable>();
            var serialized = new SerializedObject(receiver);
            serialized.FindProperty("_faction").intValue = (int)Faction.Player;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(true); return go;
        }

        private static void RefreshDemo()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(EnemyNavigationLabSetup.ScenePath) == null) return;
            var scene = EditorSceneManager.OpenScene(EnemyNavigationLabSetup.ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!root.TryGetComponent<EnemyActor>(out var actor)) continue;
                    foreach (var box in actor.GetComponentsInChildren<Hurtbox>(true))
                        if (PrefabUtility.IsAddedGameObjectOverride(box.gameObject))
                            PrefabUtility.RevertAddedGameObject(box.gameObject, InteractionMode.AutomatedAction);
                    actor.Target = null; actor.Combat = null;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
                    Check(actor.GetComponentsInChildren<Hurtbox>(true).Length == 1, "寻路白盒复用预制体受击箱，没有重复判定框");
                }
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static void Check(bool passed, string description)
        {
            if (!passed) throw new InvalidOperationException(description);
            _passed++; Debug.Log("PASS: " + description);
        }

        private static void Fail(Exception error)
        {
            SessionState.SetBool(Pending, false);
            Debug.LogException(error);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/EnemyPrefabPlacementValidation.txt", $"Passed: {_passed}\nFailed: 1\n{error}\n");
            EditorApplication.Exit(1);
        }
    }
}
