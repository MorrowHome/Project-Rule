using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuleGame.Gameplay.Travel
{
    /// <summary>
    /// **当前在场**的所有火车站。谁加载进来谁登记，场景一卸载就自动退场。
    /// </summary>
    /// <remarks>
    /// <para>只认识在场对象，这是有意的：不在场的站根本没有落点数据，
    /// 也不能把人塞进一片还没加载的地形里。跨场景的目的地由
    /// <see cref="TrainStationCatalog"/> + <c>WorldStreamer.EnsureLoaded</c> 负责先加载再找站。</para>
    /// <para>静态表要跨 Play 会话清理：本工程关着 Scene Reload，静态字段不会自动重置。</para>
    /// </remarks>
    public static class TrainNetwork
    {
        private static readonly List<TrainStation> StationsInternal = new List<TrainStation>();

        /// <summary>在场站点（按 <see cref="TrainStation.Order"/> 排序）。</summary>
        public static IReadOnlyList<TrainStation> Stations => StationsInternal;

        /// <summary>站点进出的通知（面板开着时用来刷新列表）。</summary>
        public static event Action Changed;

        public static void Register(TrainStation station)
        {
            if (station == null || StationsInternal.Contains(station)) return;

            StationsInternal.Add(station);
            Sort();
            Changed?.Invoke();
        }

        public static void Unregister(TrainStation station)
        {
            if (station == null) return;
            if (StationsInternal.Remove(station)) Changed?.Invoke();
        }

        /// <summary>按编号找在场站点；找不到返回 null。</summary>
        public static TrainStation Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < StationsInternal.Count; i++)
            {
                TrainStation station = StationsInternal[i];
                if (station != null && station.Id == id) return station;
            }

            return null;
        }

        /// <summary>离 <paramref name="point"/> 最近、且不超过 <paramref name="maxDistance"/> 的站。</summary>
        public static TrainStation Nearest(Vector2 point, float maxDistance)
        {
            TrainStation nearest = null;
            float best = maxDistance * maxDistance;

            for (int i = 0; i < StationsInternal.Count; i++)
            {
                TrainStation station = StationsInternal[i];
                if (station == null) continue;

                float distance = ((Vector2)station.transform.position - point).sqrMagnitude;
                if (distance > best) continue;

                best = distance;
                nearest = station;
            }

            return nearest;
        }

        private static void Sort()
        {
            StationsInternal.Sort((a, b) =>
            {
                if (a.Order != b.Order) return a.Order.CompareTo(b.Order);
                return string.CompareOrdinal(a.Id, b.Id);
            });
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            StationsInternal.Clear();
            Changed = null;
        }
    }
}
