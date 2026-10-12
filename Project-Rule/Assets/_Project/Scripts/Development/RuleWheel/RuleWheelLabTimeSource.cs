using System;
using RuleGame.Infrastructure.Interaction;
using UnityEngine;

namespace RuleGame.Development.RuleWheel
{
    /// <summary>仅供独立试播场景。P1 的多来源时间管理完成后，用其接口实现替换此组件。</summary>
    public sealed class RuleWheelLabTimeSource : MonoBehaviour, IRuleSelectionTimeSource
    {
        private Lease _lease;
        private float _entryScale, _entryFixedStep, _writtenScale, _writtenFixedStep;

        public IDisposable AcquireSelectionScale(float scale)
        {
            if (_lease != null || !isActiveAndEnabled || Time.timeScale <= 0f) return null;
            _entryScale = Time.timeScale;
            _entryFixedStep = Time.fixedDeltaTime;
            float factor = Mathf.Clamp(scale, 0.01f, 1f);
            _writtenScale = _entryScale * factor;
            _writtenFixedStep = _entryFixedStep * factor;
            Time.timeScale = _writtenScale;
            Time.fixedDeltaTime = _writtenFixedStep;
            // Unity 的时间步长有内部量化，读回值可能比传入 float 略小。
            // 必须记录实际写入结果，否则关闭时会误判为外部修改，步长每次再缩小十倍。
            _writtenScale = Time.timeScale;
            _writtenFixedStep = Time.fixedDeltaTime;
            _lease = new Lease(this);
            return _lease;
        }

        private void Restore()
        {
            if (_lease == null) return;
            // 仅恢复自己仍持有的值，不覆盖期间由其他系统重新写入的倍率。
            if (Mathf.Approximately(Time.timeScale, _writtenScale)) Time.timeScale = _entryScale;
            if (Mathf.Approximately(Time.fixedDeltaTime, _writtenFixedStep)) Time.fixedDeltaTime = _entryFixedStep;
            _lease = null;
        }

        private void OnDisable() => Restore();
        private sealed class Lease : IDisposable
        {
            private RuleWheelLabTimeSource _owner;
            public Lease(RuleWheelLabTimeSource owner) => _owner = owner;
            public void Dispose() { if (_owner != null) _owner.Restore(); _owner = null; }
        }
    }
}
