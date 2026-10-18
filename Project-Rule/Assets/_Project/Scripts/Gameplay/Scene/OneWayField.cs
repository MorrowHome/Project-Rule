using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 跳穿平台管理：挂在 <c>Logic</c> Tilemap 上，把 <see cref="OneWayTile"/> 格子变成**一格一块**的单向平台。
    /// </summary>
    /// <remarks>
    /// <para>和 <see cref="HazardField"/> / <see cref="SecretWallField"/> 同一套路：瓦片负责「哪几格」，
    /// 组件负责「什么后果」。这里每格生成一个 <c>BoxCollider2D</c>（正好一格大）+ <see cref="OneWayPlatform"/>。</para>
    ///
    /// <para><b>为什么必须逐格生成</b>：整张 Tilemap 只有一个碰撞体，没法单独开关某一格 ——
    /// 而单向平台的核心就是「这一格此刻对玩家实不实心」。所以瓦片的 Collider Type 要是 None，
    /// 碰撞完全交给这里。</para>
    /// </remarks>
    [RequireComponent(typeof(Tilemap))]
    public sealed class OneWayField : MonoBehaviour
    {
        private const string RootName = "OneWayColliders";

        [Tooltip("兜底放行时长；瓦片资产上填了就用瓦片的")]
        [SerializeField] private float _dropDuration = 0.35f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>运行时生成的平台数量（诊断用）。</summary>
        public int Count => _spawned.Count;

        private void Awake()
        {
            Build();
        }

        /// <summary>把当前 Tilemap 里的跳穿瓦片变成一格一块的单向平台。</summary>
        public void Build()
        {
            Clear();

            var map = GetComponent<Tilemap>();
            if (map == null) return;

            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0)
            {
                Debug.LogError("[跳穿平台] 工程里没有 Ground 层，平台放下去玩家踩不到。");
                return;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(transform, false);
            root.layer = groundLayer;
            _spawned.Add(root);

            Vector3 cell = map.layoutGrid != null ? map.layoutGrid.cellSize : new Vector3(0.5f, 0.5f, 0f);
            BoundsInt bounds = map.cellBounds;

            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (!(map.GetTile(c) is OneWayTile tile)) continue;

                    var go = new GameObject($"OneWay_{x}_{y}");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = map.layoutGrid.CellToWorld(c)
                                            + new Vector3(cell.x * 0.5f, cell.y * 0.5f, 0f);   // 中心对齐格子
                    go.layer = groundLayer;

                    var box = go.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(cell.x, cell.y);      // 正好一格

                    var platform = go.AddComponent<OneWayPlatform>();
                    platform.DropDuration = tile.DropDuration > 0f ? tile.DropDuration : _dropDuration;

                    _spawned.Add(go);
                }
            }
        }

        /// <summary>清掉上一次生成的平台（重铺 / 改完瓦片后可重跑）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }
    }
}
