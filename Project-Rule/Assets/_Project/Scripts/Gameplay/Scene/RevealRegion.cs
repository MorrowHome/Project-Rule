using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 揭示区域：玩家进入后，清掉这里的黑色遮罩，并揭掉这里的暗墙。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不用触发器回调</b>：玩家是 Kinematic 刚体 + <c>Rigidbody2D.Slide</c>，
    /// 默认不与静态碰撞体产生触发回调。这里只用一个 <see cref="BoxCollider2D"/> 当**范围定义**
    /// （Inspector 里拖手柄就能改大小），判定靠比较坐标，和物理无关。</para>
    ///
    /// <para><b>黑色遮罩怎么工作</b>：遮罩是一张普通 Tilemap（黑瓦片），排序在所有世界美术之上。
    /// 本区域被触发时，只清掉**自己范围内**的遮罩格 —— 所以一张遮罩图可以服务整个关卡。</para>
    ///
    /// <para><b>暗墙</b>：见 <see cref="SecretWallField"/>。同一次揭示里一起处理，两者天然同步。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class RevealRegion : MonoBehaviour
    {
        [Header("触发")]
        [Tooltip("谁进来算数。留空则自动找场景里的 PlayerController。")]
        [SerializeField] private Transform _revealer;

        [Header("要揭开的东西")]
        [Tooltip("黑色遮罩层（黑瓦片 Tilemap）。进入后清掉本区域范围内的遮罩格。")]
        [SerializeField] private Tilemap _mask;

        [Tooltip("暗墙管理组件（挂在逻辑 Tilemap 上）。没有暗墙就留空。")]
        [SerializeField] private SecretWallField _secretWalls;

        private BoxCollider2D _area;
        private bool _revealed;

        /// <summary>是否已经揭开。</summary>
        public bool IsRevealed => _revealed;

        /// <summary>区域范围（世界坐标），由 BoxCollider2D 定义。</summary>
        public Bounds Bounds => _area != null ? _area.bounds : new Bounds(transform.position, Vector3.zero);

        private void Awake()
        {
            _area = GetComponent<BoxCollider2D>();
            _area.isTrigger = true;      // 只当范围用，绝不挡路

            if (_revealer == null)
            {
                var player = FindAnyObjectByType<RuleGame.Gameplay.Player.PlayerController>();
                if (player != null) _revealer = player.transform;
            }
        }

        private void Update()
        {
            if (_revealed || _revealer == null) return;
            if (!Bounds.Contains(_revealer.position)) return;
            Reveal();
        }

        /// <summary>揭开本区域。重复调用无副作用。</summary>
        public void Reveal()
        {
            if (_revealed) return;
            _revealed = true;

            Bounds bounds = Bounds;
            if (_mask != null) ClearMask(bounds);
            if (_secretWalls != null) _secretWalls.RevealIn(bounds);
        }

        private void ClearMask(Bounds bounds)
        {
            BoundsInt cells = _mask.cellBounds;
            Vector3Int min = _mask.WorldToCell(bounds.min);
            Vector3Int max = _mask.WorldToCell(bounds.max);
            int xMin = Mathf.Max(min.x, cells.xMin);
            int xMax = Mathf.Min(max.x, cells.xMax);
            int yMin = Mathf.Max(min.y, cells.yMin);
            int yMax = Mathf.Min(max.y, cells.yMax);

            for (int x = xMin; x <= xMax; x++)
                for (int y = yMin; y <= yMax; y++)
                    _mask.SetTile(new Vector3Int(x, y, 0), null);
        }
    }
}
