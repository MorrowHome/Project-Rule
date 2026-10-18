using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 区域划分：挂在 <c>Grid</c> 上，回答「世界坐标这一点该用哪套瓦片素材」。
    /// </summary>
    /// <remarks>
    /// <para><b>两级</b>：<c>_defaultKit</c> 管整个场景（= 一个世界分区），
    /// <c>_areas</c> 里每个矩形可以覆盖一小片 —— 一个分区横跨两个王国时用。</para>
    ///
    /// <para><b>为什么信息住在场景里</b>：全局区域表会跟场景脱钩（分区目录就因为存路径踩过坑）。
    /// 区域挂在 Grid 上，场景怎么挪都不会失效。</para>
    ///
    /// <para><b>矩形用 BoxCollider2D</b>：和 <see cref="RevealRegion"/> 同一套编辑器体验，
    /// 拖盒子就能改范围，不用手填坐标。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TileRegion : MonoBehaviour
    {
        /// <summary>一小片区域：矩形 + 该片用的套件。</summary>
        [Serializable]
        public sealed class Area
        {
            [Tooltip("范围矩形（只会用它的 bounds，不影响物理）")]
            public BoxCollider2D Box;

            [Tooltip("这一片用哪套瓦片")]
            public TileRegionKit Kit;
        }

        [Tooltip("整个场景（世界分区）默认用哪套。留空 = 回退旧石头套件并告警")]
        [SerializeField] private TileRegionKit _defaultKit;

        [Tooltip("跨王国时在这里加矩形覆盖；命中就优先用它")]
        [SerializeField] private List<Area> _areas = new List<Area>();

        public TileRegionKit DefaultKit => _defaultKit;

        public IReadOnlyList<Area> Areas => _areas;

        /// <summary>
        /// 世界坐标 → 该点用的套件（命中覆盖矩形优先，否则默认；都没有返回 null）。
        /// </summary>
        public TileRegionKit ResolveKit(Vector3 worldPoint)
        {
            for (int i = 0; i < _areas.Count; i++)
            {
                Area area = _areas[i];
                if (area == null || area.Kit == null || area.Box == null) continue;

                Bounds b = area.Box.bounds;
                // 只比较 x/y：2D 盒子的 z 厚度是 0，用 Bounds.Contains 会因浮点误差漏判。
                if (worldPoint.x >= b.min.x && worldPoint.x <= b.max.x
                    && worldPoint.y >= b.min.y && worldPoint.y <= b.max.y)
                {
                    return area.Kit;
                }
            }

            return _defaultKit;
        }

        /// <summary>在 Grid（或它的父级）上找区域组件。</summary>
        public static TileRegion FindFor(Tilemap tilemap)
        {
            if (tilemap == null) return null;
            return tilemap.GetComponentInParent<TileRegion>();
        }
    }
}
