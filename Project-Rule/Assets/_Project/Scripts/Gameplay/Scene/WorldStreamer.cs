using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Infrastructure.LevelFlow;

// ⚠️ 本文件的命名空间就叫 RuleGame.Gameplay.Scene，它会把 UnityEngine.SceneManagement.Scene
// 这个**类型名**挡住（命名空间成员优先于 using 别名），所以下面用到时一律全限定。

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 世界流式加载：按玩家位置决定该有哪几个分区在场，加进来的加载、离开的卸载。
    /// </summary>
    /// <remarks>
    /// <para><b>它替代的不是「关卡切换」，是「根本没有切换」</b>：世界是一整张大地图，
    /// 玩家走过去就是走过去，中间没有过场、没有落点。分区只是搭建与性能单位。</para>
    /// <para><b>挂在常驻宿主（<see cref="GameRoot"/>）上</b>：它要活过所有分区。</para>
    /// <para><b>判定是数据驱动的</b>：范围来自 <see cref="PartitionCatalog"/>，
    /// 不依赖场景里的任何标记 —— 因为没加载的场景读不到自己的范围。</para>
    /// <para><b>迟滞</b>：加载用「矩形 + 预载半径」，卸载用「矩形 + 预载半径 + 迟滞」，
    /// 否则玩家站在边界上会来回加载卸载。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WorldStreamer : MonoBehaviour
    {
        [Tooltip("分区表。放 Assets/_Project/Data/Config/")]
        [SerializeField] private PartitionCatalog _catalog;

        [Tooltip("跟随谁判断该加载哪些分区。留空则自动找 PlayerController")]
        [SerializeField] private Transform _focus;

        [Tooltip("判定间隔（秒）。不必每帧算")]
        [SerializeField] private float _checkInterval = 0.2f;

        [Tooltip("开局把玩家摆到哪个出生点（PlayerSpawnPoint.Id）")]
        [SerializeField] private string _startEntryId = PlayerSpawnPoint.DefaultId;

        [Tooltip("开局是否自动把玩家摆到出生点")]
        [SerializeField] private bool _applyStartSpawn = true;

        /// <summary>场景路径 → 分区下标。</summary>
        private readonly Dictionary<string, int> _loaded = new Dictionary<string, int>();
        private readonly HashSet<string> _loading = new HashSet<string>();

        /// <summary>已经报过「没登记」的分区场景，避免每帧刷屏。</summary>
        private readonly HashSet<string> _missingWarned = new HashSet<string>();
        private readonly List<int> _wanted = new List<int>();
        private readonly List<string> _toUnload = new List<string>();

        private float _timer;
        private bool _startSpawnApplied;

        /// <summary>分区表。</summary>
        public PartitionCatalog Catalog => _catalog;

        /// <summary>当前在场分区数。</summary>
        public int LoadedCount => _loaded.Count;

        /// <summary>是否还有分区在加载中。</summary>
        public bool IsLoading => _loading.Count > 0;

        /// <summary>一个分区加载完成（参数是分区号）。</summary>
        public event Action<int> OnPartitionLoaded;

        /// <summary>一个分区卸载完成（参数是分区号）。</summary>
        public event Action<int> OnPartitionUnloaded;

        // ==================================================================
        //  绑定
        // ==================================================================

        /// <summary>由 <see cref="GameRoot"/> 注入依赖。</summary>
        public void Bind(PartitionCatalog catalog, Transform focus)
        {
            _catalog = catalog;
            _focus = focus;
        }

        // ==================================================================
        //  心跳
        // ==================================================================

        private void Update()
        {
            if (_catalog == null) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            _timer = _checkInterval;
            Refresh();
        }

        /// <summary>立刻重算一次该加载 / 卸载哪些分区。</summary>
        public void Refresh()
        {
            if (_catalog == null || _catalog.Count == 0) return;

            if (_focus == null) _focus = FindFocus();
            if (_focus == null) return;

            Vector2 point = _focus.position;

            LoadWanted(point);
            UnloadDeparted(point);
        }

        /// <summary>重开：把在场分区全部卸掉，下一轮按当前位置重新加载。</summary>
        public void ReloadAll()
        {
            _toUnload.Clear();
            foreach (KeyValuePair<string, int> pair in _loaded) _toUnload.Add(pair.Key);

            for (int i = 0; i < _toUnload.Count; i++) StartUnload(_toUnload[i]);

            _startSpawnApplied = false;
            _timer = 0f;
        }

        // ==================================================================
        //  加载
        // ==================================================================

        private void LoadWanted(Vector2 point)
        {
            _wanted.Clear();
            _catalog.CollectWanted(point, _wanted);

            for (int i = 0; i < _wanted.Count; i++)
            {
                int index = _wanted[i];
                PartitionCatalog.PartitionEntry entry = _catalog[index];

                if (string.IsNullOrEmpty(entry.ScenePath)) continue;
                if (_loaded.ContainsKey(entry.ScenePath) || _loading.Contains(entry.ScenePath)) continue;

                // 一次只开一个异步加载：并发 LoadSceneAsync 会让加载时间互相拖长，
                // 而且没必要 —— 预载半径已经给了缓冲。
                if (_loading.Count > 0) return;

                StartLoad(index, entry);
            }
        }

        private void StartLoad(int index, PartitionCatalog.PartitionEntry entry)
        {
            // ⚠️ 不要用 Application.CanStreamedLevelBeLoaded(string) 判断：本工程实测它对**已经登记进
            // Build Settings 的场景**也一律返回 false（Unity 6 里这个字符串重载不可靠），
            // 会把正常配置误报成「没登记」。改查 Build Settings 索引：已登记 ≥ 0，未登记 = -1。
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(entry.ScenePath);
            if (buildIndex < 0)
            {
                // Refresh 每帧都会跑，同一分区只报一次，避免刷屏。
                if (_missingWarned.Add(entry.ScenePath))
                {
                    Debug.LogError($"[WorldStreamer] 分区 {entry.PartitionId} 的场景没登记进 Build Settings：{entry.ScenePath}", this);
                }
                return;
            }

            _loading.Add(entry.ScenePath);
            StartCoroutine(LoadRoutine(index, buildIndex, entry));
        }

        private IEnumerator LoadRoutine(int index, int buildIndex, PartitionCatalog.PartitionEntry entry)
        {
            // 按索引加载：路径只是数据，索引才是 Build Settings 里真正认得的东西。
            AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
            if (op == null)
            {
                _loading.Remove(entry.ScenePath);
                Debug.LogError($"[WorldStreamer] 分区 {entry.PartitionId} 加载失败：{entry.ScenePath}", this);
                yield break;
            }

            while (!op.isDone) yield return null;

            _loading.Remove(entry.ScenePath);
            _loaded[entry.ScenePath] = index;

            if (_applyStartSpawn && !_startSpawnApplied) ApplyStartSpawn();

            OnPartitionLoaded?.Invoke(entry.PartitionId);
        }

        /// <summary>开局出生点：按编号找到 PlayerSpawnPoint 并把玩家搬过去（只做一次）。</summary>
        /// <remarks>先找精确匹配，再退到默认点；都没有就什么都不做，等下一片分区加载完再试。</remarks>
        private void ApplyStartSpawn()
        {
            PlayerSpawnPoint[] points = FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Exclude);
            if (points.Length == 0) return;      // 这一片没有出生点，等下一片

            PlayerSpawnPoint chosen = FindSpawn(points, _startEntryId);
            if (chosen == null) chosen = FindSpawn(points, PlayerSpawnPoint.DefaultId);
            if (chosen == null) return;

            chosen.Apply();
            _startSpawnApplied = true;
        }

        private static PlayerSpawnPoint FindSpawn(PlayerSpawnPoint[] points, string id)
        {
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Id == id) return points[i];
            }

            return null;
        }

        // ==================================================================
        //  卸载
        // ==================================================================

        private void UnloadDeparted(Vector2 point)
        {
            _toUnload.Clear();

            foreach (KeyValuePair<string, int> pair in _loaded)
            {
                int index = pair.Value;
                if (index < 0 || index >= _catalog.Count) continue;

                PartitionCatalog.PartitionEntry entry = _catalog[index];
                if (entry.AlwaysLoaded) continue;

                float keep = _catalog.GetLoadRadius(entry) + _catalog.UnloadHysteresis;
                if (PartitionCatalog.WithinRange(entry, point, keep)) continue;

                _toUnload.Add(pair.Key);
            }

            for (int i = 0; i < _toUnload.Count; i++) StartUnload(_toUnload[i]);
        }

        private void StartUnload(string scenePath)
        {
            UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                _loaded.Remove(scenePath);
                return;
            }

            StartCoroutine(UnloadRoutine(scenePath));
        }

        private IEnumerator UnloadRoutine(string scenePath)
        {
            AsyncOperation op = SceneManager.UnloadSceneAsync(scenePath);
            while (op != null && !op.isDone) yield return null;

            int partitionId = -1;
            if (_loaded.TryGetValue(scenePath, out int index) && index >= 0 && index < _catalog.Count)
                partitionId = _catalog[index].PartitionId;

            _loaded.Remove(scenePath);

            if (partitionId >= 0) OnPartitionUnloaded?.Invoke(partitionId);
        }

        // ==================================================================
        //  工具
        // ==================================================================

        private Transform FindFocus()
        {
            RuleGame.Gameplay.Player.PlayerController player =
                FindAnyObjectByType<RuleGame.Gameplay.Player.PlayerController>();

            return player != null ? player.transform : null;
        }

        private void OnDrawGizmosSelected()
        {
            if (_catalog == null) return;

            for (int i = 0; i < _catalog.Count; i++)
            {
                PartitionCatalog.PartitionEntry entry = _catalog[i];
                Bounds b = PartitionCatalog.GetBounds(entry);

                Gizmos.color = entry.AlwaysLoaded
                    ? new Color(0.2f, 0.6f, 1f, 0.8f)
                    : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                Gizmos.DrawWireCube(b.center, b.size);

                Gizmos.color = new Color(0.9f, 0.7f, 0.2f, 0.35f);
                Gizmos.DrawWireCube(b.center, b.size + Vector3.one * (_catalog.GetLoadRadius(entry) * 2f));
            }
        }
    }
}
