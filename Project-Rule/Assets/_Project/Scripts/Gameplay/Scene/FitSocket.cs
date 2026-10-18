using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 拼装凹位：一个**只吃指定形状**的座位。判定三条同时成立才把件吸进来。
    /// </summary>
    /// <remarks>
    /// <para><b>三条判据</b>：① 形状对（只认 <see cref="Accepted"/>）；② 位置对（块心进容差盒）；
    /// ③ 已经停（线速度低于 <c>_maxSpeed</c> 连续 <c>_settleTime</c> 秒）。
    /// 成立即让件 <see cref="FitBlock.Seat"/>：Kinematic + 吸到锚点 + 不再吃推力。</para>
    ///
    /// <para><b>为什么不用「碰撞体卡住」当判定</b>：Dynamic 刚体在凹槽里会抖、会被玩家的
    /// <c>Slide</c> 顶出去一点点、会被别的刚体挤。纯碰撞卡不住「到位」这件事，只能当**视觉**。
    /// 判定必须另算，然后用 snap 把视觉与物理一次对齐。</para>
    ///
    /// <para><b>触发盒怎么来</b>：本物体自己就是座位锚点（放在凹位中心）。<c>Awake</c> 时如果没有
    /// Trigger 碰撞体，会按 <c>_tolerance × 2</c> 自动补一个 Box —— 所以场景里把空物体摆在凹位中心就行。</para>
    ///
    /// <para><b>接线</b>：<see cref="SeatedEvent"/> 给 <c>FitPuzzle</c> 用；
    /// <c>_onSeatedTarget</c> 可以拖任何实现 <see cref="ISwitchTarget"/> 的东西（CitySwitch / 门 / 升降台）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FitSocket : MonoBehaviour
    {
        [Header("只吃哪种形状")]
        [SerializeField] private FitShape _accepted = FitShape.Triangle;

        [Header("座位")]
        [Tooltip("块心与锚点的最大偏差（米）。X = 水平，Y = 竖直。")]
        [SerializeField] private Vector2 _tolerance = new Vector2(0.25f, 0.25f);

        [Tooltip("入位角度（度）。三角用 0；圆球会保留自己当前角度。")]
        [SerializeField] private float _seatAngle;

        [Tooltip("入位后把件挂到谁下面（可空）。凹位在会动的闸门上时要填，否则闸门升走、件留在原地。")]
        [SerializeField] private Transform _seatParent;

        [Header("判定")]
        [Tooltip("低于这个线速度才算「停住了」（米/秒）。")]
        [SerializeField, Min(0f)] private float _maxSpeed = 1f;

        [Tooltip("要连续静止多久才判入位（秒）。防高速穿过时误判。")]
        [SerializeField, Min(0.01f)] private float _settleTime = 0.12f;

        [Header("输出")]
        [Tooltip("入位后要驱动的东西（实现 ISwitchTarget 的任意组件，可留空）。")]
        [SerializeField] private MonoBehaviour _onSeatedTarget;

        [Tooltip("入位时打一条 Console。调试用，正式关卡可以关。")]
        [SerializeField] private bool _logEvents = true;

        private float _hold;
        private BoxCollider2D _trigger;

        // ==================================================================
        //  事件 / 状态
        // ==================================================================

        /// <summary>入位时触发一次（参数是自己）。</summary>
        public event System.Action<FitSocket> SeatedEvent;

        /// <summary>已经入位。</summary>
        public bool IsSeated { get; private set; }

        /// <summary>入位的是谁（没入位时为 null）。</summary>
        public FitBlock SeatedBlock { get; private set; }

        /// <summary>本凹位只吃哪种形状。</summary>
        public FitShape Accepted => _accepted;

        /// <summary>座位锚点的世界坐标。</summary>
        public Vector2 AnchorPosition => transform.position;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _trigger = GetComponent<BoxCollider2D>();
            if (_trigger == null) _trigger = gameObject.AddComponent<BoxCollider2D>();

            _trigger.isTrigger = true;
            _trigger.size = new Vector2(Mathf.Max(0.1f, _tolerance.x * 2f), Mathf.Max(0.1f, _tolerance.y * 2f));
            _trigger.offset = Vector2.zero;
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (IsSeated) return;

            FitBlock block = other.GetComponentInParent<FitBlock>();
            if (block == null || block.Shape != _accepted || block.IsSeated)
            {
                ResetHold();
                return;
            }

            Vector2 delta = block.Body.position - AnchorPosition;
            if (Mathf.Abs(delta.x) > _tolerance.x || Mathf.Abs(delta.y) > _tolerance.y)
            {
                ResetHold();
                return;
            }

            if (block.Body.linearVelocity.sqrMagnitude > _maxSpeed * _maxSpeed)
            {
                ResetHold();
                return;
            }

            _hold += Time.fixedDeltaTime;
            if (_hold >= _settleTime) Seat(block);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (IsSeated) return;
            if (other.GetComponentInParent<FitBlock>() != null) ResetHold();
        }

        // ==================================================================
        //  入位
        // ==================================================================

        private void Seat(FitBlock block)
        {
            IsSeated = true;
            SeatedBlock = block;
            _hold = 0f;

            block.Seat(AnchorPosition, _seatAngle, _seatParent);

            if (_logEvents)
            {
                Debug.Log($"[拼装缺口] {name} 入位：{block.name}（{_accepted}）→ Kinematic 固定。", this);
            }

            if (_onSeatedTarget is ISwitchTarget target) target.SetSwitchState(true);

            SeatedEvent?.Invoke(this);
        }

        private void ResetHold() => _hold = 0f;

        // ==================================================================
        //  供关卡工具配置
        // ==================================================================

        /// <summary>设置接受的形状与座位（生成工具用）。</summary>
        public void Configure(FitShape accepted, Vector2 tolerance, Transform seatParent = null, float seatAngle = 0f)
        {
            _accepted = accepted;
            _tolerance = tolerance;
            _seatParent = seatParent;
            _seatAngle = seatAngle;
        }
    }
}
