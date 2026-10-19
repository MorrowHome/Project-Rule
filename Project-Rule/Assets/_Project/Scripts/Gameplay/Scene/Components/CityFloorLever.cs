using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>
    /// **多档拉杆**：档位由「**打过来的位置**」决定 —— 站在左边打扳到左档、中间打停中间、右边打扳到右档。
    /// </summary>
    /// <remarks>
    /// <para><b>与 <c>CitySwitch</c> 的分工</b>：那个是开 / 关两态、按一下切一下；这个有 N 档，
    /// 而且**不循环** —— 位置直接映射到档位，站在同一个位置连打两下还是那一档
    /// （电梯已经在那一站，<see cref="CityMultiStopLift.SelectStop"/> 会直接忽略）。</para>
    ///
    /// <para><b>分区怎么算</b>：以拉杆为中心、<see cref="_selectHalfWidth"/> 为半宽，把这段横向范围
    /// **平均切成 N 段**，打过来的点落在第几段就是第几档。3 档时正好是「左 / 中 / 右」；
    /// 2 档时是「左半 / 右半」。</para>
    ///
    /// <para><b>打过来的点是谁</b>：近战传**玩家自己的位置**（不是攻击盒中心 —— 那个永远在身前
    /// 0.8 米，整体会偏一档）；纸飞机传**自己撞上去那一刻的位置**。所以人站在车上的哪一头
    /// 去砍它，就对应哪一层，这正是玩法。</para>
    ///
    /// <para><b>随车移动</b>：拉杆是车的**兄弟节点**，由 <c>CityMultiStopLift</c> 的
    /// <c>_carriedBodies</c> 每步带着走（不要挂成车的子物体，理由见那个组件）。</para>
    /// </remarks>
    public sealed class CityFloorLever : CityInteractable, IContactSwitch
    {
        [Header("档位")]
        [Tooltip("每一档把手停的角度（绕 Z，度），从最左档排到最右档。档数 = 这个数组的长度，也是电梯的站点数。")]
        [SerializeField] private float[] _angles = { 25f, -35f, -95f };

        [Tooltip("选档的左右半宽（米）：以拉杆为中心，± 这个宽度是整根拉杆的选择范围，内部平均分 N 段。")]
        [SerializeField, Min(0.1f)] private float _selectHalfWidth = 1.2f;

        [Tooltip("把档位左右反过来（左段 = 最高档）。拉杆装在车的右端、或电梯井朝下时用。")]
        [SerializeField] private bool _reverse;

        [Header("把手")]
        [Tooltip("会转的把手（子物体）。留空 = 只联动、不转。")]
        [SerializeField] private Transform _handle;

        [Tooltip("把手转速（度 / 秒）。小于等于 0 = 直接到位。")]
        [SerializeField, Min(0f)] private float _handleDegreesPerSecond = 540f;

        [Header("联动")]
        [Tooltip("拖这台电梯。留空会自动在父级 / 父级的子物体里找 CityMultiStopLift。")]
        [SerializeField] private CityMultiStopLift _lift;

        /// <summary>当前档位（0 = 最左档 / 最低站）。</summary>
        public int Position { get; private set; }

        /// <summary>档数。电梯没接上时退回角度数组的长度。</summary>
        public int StopCount => _lift != null && _lift.StopCount > 0
            ? _lift.StopCount
            : Mathf.Max(2, _angles != null ? _angles.Length : 2);

        protected override void Awake()
        {
            base.Awake();
            ResolveLift();
            Position = Mathf.Clamp(Position, 0, StopCount - 1);
        }

        private void ResolveLift()
        {
            if (_lift != null) return;
            if (transform.parent != null) _lift = transform.parent.GetComponentInChildren<CityMultiStopLift>(true);
            if (_lift == null) _lift = GetComponentInParent<CityMultiStopLift>();
        }

        // ==================================================================
        //  交互（E 键 / 攻击盒 / 投射物）—— 都走同一条「按位置选档」
        // ==================================================================

        public override bool Interact(PlayerController player)
        {
            if (Paused) return false;

            Vector2 from = player != null ? (Vector2)player.transform.position : (Vector2)transform.position;
            Apply(PositionFor(from));
            return true;
        }

        /// <summary>被攻击盒 / 投射物打中：按来源点选档（边沿，没有「离开」）。</summary>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (Paused) return;
            Apply(PositionFor(sourcePoint));
        }

        /// <summary>被重物压到：压过来的东西没有「站位」，按中心算（正中 = 中间那一档）。</summary>
        public void PressByContact() => ImpactByContact(transform.position);

        /// <summary>压着的东西走了：多档拉杆没有「弹回」这回事。</summary>
        public void ReleaseByContact() { }

        // ==================================================================
        //  档位
        // ==================================================================

        /// <summary>世界坐标 → 档位。横向平均分 N 段，落在第几段就是第几档。</summary>
        public int PositionFor(Vector2 worldPoint)
        {
            int count = StopCount;
            float local = transform.InverseTransformPoint(worldPoint).x;
            float t = Mathf.InverseLerp(-_selectHalfWidth, _selectHalfWidth, local);
            int index = Mathf.Clamp(Mathf.FloorToInt(t * count), 0, count - 1);
            return _reverse ? count - 1 - index : index;
        }

        /// <summary>选档并驱动电梯。同一档重复调用是安全的（电梯已经在那儿就不动）。</summary>
        public void Apply(int index)
        {
            Position = Mathf.Clamp(index, 0, StopCount - 1);
            if (_lift != null) _lift.SelectStop(Position);
        }

        private void LateUpdate()
        {
            if (_lift != null)
            {
                // 外部换了目标（回溯、别的脚本）时把手跟着走。
                int wanted = Mathf.Clamp(_lift.TargetStop, 0, StopCount - 1);
                if (wanted != Position) Position = wanted;
            }

            AnimateHandle();
        }

        private void AnimateHandle()
        {
            if (_handle == null || _angles == null || _angles.Length == 0) return;

            float target = _angles[Mathf.Clamp(Position, 0, _angles.Length - 1)];
            Vector3 euler = _handle.localEulerAngles;

            float z = _handleDegreesPerSecond > 0f
                ? Mathf.MoveTowardsAngle(euler.z, target, _handleDegreesPerSecond * Time.deltaTime)
                : target;

            if (Mathf.Approximately(z, euler.z)) return;
            _handle.localRotation = Quaternion.Euler(euler.x, euler.y, z);
        }

        // ==================================================================
        //  快照 / 编辑器
        // ==================================================================

        protected override object CaptureMechanismState() => Position;

        protected override void RestoreMechanismState(object state)
        {
            // 不回头驱动电梯：电梯自己的快照已经恢复了目标站。
            if (state is int position) Position = Mathf.Clamp(position, 0, StopCount - 1);
        }

        /// <summary>只给编辑器工具用：一次写档位角度。</summary>
        public void SetAngles(float[] angles)
        {
            _angles = angles != null && angles.Length > 0 ? angles : new[] { 25f, -35f };
        }

        /// <summary>只给编辑器工具用：一次写选档半宽（车厢变窄时要把左右档位一起收进来）。</summary>
        public void SetSelectionWidth(float halfWidth)
        {
            _selectHalfWidth = Mathf.Max(0.1f, halfWidth);
        }

        /// <summary>只给编辑器工具用：指定把手与电梯。</summary>
        public void SetWiring(Transform handle, CityMultiStopLift lift)
        {
            _handle = handle;
            _lift = lift;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 center = transform.position;
            float half = _selectHalfWidth;

            // 选择范围：左右边界 + 每段的边界（几档就画几条分界）。
            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.85f);
            Gizmos.DrawLine(center + Vector3.left * half + Vector3.up * 0.6f,
                            center + Vector3.left * half - Vector3.up * 0.9f);
            Gizmos.DrawLine(center + Vector3.right * half + Vector3.up * 0.6f,
                            center + Vector3.right * half - Vector3.up * 0.9f);

            int count = StopCount;
            Gizmos.color = new Color(0.9f, 0.75f, 0.3f, 0.7f);
            for (int i = 1; i < count; i++)
            {
                float x = Mathf.Lerp(-half, half, i / (float)count);
                Gizmos.DrawLine(center + Vector3.right * x + Vector3.up * 0.5f,
                                center + Vector3.right * x - Vector3.down * 0.5f);
            }

            // 当前档
            if (!Application.isPlaying || _angles == null || _angles.Length == 0) return;
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(center, 0.22f);
        }
    }
}
