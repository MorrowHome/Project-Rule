using System.Collections.Generic;
using UnityEngine;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 一块「进入就换背景」的世界矩形，和 <c>MusicRegion</c> 同一套做法。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不加碰撞体</b>：判定是「玩家位置落在本地矩形里」，每帧算一次，
    /// 不依赖物理触发 —— 玩家是 Kinematic + Slide，触发回调本来就不可靠。</para>
    /// <para><b>它能随分区一起卸载</b>：场景卸载时 <c>OnDisable</c> 把自己摘出登记表，
    /// <see cref="BackdropDirector"/> 解不到区域就回到默认背景。</para>
    /// <para><b>不要放进 <c>Grid/Art</c></b>：铺装器（Ctrl+Shift+G）会清空那个容器。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BackdropZone : MonoBehaviour
    {
        private static readonly List<BackdropZone> Zones = new List<BackdropZone>();

        [Tooltip("进入这个矩形用哪张背景。")]
        [SerializeField] private BackdropProfile _profile;

        [Tooltip("以此物体为中心的本地矩形（米）。不需要碰撞体。")]
        [SerializeField] private Vector2 _size = new Vector2(30f, 20f);

        [Tooltip("重叠时数值高的优先。BOSS 房这类小区域用 10，整片王国的区域用 0。")]
        [SerializeField] private int _priority;

        public BackdropProfile Profile => _profile;
        public int Priority => _priority;
        public Vector2 Size => _size;

        private void OnEnable()
        {
            if (!Zones.Contains(this)) Zones.Add(this);
        }

        private void OnDisable() => Zones.Remove(this);

        /// <summary>世界坐标点是否落在这个矩形里；<paramref name="margin"/> 是迟滞用的外扩（米）。</summary>
        public bool Contains(Vector3 worldPoint, float margin = 0f)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 scale = transform.lossyScale;
            float mx = margin / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float my = margin / Mathf.Max(0.001f, Mathf.Abs(scale.y));
            return Mathf.Abs(local.x) <= Mathf.Abs(_size.x) * 0.5f + mx
                && Mathf.Abs(local.y) <= Mathf.Abs(_size.y) * 0.5f + my;
        }

        /// <summary>挑出该用哪个区域：重叠取 Priority 高的，上一個区域在外扩范围内继续保留（防抖）。</summary>
        public static BackdropZone Resolve(Vector3 point, BackdropZone previous, float hysteresis)
        {
            BackdropZone best = null;
            for (int i = 0; i < Zones.Count; i++)
            {
                BackdropZone zone = Zones[i];
                if (zone == null || !zone.isActiveAndEnabled || zone.Profile == null
                    || !zone.Contains(point)) continue;
                if (best == null || zone.Priority > best.Priority) best = zone;
            }

            if (previous != null && previous.isActiveAndEnabled && previous.Profile != null
                && previous.Contains(point, hysteresis)
                && (best == null || previous.Priority >= best.Priority)) return previous;
            return best;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_size.x, _size.y, 0.1f));
        }
    }
}
