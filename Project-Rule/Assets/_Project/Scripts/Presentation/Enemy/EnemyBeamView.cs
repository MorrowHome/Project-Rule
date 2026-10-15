using UnityEngine;
using RuleGame.Gameplay.Enemy;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>保留原光束伤害盒：预告期间细线，释放时暖色外焰与白色核心同时扩张、消退。</summary>
    [DefaultExecutionOrder(40)]
    public sealed class EnemyBeamView : MonoBehaviour
    {
        private EnemyBeam _beam;
        private LineRenderer _outer, _core;
        private EnemyVfxProfile _profile;
        private float _lastAge;
        private bool _fired;
        public void Configure(EnemyVfxProfile profile)
        {
            _profile = profile; _fired = false; _lastAge = 0f;
            if (_beam == null) _beam = GetComponent<EnemyBeam>();
            if (_outer == null) _outer = GetComponent<LineRenderer>();
            _outer.sharedMaterial = profile.BeamMaterial; _outer.sortingOrder = 14;
            if (_core == null)
            {
                var go = new GameObject("BeamCore"); go.transform.SetParent(transform, false);
                _core = go.AddComponent<LineRenderer>(); _core.positionCount = 2;
                _core.useWorldSpace = true; _core.sortingOrder = 15;
            }
            // 核心用无纹理实线，外层单独保留作者的柔和渐变，避免核心也被纹理压窄。
            _core.sharedMaterial = profile.BeamCoreMaterial;
        }
        private void LateUpdate()
        {
            if (_profile == null || _beam.Source == null) return;
            if (_beam.Age < _lastAge) _fired = false;
            _lastAge = _beam.Age;
            // 原点跟随表盘，而不是身体包围盒中心。发射方向和末端沿用原伤害线。
            Vector3 start = _beam.Origin;
            Vector3 end = _outer.GetPosition(1); start.z = end.z;
            _outer.SetPosition(0, start); _core.SetPosition(0, start); _core.SetPosition(1, end);
            float progress = Mathf.Clamp01(_beam.Age / Mathf.Max(.01f, _beam.WarningDuration));
            bool firing = _beam.Age >= _beam.WarningDuration;
            float fade = firing ? Mathf.Clamp01(1f - (_beam.Age - _beam.WarningDuration) / _beam.FireDuration) : 1f;
            _outer.startWidth = _outer.endWidth = firing ? _beam.Width * (10f + fade * 6f) : .1f + progress * .06f;
            Color outer = _profile.Secondary; outer.a = firing ? fade * .85f : .25f + progress * .4f;
            _outer.startColor = _outer.endColor = outer;
            _core.enabled = firing;
            _core.startWidth = _core.endWidth = _beam.Width * 1.2f * fade;
            _core.startColor = _core.endColor = new Color(1f, .92f, .65f, fade);
            if (firing && !_fired)
            {
                _fired = true;
                var presenter = _beam.Source.GetComponent<EnemyVfxPresenter>();
                presenter?.Play(_profile.Shot, start, 1.8f, Color.white, duration: .24f);
                presenter?.Play(_profile.Impact, end, 2f, Color.white, duration: .45f);
                BossCameraImpulse.Pulse(_profile.CameraImpulse * .3f, .09f);
            }
        }
        private void OnDisable() { if (_core != null) _core.enabled = false; }
    }
}
