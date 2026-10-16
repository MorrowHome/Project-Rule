using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 暗墙管理：挂在**逻辑 Tilemap** 上，声明哪些瓦片是「可穿透的暗墙」。
    /// </summary>
    /// <remarks>
    /// <para><b>暗墙 = 没有碰撞体的墙</b>：瓦片资产的 <c>Collider Type</c> 设成 <c>None</c>，
    /// 玩家可以直接走进去。但它在**铺装器眼里仍然是实心几何** ——
    /// 所以暗墙的墙皮和普通墙完全一样，看不出破绽。</para>
    ///
    /// <para><b>和刺的区别</b>：刺（<see cref="HazardField"/>）不参与暴露面判断，
    /// 暗墙参与。所以两者必须分开声明。</para>
    ///
    /// <para><b>揭开</b>：<see cref="RevealIn"/> 清掉范围内的暗墙瓦片，并销毁对应的
    /// <see cref="SecretArt"/> 墙皮。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Tilemap))]
    public sealed class SecretWallField : MonoBehaviour
    {
        [Header("暗墙瓦片")]
        [Tooltip("哪些瓦片算「可穿透的暗墙」。铺装器会照常给它铺墙皮，并把构件标记成可揭开的。")]
        [SerializeField] private TileBase[] _secretTiles = new TileBase[0];

        private Tilemap _tilemap;

        private void Awake() => _tilemap = GetComponent<Tilemap>();

        /// <summary>
        /// 这一格是不是暗墙。铺装器用它把 run 在暗墙边界切开，
        /// 保证「一段要么全是暗墙、要么全是普通墙」。
        /// </summary>
        public bool IsSecretCell(Vector3Int cell)
        {
            if (_tilemap == null) _tilemap = GetComponent<Tilemap>();
            if (_tilemap == null) return false;
            return Matches(_tilemap.GetTile(cell));
        }

        /// <summary>
        /// 揭开范围内的暗墙：清瓦片 + 销毁墙皮。重复调用无副作用。
        /// </summary>
        public void RevealIn(Bounds bounds)
        {
            if (_tilemap == null) _tilemap = GetComponent<Tilemap>();

            if (_tilemap != null)
            {
                BoundsInt cells = _tilemap.cellBounds;
                Vector3Int min = _tilemap.WorldToCell(bounds.min);
                Vector3Int max = _tilemap.WorldToCell(bounds.max);
                int xMin = Mathf.Max(min.x, cells.xMin);
                int xMax = Mathf.Min(max.x, cells.xMax);
                int yMin = Mathf.Max(min.y, cells.yMin);
                int yMax = Mathf.Min(max.y, cells.yMax);

                for (int x = xMin; x <= xMax; x++)
                {
                    for (int y = yMin; y <= yMax; y++)
                    {
                        var c = new Vector3Int(x, y, 0);
                        if (Matches(_tilemap.GetTile(c))) _tilemap.SetTile(c, null);
                    }
                }
            }

            foreach (SecretArt art in FindObjectsByType<SecretArt>())
            {
                if (art == null || !bounds.Intersects(art.Bounds)) continue;
                Destroy(art.gameObject);
            }
        }

        private bool Matches(TileBase tile)
        {
            if (tile == null || _secretTiles == null) return false;
            for (int i = 0; i < _secretTiles.Length; i++)
                if (_secretTiles[i] == tile) return true;
            return false;
        }
    }
}
