using System;
using UnityEngine;

namespace RuleGame.Gameplay.Travel
{
    /// <summary>
    /// 火车站点的**全量目录**：跨场景传送时，目的地可能还没加载，列表只能靠它。
    /// </summary>
    /// <remarks>
    /// <para><b>目录里只放「不随场景消失」的数据</b>：编号、显示名、所在场景、排序。
    /// 落点坐标<b>不进目录</b> —— 它在场景里的 <see cref="TrainStation"/> 身上，
    /// 场景没加载就读不到；等加载完再按编号找站，位置自然就有了
    /// （与 <c>PartitionCatalog</c> 同一条「数据驱动、不复制场景数据」的做法）。</para>
    /// <para><b>为什么必须有</b>：<see cref="TrainNetwork"/> 只认识**当前在场**的站。
    /// 没有目录的话，玩家在 A 区就看不到 B 区的站，跨地图快速移动根本不成立。</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "规则游戏/火车站点目录", fileName = "TrainStationCatalog")]
    public sealed class TrainStationCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("站点编号。必须与场景里 TrainStation 的 Id 完全一致。")]
            public string Id;

            [Tooltip("列表里显示的名字。留空则显示编号。")]
            public string DisplayName;

            [Tooltip("站点所在的场景路径（Assets/...unity）。跨场景传送靠它预加载。")]
            public string ScenePath;

            [Tooltip("列表排序，小的在前。")]
            public int Order;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public int Count => _entries != null ? _entries.Length : 0;

        public Entry this[int index] => _entries[index];

        public bool TryGet(string id, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(id) || _entries == null) return false;

            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Id != id) continue;
                entry = _entries[i];
                return true;
            }

            return false;
        }

        /// <summary>编辑器工具用：整体写回。运行时不要调。</summary>
        public void SetEntries(Entry[] entries) => _entries = entries ?? Array.Empty<Entry>();
    }
}
