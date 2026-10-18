using System;
using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 拼装机关：**所有凹位都入位**之后，驱动一个目标（升门 / 开灯 / 按开关）。
    /// </summary>
    /// <remarks>
    /// <para>它只做三件事：① 订阅每个 <see cref="FitSocket"/> 的入位事件；
    /// ② 全部入位时把 <c>_onSolvedTarget</c>（实现 <see cref="ISwitchTarget"/>）打开；
    /// ③ 有 <c>_gate</c> 的话把它整体抬起来 <c>_gateRise</c> 米。</para>
    ///
    /// <para><b>为什么把「升门」写在这里</b>：闸门本身没有新机制 —— 它就是一块石头。
    /// 抬它不需要 <c>MovingPlatform</c> 那套路径系统（那套是给滑板车 / 电梯用的）。
    /// 想要更复杂的开门动作，把 <c>_gateRise</c> 设成 0，改用 <c>_onSolvedTarget</c> 驱动现成组件。</para>
    ///
    /// <para>灯是可选的：<c>_lamps</c> 与 <c>_sockets</c> 一一对应，谁入位谁亮。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FitPuzzle : MonoBehaviour
    {
        [Header("要凑齐的凹位")]
        [SerializeField] private FitSocket[] _sockets = new FitSocket[0];

        [Header("解开之后")]
        [Tooltip("整体抬起来的闸门（可留空）。")]
        [SerializeField] private Transform _gate;

        [Tooltip("闸门抬起多少米（正数 = 往上）。")]
        [SerializeField] private float _gateRise = 2f;

        [Tooltip("闸门抬起速度（米/秒）。")]
        [SerializeField, Min(0.01f)] private float _gateSpeed = 1.2f;

        [Tooltip("解锁时驱动的目标（实现 ISwitchTarget，可留空）。")]
        [SerializeField] private MonoBehaviour _onSolvedTarget;

        [Header("指示灯（可选，与凹位一一对应）")]
        [SerializeField] private GameObject[] _lamps = new GameObject[0];

        [Tooltip("解锁时打一条 Console。")]
        [SerializeField] private bool _logEvents = true;

        private bool _solved;
        private Vector3 _gateClosed;

        /// <summary>已经解锁。</summary>
        public bool IsSolved => _solved;

        private void OnEnable()
        {
            if (_sockets == null) return;
            foreach (FitSocket socket in _sockets)
            {
                if (socket != null) socket.SeatedEvent += HandleSeated;
            }
        }

        private void OnDisable()
        {
            if (_sockets == null) return;
            foreach (FitSocket socket in _sockets)
            {
                if (socket != null) socket.SeatedEvent -= HandleSeated;
            }
        }

        private void Start()
        {
            if (_gate != null) _gateClosed = _gate.position;

            // 场景可能是「已经拼好」的状态存下来的：开局先对一次。
            Evaluate();
        }

        private void Update()
        {
            if (!_solved || _gate == null) return;

            Vector3 target = _gateClosed + Vector3.up * _gateRise;
            _gate.position = Vector3.MoveTowards(_gate.position, target, _gateSpeed * Time.deltaTime);
        }

        private void HandleSeated(FitSocket socket)
        {
            int index = _sockets != null ? Array.IndexOf(_sockets, socket) : -1;
            if (index >= 0 && _lamps != null && index < _lamps.Length && _lamps[index] != null)
            {
                _lamps[index].SetActive(true);
            }

            Evaluate();
        }

        private void Evaluate()
        {
            if (_solved || _sockets == null || _sockets.Length == 0) return;

            foreach (FitSocket socket in _sockets)
            {
                if (socket == null || !socket.IsSeated) return;
            }

            _solved = true;

            if (_logEvents) Debug.Log("[拼装缺口] 全部入位 —— 机关解开。", this);

            if (_onSolvedTarget is ISwitchTarget target) target.SetSwitchState(true);
        }

        // ==================================================================
        //  供关卡工具配置
        // ==================================================================

        /// <summary>接线（生成工具用）：凹位 + 闸门 + 灯。</summary>
        public void Configure(FitSocket[] sockets, Transform gate, GameObject[] lamps, float rise)
        {
            _sockets = sockets ?? new FitSocket[0];
            _gate = gate;
            _lamps = lamps ?? new GameObject[0];
            _gateRise = rise;
        }
    }
}
