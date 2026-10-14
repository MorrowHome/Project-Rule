using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>池化投射物复用时重新接线并清空拖尾；不修改速度、碰撞半径或命中判定。</summary>
    [DefaultExecutionOrder(40)]
    public sealed class EnemyProjectileView : MonoBehaviour
    {
        private EnemyProjectile _shot;
        private SpriteRenderer _body;
        private TrailRenderer _trail;
        private EnemyVfxProfile _profile;
        private EnemyActor _source;
        private float _age;
        private void Awake() => _shot = GetComponent<EnemyProjectile>();
        public void Configure(EnemyVfxProfile profile)
        {
            _profile = profile; _age = 0f;
            if (_shot == null) _shot = GetComponent<EnemyProjectile>();
            if (_body == null)
            {
                var go = new GameObject("ProjectileVisual"); go.transform.SetParent(transform, false);
                _body = go.AddComponent<SpriteRenderer>(); _body.sortingOrder = 8;
            }
            // 子物体独占外观变换；根节点的比例和旋转仍由投射物/快照系统管理。
            GetComponent<SpriteRenderer>().enabled = false;
            if (_source != null) _source.Restored -= OnRestore;
            _source = _shot.Source;
            if (_source != null) _source.Restored += OnRestore;
            _body.sprite = profile.ProjectileSprite;
            _body.enabled = true;
            _body.sharedMaterial = profile.Material;
            _body.color = profile.Accent;
            if (_trail == null)
            {
                _trail = gameObject.AddComponent<TrailRenderer>();
                _trail.sortingOrder = 7; _trail.minVertexDistance = .08f;
                _trail.numCapVertices = 4; _trail.numCornerVertices = 3;
                _trail.textureMode = LineTextureMode.Stretch;
                _trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            }
            _trail.sharedMaterial = profile.BeamMaterial;
            _trail.time = .18f; _trail.startWidth = .28f; _trail.endWidth = 0f;
            _trail.startColor = profile.Secondary; _trail.endColor = new Color(profile.Accent.r, profile.Accent.g, profile.Accent.b, 0f);
            _trail.Clear(); _trail.emitting = true;
        }
        private void OnRestore() { _age = 0f; if (_trail != null) _trail.Clear(); }
        private void OnDisable()
        {
            if (_source != null) _source.Restored -= OnRestore;
            _source = null;
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
        }
        private void OnEnable()
        {
            // 公共弹池也会被无特效的杂兵复用，先恢复其原始外观，不能残留上一个 Boss 的颜色。
            _profile = null;
            GetComponent<SpriteRenderer>().enabled = true;
            if (_body != null) _body.enabled = false;
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
        }
        private void LateUpdate()
        {
            if (_shot.Source == null) return;
            if (_source != _shot.Source)
            {
                if (_source != null) _source.Restored -= OnRestore;
                var presenter = _shot.Source.GetComponent<EnemyVfxPresenter>();
                if (presenter != null && presenter.Profile != null) Configure(presenter.Profile);
                else
                {
                    _source = _shot.Source; _profile = null;
                    GetComponent<SpriteRenderer>().enabled = true;
                    if (_body != null) _body.enabled = false;
                    if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
                }
            }
            if (_profile == null) return;
            // 已发射弹体继续独立飞行，不跟随发射者的单体暂停。
            bool paused = TimeManager.Instance?.IsPaused == true;
            _trail.emitting = !paused;
            // TrailRenderer 的历史点用引擎时间清退，实体暂停时延长寿命以保留现有轨迹。
            _trail.time = paused ? 1000f : .18f;
            if (paused) return;
            _age += Time.deltaTime;
            bool ground = _shot.Gravity == 0f && _shot.Radius >= .3f;
            _body.transform.localRotation = Quaternion.Euler(0f, 0f, ground ? 0f :
                Mathf.Atan2(_shot.Velocity.y, _shot.Velocity.x) * Mathf.Rad2Deg);
            _body.transform.localScale = ground ? new Vector3(1.7f, .8f, 1f) : Vector3.one * 1.7f;
            if (_profile.Shot != null && !ground)
                _body.sprite = _profile.Shot.Frame(_age % _profile.Shot.Duration);
        }
    }
}
