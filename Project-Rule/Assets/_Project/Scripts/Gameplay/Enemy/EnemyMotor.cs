using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Core.Rules;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>AI 只提交移动意图。这里独立处理重力、摩擦、碰撞和插值，不读取玩家输入。</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class EnemyMotor : MonoBehaviour, IKnockbackReceiver
    {
        public LayerMask SolidMask = 1 << 8;
        public Vector2 Intent { get; set; }
        public float SpeedMultiplier { get; set; } = 1f;
        public bool Grounded { get; private set; }
        public Vector2 Up { get; private set; } = Vector2.up;
        public Vector2 Velocity { get; private set; }
        private Vector2 _impulse;
        private Rigidbody2D _body;
        private EnemyActor _actor;
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[8];

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _actor = GetComponent<EnemyActor>();
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
            _body.useFullKinematicContacts = true;
        }

        public void Tick(float dt)
        {
            if (_actor.Definition == null || dt <= 0f) return;
            EnemyDefinition config = _actor.Definition;
            if (Physics2D.gravity.sqrMagnitude > 0.001f) Up = -Physics2D.gravity.normalized;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(SolidMask);
            int count = _body.Cast(-Up, filter, _hits, 0.06f);
            Grounded = false;
            for (int i = 0; i < count; i++)
                if (Vector2.Dot(_hits[i].normal, Up) > 0.55f) { Grounded = true; break; }

            Vector2 velocity = Velocity;
            float direction = Intent.x;
            var attributes = RuleSystem.Instance?.Attributes;
            if (Grounded && attributes != null && attributes.Values.FrictionInverted) direction = -direction;
            float acceleration = config.Flying ? Mathf.Max(35f, config.FlightAcceleration) : 35f;
            if (Grounded && attributes != null)
                acceleration *= Mathf.Max(0.02f, attributes.Values.FrictionScale);
            velocity.x = Mathf.MoveTowards(velocity.x, direction * config.MoveSpeed * SpeedMultiplier, acceleration * dt);
            if (config.Flying && !_actor.Defeated)
                velocity.y = Mathf.MoveTowards(velocity.y, Intent.y * config.MoveSpeed * SpeedMultiplier,
                    Mathf.Max(1f, config.FlightAcceleration) * dt);
            else
            {
                velocity += Physics2D.gravity * config.GravityScale * dt;
                if (Grounded && Vector2.Dot(velocity, Up) < 0f) velocity -= Up * Vector2.Dot(velocity, Up);
                velocity.y = Mathf.Clamp(velocity.y, -28f, 28f);
                // 白盒可直接追击；正式地图缺少支撑面时停止，不盲走下悬崖。
                if (Grounded && Mathf.Abs(direction) > 0.01f)
                {
                    Vector2 probe = _body.position - Up * (config.BodySize.y * 0.5f - 0.1f)
                        + Vector2.right * Mathf.Sign(direction) * (config.BodySize.x * 0.5f + 0.15f);
                    if (!Physics2D.Raycast(probe, -Up, 0.45f, SolidMask)) velocity.x = 0f;
                }
            }
            _impulse = Vector2.MoveTowards(_impulse, Vector2.zero, 24f * dt);
            var slide = _body.Slide(velocity + _impulse, dt, new Rigidbody2D.SlideMovement
            {
                surfaceUp = Up, surfaceSlideAngle = 60f, gravitySlipAngle = 0f,
                gravity = Vector2.zero, maxIterations = 6, useLayerMask = true,
                // 玩家身体只参与位移阻挡，不作为接地或悬崖探测的支撑面。
                layerMask = SolidMask | _actor.PlayerMask, useAttachedTriggers = false, useSimulationMove = true
            });
            if (slide.slideHit.collider != null)
            {
                Vector2 normal = slide.slideHit.normal;
                if (Mathf.Abs(normal.x) > 0.9f && velocity.x * normal.x < 0f) velocity.x = 0f;
                if (Vector2.Dot(normal, Up) < -0.5f && Vector2.Dot(velocity, Up) > 0f)
                    velocity -= Up * Vector2.Dot(velocity, Up);
            }
            Velocity = velocity;
        }

        public void AddImpulse(Vector2 impulse) => _impulse += impulse / Mathf.Max(0.1f, _body.mass);
        public void Stop() { Intent = Vector2.zero; Velocity = _impulse = Vector2.zero; SpeedMultiplier = 1f; }
        public Vector2 CaptureImpulse() => _impulse;
        public void Restore(Vector2 velocity, Vector2 impulse) { Velocity = velocity; _impulse = impulse; }
    }
}
