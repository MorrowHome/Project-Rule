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
        [Tooltip("仅阻挡身体移动，不作为地面或悬崖支撑。默认 Enemy 层，包含 Boss 与召唤小兵。")]
        public LayerMask BodyBlockMask = 1 << 9;
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
            bool flying = _actor.IsFlying;
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
            float acceleration = flying ? Mathf.Max(35f, config.FlightAcceleration) : 35f;
            if (Grounded && attributes != null)
                acceleration *= Mathf.Max(0.02f, attributes.Values.FrictionScale);
            velocity.x = Mathf.MoveTowards(velocity.x, direction * config.MoveSpeed * SpeedMultiplier, acceleration * dt);
            if (flying && !_actor.Defeated)
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
            var movement = new Rigidbody2D.SlideMovement
            {
                surfaceUp = Up, surfaceSlideAngle = 60f, gravitySlipAngle = 0f,
                // 只在贴着当前重力方向的支撑面时吸附。默认世界向下的 Anchor 会把反重力实体拉回旧地面。
                surfaceAnchor = !flying && Grounded && Vector2.Dot(velocity, Up) <= .01f
                    ? -Up * .05f : Vector2.zero,
                gravity = Vector2.zero, maxIterations = 6, useLayerMask = true,
                // 玩家身体只参与位移阻挡，不作为接地或悬崖探测的支撑面。
                layerMask = SolidMask | _actor.PlayerMask | BodyBlockMask, useAttachedTriggers = false, useSimulationMove = true
            };
            var slide = _body.Slide(velocity + _impulse, dt, movement);
            if (slide.slideHit.collider != null)
            {
                Vector2 normal = slide.slideHit.normal;
                if (Mathf.Abs(normal.x) > 0.9f && velocity.x * normal.x < 0f) velocity.x = 0f;
                if (Vector2.Dot(normal, Up) < -0.5f && Vector2.Dot(velocity, Up) > 0f)
                    velocity -= Up * Vector2.Dot(velocity, Up);
            }
            Velocity = velocity;
        }

        /// <summary>检查真实身体阻挡；不能把摩擦导致的低速误判成撞到了玩家。</summary>
        public bool HasHorizontalObstacle(float direction)
        {
            float side = Mathf.Sign(direction);
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(SolidMask | BodyBlockMask | _actor.PlayerMask);
            // Rigidbody.Cast 排除自身；只接受迎面接触，不把脚下地面当成横向墙壁。
            int count = _body.Cast(Vector2.right * side, filter, _hits, .035f);
            for (int i = 0; i < count; i++)
                if (_hits[i].normal.x * side < -.5f) return true;
            if (!Grounded) return false;
            EnemyDefinition config = _actor.Definition;
            Vector2 probe = _body.position - Up * (config.BodySize.y * .5f - .1f)
                + Vector2.right * side * (config.BodySize.x * .5f + .15f);
            return !Physics2D.Raycast(probe, -Up, .45f, SolidMask);
        }
        public void AddImpulse(Vector2 impulse) => _impulse += impulse / Mathf.Max(0.1f, _body.mass);
        public void Stop() { Intent = Vector2.zero; Velocity = _impulse = Vector2.zero; SpeedMultiplier = 1f; }
        public Vector2 CaptureImpulse() => _impulse;
        public void Restore(Vector2 velocity, Vector2 impulse)
        {
            Velocity = velocity; _impulse = impulse;
            // 全局重力随快照恢复后，立即更新派生朝上方向，避免原画和弱点仍用未来时间线的方向。
            if (Physics2D.gravity.sqrMagnitude > .001f) Up = -Physics2D.gravity.normalized;
        }
    }
}
