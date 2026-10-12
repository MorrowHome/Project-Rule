using System;
using RuleGame.Infrastructure.Interaction;
using UnityEngine;

namespace RuleGame.Presentation.RuleWheel
{
    /// <summary>P4 临时特写：选择时推近并给玩家右侧留出空间，结束后归还场景原有跟随组件。</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RuleSelectionCamera : MonoBehaviour, IRuleSelectionFocus
    {
        [SerializeField] private Transform _target;
        [Tooltip("可选：已有的跟随组件。只在选择与回拉期间接管，完成后归还。")]
        [SerializeField] private Behaviour _normalFollow;
        [SerializeField] private Vector2 _followOffset = new Vector2(0f, 0.7f);
        [SerializeField] private Vector2 _selectionOffset = new Vector2(1.3f, 0.35f);
        [SerializeField, Min(0.1f)] private float _selectionSize = 2.8f;
        [SerializeField, Min(0.01f)] private float _transitionTime = 0.18f;
        private Camera _camera;
        private float _normalSize, _sizeVelocity;
        private Vector3 _positionVelocity;
        private int _focusCount;
        private bool _returning, _followWasEnabled;
        private Vector3 _entryOffset;
        public bool IsFocused => _focusCount > 0;
        public void BindTarget(Transform target) => _target = target;

        private void Awake() { _camera = GetComponent<Camera>(); _normalSize = _camera.orthographicSize; }
        public IDisposable AcquireSelectionFocus()
        {
            if (!isActiveAndEnabled || _target == null) return null;
            if (_focusCount == 0 && _normalFollow != null)
            {
                if (!_returning)
                {
                    _entryOffset = transform.position - _target.position;
                    _followWasEnabled = _normalFollow.enabled;
                }
                _normalFollow.enabled = false;
                _returning = false;
            }
            _focusCount++;
            return new Lease(this);
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            if (_normalFollow != null && !IsFocused && !_returning) return;
            Vector2 offset = IsFocused ? _selectionOffset : _followOffset;
            Vector3 position = _target.position + (Vector3)offset;
            position.z = transform.position.z;
            if (_normalFollow != null && !IsFocused) position = _target.position + _entryOffset;
            // 特写与回拉不受世界慢时间影响，否则镜头动画会被拖慢十倍。
            transform.position = Vector3.SmoothDamp(transform.position, position, ref _positionVelocity,
                _transitionTime, Mathf.Infinity, Time.unscaledDeltaTime);
            _camera.orthographicSize = Mathf.SmoothDamp(_camera.orthographicSize,
                IsFocused ? _selectionSize : _normalSize, ref _sizeVelocity, _transitionTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
            if (_normalFollow != null && !IsFocused && _returning
                && Mathf.Abs(_camera.orthographicSize - _normalSize) < 0.02f)
            {
                // 玩家恢复走动后，追赶移动目标可能永远达不到位置阈值。
                // 缩放回到正常取景即可归还，后续位置由原跟随组件继续平滑。
                _returning = false;
                _normalFollow.enabled = _followWasEnabled;
            }
        }

        private void OnDisable()
        {
            _focusCount = 0;
            if (_normalFollow != null && (_returning || !_normalFollow.enabled))
                _normalFollow.enabled = _followWasEnabled;
            _returning = false;
            if (_camera != null) _camera.orthographicSize = _normalSize;
            _sizeVelocity = 0f;
            _positionVelocity = Vector3.zero;
        }

        private sealed class Lease : IDisposable
        {
            private RuleSelectionCamera _owner;
            public Lease(RuleSelectionCamera owner) => _owner = owner;
            public void Dispose()
            {
                if (_owner != null)
                {
                    _owner._focusCount = Mathf.Max(0, _owner._focusCount - 1);
                    if (_owner._focusCount == 0 && _owner._normalFollow != null) _owner._returning = true;
                }
                _owner = null;
            }
        }
    }
}
