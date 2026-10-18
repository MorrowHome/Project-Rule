using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.LevelFlow;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 落点（出生点 = 存档点）的摆放与体检工具。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有它</b>：落点靠**编号**区分，而编号是字符串。手摆最容易错的三处都是**静默失败** ——
    /// <b>重名</b>（<c>FindObjectsByType</c> 先找到谁不确定）、<b>世界里有多个 <c>start</c></b>、
    /// <b>点落在分区矩形之外</b>（那一片永远不加载，复活直接掉进虚空）。玩到一半才炸，
    /// 所以用一条只读检查统一兜住。</para>
    /// <para>新建放在**场景视图中心**：先把镜头对准想放的位置，再点菜单 —— 比先建空物体再拖省一步。</para>
    /// </remarks>
    public static class PlayerSpawnPointTool
    {
        private const string CatalogPath = "Assets/_Project/Data/Config/PartitionCatalog.asset";
        private const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";

        // ==================================================================
        //  新建
        // ==================================================================

        [MenuItem("Tools/规则游戏/落点/① 在场景视图中心新建落点（存档点）", false, 60)]
        public static void Create()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) || !scene.isLoaded)
            {
                EditorUtility.DisplayDialog(
                    "新建落点", "当前场景还没有保存到磁盘。先保存场景，再放落点（分区表要按路径认场景）。", "好");
                return;
            }

            Vector3 position = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.pivot
                : Vector3.zero;

            var go = new GameObject("PlayerSpawnPoint");
            go.transform.position = new Vector3(position.x, position.y, 0f);

            var point = go.AddComponent<PlayerSpawnPoint>();
            point.SetId(NextCheckpointId(scene));

            Undo.RegisterCreatedObjectUndo(go, "新建落点");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"[落点] 已在 {scene.name} 新建落点 \"{point.Id}\"，位置 {go.transform.position}。" +
                "\n· 起点/入口用 id = start（全世界只能有一个）。" +
                "\n· 触发半径按门洞/落脚区宽度给，默认 1.2 米；设 0 = 只能靠脚本 Activate。", go);
        }

        /// <summary>同一场景里取一个没人用的 <c>checkpoint-NN</c>。</summary>
        private static string NextCheckpointId(Scene scene)
        {
            var used = new HashSet<string>();
            foreach (var point in AllPoints())
            {
                if (point.gameObject.scene == scene) used.Add(point.Id);
            }

            for (int i = 1; i < 1000; i++)
            {
                string id = "checkpoint-" + i.ToString("00");
                if (!used.Contains(id)) return id;
            }

            return "checkpoint-x";
        }

        // ==================================================================
        //  体检（只读）
        // ==================================================================

        [MenuItem("Tools/规则游戏/落点/② 检查所有落点（重名 / start / 分区范围）", false, 61)]
        public static void Check()
        {
            string report = BuildReport(out int errorCount);

            if (errorCount > 0) Debug.LogError(report);
            else Debug.Log(report);

            EditorUtility.DisplayDialog(
                "落点体检",
                errorCount > 0 ? "发现 " + errorCount + " 个问题，详见 Console。" : "全部通过（详见 Console）。",
                "好");
        }

        /// <summary>体检报告（只读）。自动化 / 测试直接调这个，不弹窗。</summary>
        public static string BuildReport(out int errorCount)
        {
            var points = AllPoints();
            var catalog = AssetDatabase.LoadAssetAtPath<PartitionCatalog>(CatalogPath);
            var errors = new StringBuilder();
            var notes = new StringBuilder();
            errorCount = 0;

            // ① 全世界的编号必须唯一：重名时「开局落在哪」「死回哪」都是不确定的。
            var byId = new Dictionary<string, List<PlayerSpawnPoint>>();
            foreach (var point in points)
            {
                if (!byId.TryGetValue(point.Id, out var list))
                {
                    list = new List<PlayerSpawnPoint>();
                    byId[point.Id] = list;
                }

                list.Add(point);
            }

            foreach (var pair in byId)
            {
                if (pair.Value.Count <= 1) continue;

                string where = "";
                foreach (var point in pair.Value) where += point.gameObject.scene.name + " ";

                errors.Append("· 编号重复 \"").Append(pair.Key).Append("\" ×").Append(pair.Value.Count)
                      .Append("（").Append(where).Append("）\n");
                errorCount++;
            }

            if (byId.TryGetValue(PlayerSpawnPoint.DefaultId, out var starts) && starts.Count > 1)
            {
                errors.Append("· 世界里有多处 id = \"start\"，开局落点是随机的 —— 入口只留一处，其余改成 checkpoint-NN\n");
                errorCount++;
            }

            if (!byId.ContainsKey(PlayerSpawnPoint.DefaultId))
            {
                notes.Append("· 没有 id = \"start\" 的入口落点：开局会落在 LevelRuntime 预制体里玩家原来的位置\n");
            }

            // ② 每个点：半径、场景是否在分区表内、是否落在分区矩形里。
            var catalogScenes = new HashSet<string>();
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Count; i++) catalogScenes.Add(catalog[i].ScenePath);
            }
            else
            {
                notes.Append("· 找不到 ").Append(CatalogPath).Append("，跳过分区检查\n");
            }

            foreach (var point in points)
            {
                string scenePath = point.gameObject.scene.path;
                Vector2 at = point.transform.position;

                notes.Append("  ").Append(point.Id).Append("  @ ").Append(point.gameObject.scene.name)
                     .Append(' ').Append(at.ToString("F1")).Append("  r=").Append(point.ActivateRadius.ToString("0.##"))
                     .Append('\n');

                if (point.ActivateRadius <= 0f)
                {
                    notes.Append("    ⚠️ 半径 0：只能靠开局 / 脚本 Activate，走过不会记录\n");
                }

                if (catalog == null) continue;
                if (scenePath == BootScenePath) continue;   // 入口常驻，不在分区表里

                if (!catalogScenes.Contains(scenePath))
                {
                    errors.Append("· ").Append(point.Id).Append(" 所在的场景 ").Append(scenePath)
                          .Append(" 没登记进分区表，运行期不会加载\n");
                    errorCount++;
                    continue;
                }

                int index = catalog.IndexOfScene(scenePath);
                PartitionCatalog.PartitionEntry entry = catalog[index];
                if (!PartitionCatalog.WithinRange(entry, at, 0f))
                {
                    errors.Append("· ").Append(point.Id).Append(" 落在分区 ").Append(entry.PartitionId)
                          .Append(" 的矩形之外 ").Append(at.ToString("F1"))
                          .Append("（矩形中心 ").Append(entry.Center.ToString("F1"))
                          .Append(" 尺寸 ").Append(entry.Size.ToString("F1")).Append("）\n");
                    errorCount++;
                }
            }

            return "[落点体检] " + points.Count + " 个落点，" + errorCount + " 个必须修的问题\n"
                 + (errorCount > 0 ? errors.ToString() : "")
                 + notes;
        }

        private static List<PlayerSpawnPoint> AllPoints()
        {
            var list = new List<PlayerSpawnPoint>(
                Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Include));
            return list;
        }
    }
}
