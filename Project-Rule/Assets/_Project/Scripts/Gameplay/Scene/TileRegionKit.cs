using System;
using UnityEngine;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 区域套件：一套「这个王国的石头长什么样」。
    /// </summary>
    /// <remarks>
    /// <para><b>只存素材，不存位置</b>：位置由 <see cref="TileRegion"/> 在场景里划分。
    /// 这样同一套素材可以在多个分区复用，也不会出现「全局区域表跟场景脱钩」的问题。</para>
    ///
    /// <para><b>为什么四个朝向共用一批方块</b>：区域素材都是「1 格一块」，
    /// 靠旋转让粗糙边 / 齿轮边朝外，所以不需要为每个朝向单独准备图。</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "TileRegionKit", menuName = "RuleGame/瓦片/区域套件")]
    public sealed class TileRegionKit : ScriptableObject
    {
        [Tooltip("显示名（日志、Inspector 用），如「发条王国」")]
        [SerializeField] private string _displayName = "未命名区域";

        [Tooltip("1 格方块，四个朝向共用。按格子坐标挑变体，最少 1 张")]
        [SerializeField] private Sprite[] _blocks = Array.Empty<Sprite>();

        [Tooltip("变体哈希偏移：让相邻区域即使格子坐标相同也不会排出一样的图")]
        [SerializeField] private int _variantSeed;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;

        public Sprite[] Blocks => _blocks;

        public int VariantSeed => _variantSeed;

        /// <summary>这套素材能不能用（没配方块就该回退默认套件）。</summary>
        public bool IsUsable => _blocks != null && _blocks.Length > 0;

        /// <summary>按格子坐标稳定地挑一个变体。</summary>
        public Sprite BlockAt(Vector3Int cell)
        {
            if (!IsUsable) return null;

            int h = (cell.x + _variantSeed) * 73856093 ^ (cell.y - _variantSeed) * 19349663;
            h = (h ^ (h >> 13)) & 0x7fffffff;
            return _blocks[h % _blocks.Length];
        }
    }
}
