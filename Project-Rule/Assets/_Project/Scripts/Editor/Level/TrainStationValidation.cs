using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Editor.Rules;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Travel;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 火车站的**静态**自检（不需要 Play）：布景不变量、编号、落点、目录一致。
    /// </summary>
    /// <remarks>
    /// 跑法：菜单 <c>规则游戏/验证/火车站（静态检查）</c>。
    /// 检查的是**当前已打开的场景**，不加载、不保存任何场景。
    /// </remarks>
    public static class TrainStationValidation
    {
        private const string MenuPath = "规则游戏/验证/火车站（静态检查）";

        [MenuItem(MenuPath, false, 200)]
        public static void Run()
        {
            var h = new RuleVerifyHarness();
            TrainStation[] stations = Object.FindObjectsByType<TrainStation>();

            h.Section("站点");
            var ids = new Dictionary<string, TrainStation>();
            foreach (TrainStation station in stations)
            {
                bool duplicate = ids.ContainsKey(station.Id);
                h.Check("编号唯一：" + station.Id, !duplicate, "重复："
                    + (duplicate ? ids[station.Id].name : station.name));
                if (!duplicate) ids.Add(station.Id, station);

                h.Check("是布景（没有刚体）：" + station.name,
                    station.GetComponent<Rigidbody2D>() == null, "车站不该有 Rigidbody2D");

                bool solid = station.GetComponents<Collider2D>().Any(c => !c.isTrigger);
                h.Check("是布景（没有实心碰撞）：" + station.name, !solid,
                    "车站只该有 Trigger，实心碰撞会变成平台");

                bool hasTrigger = station.GetComponents<Collider2D>().Any(c => c.isTrigger);
                h.Check("能被攻击盒打到（有 Trigger）：" + station.name, hasTrigger, "没有 Trigger 就永远叫不出面板");

                PlayerSpawnPoint spawn = station.GetComponentInChildren<PlayerSpawnPoint>(true);
                h.Check("带复活点：" + station.name, spawn != null, "车站要挂 PlayerSpawnPoint，传送后才会存档");

                h.Check("解锁途径存在：" + station.name,
                    station.UnlockedAtStart || station.UnlockRadius > 0f,
                    "既不开局开放、半径也是 0 —— 只能靠外部 Unlock()，确认开关/剧情已经接上");

                Vector2 ride = station.RidePosition;
                var filter = new ContactFilter2D { useTriggers = false };
                filter.SetLayerMask(GroundMask);
                var hits = new Collider2D[4];
                int inside = Physics2D.OverlapBox(ride, new Vector2(0.6f, 0.6f), 0f, filter, hits);
                h.Check("落点不在实心地形里：" + station.name + " " + ride, inside == 0,
                    "落点被 " + (inside > 0 ? hits[0].name : "?") + " 占住，人会卡进墙里");

                bool ground = Physics2D.Raycast(ride, Vector2.down, 2.5f, GroundMask).collider != null;
                h.Check("落点脚下有地面：" + station.name, ground, "脚下 2.5 米内没有 Ground 层，会掉下去");
            }

            h.Section("目录");
            var catalog = AssetDatabase.LoadAssetAtPath<TrainStationCatalog>(TrainStationBuilder.CatalogPath);
            if (catalog == null)
            {
                h.Check("站点目录存在", false, "先跑 ⑯c 生成 " + TrainStationBuilder.CatalogPath);
            }
            else
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < catalog.Count; i++)
                {
                    TrainStationCatalog.Entry entry = catalog[i];
                    h.Check("条目有编号 #" + i, !string.IsNullOrWhiteSpace(entry.Id), "编号为空");
                    h.Check("条目编号唯一：" + entry.Id, seen.Add(entry.Id), "目录里重复");

                    bool hasScene = !string.IsNullOrWhiteSpace(entry.ScenePath);
                    h.Check("条目有场景：" + entry.Id, hasScene, "跨场景传送需要 ScenePath");

                    if (hasScene)
                    {
                        bool registered = SceneUtility.GetBuildIndexByScenePath(entry.ScenePath) >= 0;
                        h.Check("场景已进 Build Settings：" + entry.Id, registered, entry.ScenePath);
                    }
                }

                // 当前场景里的站必须都在目录里，否则跨场景传送找不到它。
                foreach (TrainStation station in stations)
                {
                    h.Check("当前场景的站已在目录里：" + station.Id,
                        catalog.TryGet(station.Id, out _), "跑一次 ⑯c 刷新目录");
                }

                // 至少有一个开局开放的站，否则新档玩家既不能传也不能解锁远方的站。
                bool openAtStart = stations.Any(s => s.UnlockedAtStart);
                h.Check("至少一个站开局开放", openAtStart,
                    "没有任何站勾「开局解锁」：全新存档会一座站都用不了，建议把出生站勾上");
            }

            string report = h.Report("火车站");
            if (h.Failed == 0) Debug.Log(report);
            else Debug.LogError(report);
        }

        private static int GroundMask
        {
            get
            {
                int mask = LayerMask.GetMask("Ground");
                return mask != 0 ? mask : 1 << 8;
            }
        }
    }
}
