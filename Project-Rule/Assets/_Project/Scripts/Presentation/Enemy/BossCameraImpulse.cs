using UnityEngine;
using UnityEngine.Rendering;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>仅在相机渲染期间偏移视图矩阵，结束后复原，不与跟随或规则特写抢 Transform。</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BossCameraImpulse : MonoBehaviour
    {
        private static event System.Action<float, float> Requested;
        private Camera _camera;
        private Matrix4x4 _view;
        private float _strength, _duration, _age;
        private bool _applied;
        public static void Pulse(float strength, float duration) => Requested?.Invoke(strength, duration);
        private void Awake() => _camera = GetComponent<Camera>();
        private void OnEnable()
        {
            Requested += Begin;
            RenderPipelineManager.beginCameraRendering += BeforeRender;
            RenderPipelineManager.endCameraRendering += AfterRender;
        }
        private void OnDisable()
        {
            Requested -= Begin;
            RenderPipelineManager.beginCameraRendering -= BeforeRender;
            RenderPipelineManager.endCameraRendering -= AfterRender;
            Restore(); _strength = 0f;
        }
        private void Begin(float strength, float duration)
        {
            _strength = Mathf.Max(_strength, strength); _duration = Mathf.Max(.01f, duration); _age = 0f;
        }
        private void Update()
        {
            if (TimeManager.Instance?.IsPaused == true) return;
            _age += Time.deltaTime;
            if (_age >= _duration) _strength = 0f;
        }
        private void BeforeRender(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || _strength <= 0f || TimeManager.Instance?.IsPaused == true) return;
            _view = camera.worldToCameraMatrix;
            float fade = 1f - Mathf.Clamp01(_age / _duration);
            Vector3 offset = new Vector3(Mathf.Sin(_age * 173f), Mathf.Cos(_age * 139f) * .6f, 0f) * (_strength * fade * fade);
            camera.worldToCameraMatrix = Matrix4x4.Translate(offset) * _view; _applied = true;
        }
        private void AfterRender(ScriptableRenderContext context, Camera camera) { if (camera == _camera) Restore(); }
        private void Restore() { if (_applied && _camera != null) _camera.worldToCameraMatrix = _view; _applied = false; }
    }
}
