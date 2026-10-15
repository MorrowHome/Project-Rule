using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>
    /// 只订阅战斗通知。12 个本地播放槽限制峰值和对象数量；持续攻击不会逐物理帧重播。
    /// 世界慢时间自然减慢序列帧，实体暂停则单独冻结；读档清掉旧时间线的瞬时效果。
    /// </summary>
    [RequireComponent(typeof(EnemyActor))]
    public sealed class EnemyVfxPresenter : MonoBehaviour
    {
        public EnemyVfxProfile Profile;
        private EnemyActor _actor;
        private readonly Slot[] _slots = new Slot[12];
        private float _lastShot = -10f;
        private bool _restoring;

        private sealed class Slot
        {
            public SpriteRenderer Renderer;
            public EnemyVfxClip Clip;
            public float Age, Speed;
            public Transform Follow;
            public Vector3 Offset;
            public Vector3 Position;
        }
        private void Awake()
        {
            _actor = GetComponent<EnemyActor>();
        }
        private void OnEnable()
        {
            _actor.AnimationChanged += OnAction;
            _actor.Feedback += OnFeedback;
            _actor.Restored += OnRestore;
        }
        private void OnDisable()
        {
            _actor.AnimationChanged -= OnAction;
            _actor.Feedback -= OnFeedback;
            _actor.Restored -= OnRestore;
            Clear();
        }
        private Vector3 Feet => transform.position - (Vector3)_actor.Motor.Up * (_actor.Definition.BodySize.y * .5f);
        private void OnRestore() { Clear(); _restoring = true; _lastShot = -10f; }
        private void OnAction()
        {
            if (Profile == null || _restoring) return;
            // 受击或阶段切换打断时，取消还没有释放的蓄力，不能在下一招里继续闪。
            CancelFollowing();
            switch (_actor.AnimationName)
            {
                case "HammerPlayer": case "HammerGround": case "Throw": case "Slash":
                case "JumpShake": case "WingSpread": case "DashWindup": case "Roar": case "Magic":
                    Play(Profile.Charge, transform.position, Profile.CastSize * .55f, Profile.Accent,
                        0f, transform, Mathf.Max(.15f, _actor.AnimationDuration * _actor.Definition.ImpactFraction));
                    break;
                case "Angry": case "Split": case "Flip": case "Weak": case "Recover": case "Rewind":
                    Play(Profile.Cast, transform.position, Profile.CastSize, Color.white, duration: .9f);
                    break;
                case "Pop": case "Hide":
                    Play(Profile.Cast, Feet, Profile.CastSize * .6f, Color.white, duration: .6f);
                    break;
                case "Dead":
                    Play(Profile.Death, transform.position, Profile.CastSize * 1.2f, Color.white, duration: 1f);
                    BossCameraImpulse.Pulse(Profile.CameraImpulse, .22f);
                    break;
            }
        }
        private void OnFeedback(EnemyFeedback cue)
        {
            if (Profile == null || _restoring) return;
            float angle = Mathf.Atan2(cue.Direction.y, cue.Direction.x) * Mathf.Rad2Deg;
            switch (cue.Kind)
            {
                case EnemyFeedbackKind.Strike:
                    CancelFollowing();
                    bool slash = _actor.Definition.Kind == EnemyKind.BatBoss;
                    Play(slash ? Profile.Slash : Profile.Impact, cue.Position,
                        slash ? Profile.SlashSize : Profile.ImpactSize, Color.white, duration: slash ? .32f : .6f,
                        mirror: slash && _actor.Facing < 0f);
                    if (slash) Play(Profile.Shot, cue.Position, 1.4f, Color.white, duration: .12f);
                    BossCameraImpulse.Pulse(Profile.CameraImpulse, .12f);
                    break;
                case EnemyFeedbackKind.ProjectileSpawn:
                    if (cue.Subject is EnemyProjectile shot)
                    {
                        var view = shot.GetComponent<EnemyProjectileView>();
                        if (view == null) view = shot.gameObject.AddComponent<EnemyProjectileView>();
                        view.Configure(Profile);
                    }
                    // 一次散射只播一个发射爆发，毛团弹和分裂不会淹没角色。
                    if (Time.time - _lastShot < .1f) break;
                    _lastShot = Time.time;
                    CancelFollowing();
                    bool ground = _actor.AnimationName == "HammerGround";
                    Play(ground ? Profile.Impact : Profile.Shot, ground ? Feet : cue.Position,
                        ground ? Profile.ImpactSize : 1.4f, Color.white, duration: ground ? .65f : .24f);
                    if (ground)
                    {
                        // 同一作者的能量环压成地面透视，与向上的冲击粒子分层播放。
                        Play(Profile.Cast, Feet + (Vector3)_actor.Motor.Up * .08f,
                            Profile.ImpactSize * 1.1f, Color.white, duration: .5f, aspect: .22f);
                        BossCameraImpulse.Pulse(Profile.CameraImpulse, .18f);
                    }
                    break;
                case EnemyFeedbackKind.ProjectileImpact:
                    Play(Profile.Impact, cue.Position, Profile.ImpactSize * .4f, Color.white, angle, duration: .35f);
                    break;
                case EnemyFeedbackKind.BeamSpawn:
                    if (cue.Subject is EnemyBeam beam)
                    {
                        var view = beam.GetComponent<EnemyBeamView>();
                        if (view == null) view = beam.gameObject.AddComponent<EnemyBeamView>();
                        view.Configure(Profile);
                    }
                    break;
                case EnemyFeedbackKind.Hit:
                    Play(Profile.Shot, cue.Position, 2f, Color.white, angle, duration: .25f);
                    break;
                case EnemyFeedbackKind.RuleCast: case EnemyFeedbackKind.Summon:
                    CancelFollowing();
                    Play(Profile.Cast, cue.Position, Profile.CastSize, Color.white, duration: .9f);
                    break;
            }
        }
        public void Play(EnemyVfxClip clip, Vector3 position, float size, Color tint,
            float angle = 0f, Transform follow = null, float duration = -1f, bool mirror = false, float aspect = 1f)
        {
            if (clip == null || Profile == null) return;
            Slot slot = null;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null)
                {
                    var go = new GameObject("EnemyVfx");
                    go.transform.SetParent(transform, false);
                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sharedMaterial = Profile.Material; renderer.sortingOrder = 18;
                    _slots[i] = new Slot { Renderer = renderer };
                }
                if (_slots[i].Clip == null) { slot = _slots[i]; break; }
            }
            if (slot == null) return;
            slot.Clip = clip; slot.Age = 0f; slot.Follow = follow;
            slot.Position = position;
            slot.Speed = duration > 0f ? clip.Duration / duration : 1f;
            slot.Offset = follow == null ? Vector3.zero : position - follow.position;
            slot.Renderer.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
            slot.Renderer.transform.localScale = new Vector3(size, size * aspect, 1f);
            slot.Renderer.flipX = mirror;
            slot.Renderer.color = tint; slot.Renderer.sprite = clip.Frame(0f); slot.Renderer.enabled = true;
        }
        private void LateUpdate()
        {
            _restoring = false;
            if (TimeManager.Instance?.IsPaused == true || _actor.Status?.IsPaused == true) return;
            foreach (Slot slot in _slots)
            {
                if (slot == null || slot.Clip == null) continue;
                slot.Age += Time.deltaTime * slot.Speed;
                if (slot.Age >= slot.Clip.Duration) { slot.Clip = null; slot.Renderer.enabled = false; continue; }
                slot.Renderer.transform.position = slot.Follow != null ? slot.Follow.position + slot.Offset : slot.Position;
                slot.Renderer.sprite = slot.Clip.Frame(slot.Age);
            }
        }
        private void CancelFollowing()
        {
            foreach (Slot slot in _slots)
                if (slot != null && slot.Clip != null && slot.Follow != null)
                { slot.Clip = null; slot.Renderer.enabled = false; }
        }
        private void Clear()
        {
            foreach (Slot slot in _slots)
                if (slot != null) { slot.Clip = null; slot.Renderer.enabled = false; }
        }
    }
}
