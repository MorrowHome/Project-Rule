using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Enemy;

namespace RuleGame.Editor.Enemy
{
    /// <summary>把正式关卡中净空足够的积木士兵换成石头人；按地面重新落脚，保留活动范围和巡逻半径。</summary>
    public static class StoneGuardPlacementReplace
    {
        private const string LevelPath = "Assets/_Project/Scenes/BuildingBlocks/Level_Building_01.unity";
        private const string StonePrefabPath = "Assets/_Project/Prefabs/Enemies/FieldMinions/StoneGuard.prefab";

        [MenuItem("RuleGame/Enemies/把 Level_Building_01 的积木士兵换成石头人", false, 62)]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");

            var scene = SceneManager.GetSceneByPath(LevelPath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(LevelPath, OpenSceneMode.Additive);
                opened = true;
            }

            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StonePrefabPath);
                if (prefab == null) throw new InvalidOperationException("缺少 " + StonePrefabPath);

                var oldActors = UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsInactive.Exclude)
                    .Where(a => a.gameObject.scene == scene && a.Definition != null && a.Definition.Kind == EnemyKind.BrickSoldier)
                    .ToArray();

                var stoneBody = prefab.GetComponent<BoxCollider2D>();
                float height = stoneBody != null ? stoneBody.size.y : 2.4f;
                int replaced = 0, skipped = 0;
                foreach (var old in oldActors)
                {
                    float ground = GroundY(old.transform.position);
                    float ceiling = CeilingY(old.transform.position);
                    if (float.IsNaN(ground) || ceiling - ground < height + .05f)
                    {
                        skipped++;
                        Debug.LogWarning($"[StoneGuardPlacement] 跳过 {old.name}：净空 {ceiling - ground:F2} m < 石头人 {height:F2} m。");
                        continue;
                    }
                    Replace(old, prefab, scene, ground, height);
                    replaced++;
                }

                if (replaced > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                Debug.Log($"[StoneGuardPlacement] 替换 {replaced} 个，跳过 {skipped} 个。");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Replace(EnemyActor old, GameObject prefab, Scene scene, float ground, float height)
        {
            var oldGo = old.gameObject;
            var oldTransform = oldGo.transform;
            Transform parent = oldTransform.parent;
            int sibling = oldTransform.GetSiblingIndex();
            string name = oldGo.name.Replace("BrickSoldier", "StoneGuard");

            var newGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            newGo.transform.SetParent(parent, false);
            newGo.transform.position = new Vector3(oldTransform.position.x, ground + height * .5f, oldTransform.position.z);
            newGo.transform.rotation = oldTransform.rotation;
            newGo.transform.localScale = oldTransform.localScale;
            newGo.name = name;
            newGo.transform.SetSiblingIndex(sibling);

            var actor = newGo.GetComponent<EnemyActor>();
            if (actor == null) throw new InvalidOperationException("石头人预制体缺少 EnemyActor");

            actor.Target = old.Target;
            actor.Combat = old.Combat;
            actor.UseSpawnRelativeArena = old.UseSpawnRelativeArena;
            actor.ArenaSize = old.ArenaSize;
            actor.ArenaOffset = old.ArenaOffset;
            actor.MinionPrefab = old.MinionPrefab;
            actor.FlyingMinionPrefab = old.FlyingMinionPrefab;
            actor.Summoner = old.Summoner;
            actor.WeakPoint = old.WeakPoint;
            actor.PlayerMask = old.PlayerMask;
            actor.ProjectileSprite = old.ProjectileSprite;
            actor.BeamOrigin = old.BeamOrigin;
            actor.RuntimeSpawned = old.RuntimeSpawned;
            actor.ArenaX = old.ArenaX;
            actor.ArenaY = old.ArenaY;
            actor.PatrolRange = old.PatrolRange;
            actor.PressContactSwitches = old.PressContactSwitches;

            Undo.RegisterCreatedObjectUndo(newGo, "替换为石头人");
            Undo.DestroyObjectImmediate(oldGo);
        }

        private static float GroundY(Vector3 position)
        {
            var hit = Physics2D.Raycast(position, Vector2.down, 12f, 1 << 8);
            return hit.collider != null ? hit.point.y : float.NaN;
        }

        private static float CeilingY(Vector3 position)
        {
            var hit = Physics2D.Raycast(position, Vector2.up, 12f, 1 << 8);
            return hit.collider != null ? hit.point.y : float.PositiveInfinity;
        }
    }
}
