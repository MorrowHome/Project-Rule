using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuleGame.Infrastructure.LevelFlow
{
    /// <summary>
    /// 分区表：一张连续大地图被切成的各个场景，以及它们在世界坐标里的矩形范围。
    /// </summary>
    /// <remarks>
    /// <para><b>它是什么</b>：世界是一整张大地图，玩家体验是连续的；搭建时按分区切成一个个
    /// 独立场景（方便分工、测试、按需加载省性能）。本表就是「分区 ↔ 世界矩形」的映射。</para>
    /// <para><b>为什么范围记在表里而不是场景里</b>：运行时读不到一个**尚未加载**的场景的范围，
    /// 而「该预载哪几个分区」必须在加载之前就算出来。所以范围只能是外部数据。</para>
    /// <para><b>矩形必须与世界坐标一致</b>：分区场景里的几何直接摆在它真实的世界位置上，
    /// 加载后自然拼成一张连续地图，不做任何偏移。</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "PartitionCatalog", menuName = "RuleGame/Partition Catalog")]
    public sealed class PartitionCatalog : ScriptableObject
    {
        /// <summary>一个分区。</summary>
        [Serializable]
        public struct PartitionEntry
        {
            [Tooltip("分区号，搭建与日志用")]
            public int PartitionId;

            [Tooltip("场景路径，形如 Assets/_Project/Scenes/Partition_01.unity")]
            public string ScenePath;

            [Tooltip("本分区在世界坐标里的矩形中心")]
            public Vector2 Center;

            [Tooltip("本分区在世界坐标里的矩形尺寸（米）")]
            public Vector2 Size;

            [Tooltip("额外预载半径；<= 0 时用表上的默认值")]
            public float LoadRadius;

            [Tooltip("常驻分区：不论玩家在哪都保持加载（起点 / 枢纽）")]
            public bool AlwaysLoaded;
        }

        [SerializeField] private PartitionEntry[] _partitions = Array.Empty<PartitionEntry>();

        [Tooltip("默认预载半径：玩家进入「分区矩形 + 这个半径」的范围就加载它")]
        [SerializeField] private float _defaultLoadRadius = 12f;

        [Tooltip("卸载迟滞：玩家走出「分区矩形 + 预载半径 + 这个值」才卸载，防止站在边界上反复加载卸载")]
        [SerializeField] private float _unloadHysteresis = 8f;

        public int Count => _partitions?.Length ?? 0;

        public float DefaultLoadRadius => _defaultLoadRadius;

        public float UnloadHysteresis => _unloadHysteresis;

        public PartitionEntry this[int index] => _partitions[index];

        /// <summary>分区的世界矩形。</summary>
        public static Bounds GetBounds(in PartitionEntry entry)
            => new Bounds(entry.Center, new Vector3(entry.Size.x, entry.Size.y, 0f));

        /// <summary>本分区生效的预载半径。</summary>
        public float GetLoadRadius(in PartitionEntry entry)
            => entry.LoadRadius > 0f ? entry.LoadRadius : _defaultLoadRadius;

        /// <summary>点是否落在「分区矩形 + margin」内。margin 为负表示内缩。</summary>
        public static bool WithinRange(in PartitionEntry entry, Vector2 point, float margin)
        {
            float halfX = entry.Size.x * 0.5f;
            float halfY = entry.Size.y * 0.5f;

            return point.x >= entry.Center.x - halfX - margin
                && point.x <= entry.Center.x + halfX + margin
                && point.y >= entry.Center.y - halfY - margin
                && point.y <= entry.Center.y + halfY + margin;
        }

        /// <summary>把「按 <paramref name="point"/> 判断应该加载」的分区下标收进 <paramref name="results"/>。不会清空 results。</summary>
        public void CollectWanted(Vector2 point, List<int> results)
        {
            for (int i = 0; i < Count; i++)
            {
                PartitionEntry entry = _partitions[i];
                if (entry.AlwaysLoaded || WithinRange(entry, point, GetLoadRadius(entry)))
                    results.Add(i);
            }
        }

        /// <summary>按场景路径找分区下标。找不到返回 -1。</summary>
        public int IndexOfScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return -1;

            for (int i = 0; i < Count; i++)
            {
                if (string.Equals(_partitions[i].ScenePath, scenePath, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }
    }
}
