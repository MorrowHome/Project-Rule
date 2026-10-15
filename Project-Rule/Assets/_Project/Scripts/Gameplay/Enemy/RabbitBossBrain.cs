using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>快弹与高抛交替，近身藏身撤退，翻转后飞行换侧并使用有限散射。</summary>
    public sealed class RabbitBossBrain : EnemyBrain
    {
        private float _cycle, _magicCheck = 5f, _nextBurrow;
        private bool _flipped, _canBurrow, _lob;
        private uint _restoreRevision;
        private int _airSide;
        private Vector2 _airDestination;
        private string _resume = "Pop";
        private float _resumeElapsed, _burrowX;
        private int _resumePulses, _pattern;
        private uint _random = 3917;
        private Vector2 _aim;
        protected override string[] ActionNames => new[] { "Pop", "Reposition", "Throw", "Hide", "Burrow", "HideThrow", "AirReposition", "FlipThrow", "Hurt" };
        public override bool IsFlying => base.IsFlying || (_flipped && Phase >= 2 && !Actor.Defeated);
        protected override string FirstAction => "Pop";
        public RabbitBossBrain(EnemyActor actor) : base(actor) { }
        private sealed class Data
        {
            public float Cycle, Magic, Burrow, ResumeElapsed, BurrowX;
            public bool Flipped, CanBurrow, Lob;
            public int AirSide;
            public Vector2 AirDestination;
            public string Resume;
            public int ResumePulses, Pattern;
            public uint Random;
            public Vector2 Aim;
        }
        protected override object CaptureExtra() => new Data
        {
            Cycle = _cycle, Magic = _magicCheck, Burrow = _nextBurrow, ResumeElapsed = _resumeElapsed,
            BurrowX = _burrowX, Flipped = _flipped, CanBurrow = _canBurrow, Resume = _resume,
            ResumePulses = _resumePulses, Pattern = _pattern, Random = _random, Aim = _aim,
            Lob = _lob, AirSide = _airSide, AirDestination = _airDestination
        };
        protected override void RestoreExtra(object state)
        {
            if (!(state is Data data)) return;
            _restoreRevision++;
            _lob = data.Lob; _airSide = data.AirSide; _airDestination = data.AirDestination;
            _cycle = data.Cycle; _magicCheck = data.Magic; _nextBurrow = data.Burrow;
            _resumeElapsed = data.ResumeElapsed; _burrowX = data.BurrowX; _flipped = data.Flipped;
            _canBurrow = data.CanBurrow; _resume = data.Resume; _resumePulses = data.ResumePulses;
            _pattern = data.Pattern; _random = data.Random; _aim = data.Aim;
        }
        protected override void TickGlobal()
        {
            if (Root.ActiveStateName != "Alive") return;
            _cycle += Delta;
            if (!_flipped && Actor.Status.Health <= 50f)
            {
                _flipped = true;
                Transition("Flip", 2, Actor.Duration("Flip", .8f));
                return;
            }
            if (IsResting || StateName == "Rest" || !HasTarget) return;
            bool preparing = StateName == "Reposition" || StateName == "Throw" || StateName == "FlipThrow"
                || (StateName == "Pop" && Elapsed >= .5f);
            if (!preparing) return;
            if (Age >= _nextBurrow && Pulses == 0
                && (CombatDistance < Mathf.Max(1f, Config.RabbitEscapeDistance) || _cycle >= Config.RabbitCycle))
            {
                Go(_flipped ? "AirReposition" : "Hide");
                return;
            }
            // 只在投掷起手或重新站位时选择施法，不中断已经释放的散射或藏身。
            if (Age < _magicCheck || CombatDistance > RangedRange || Pulses != 0
                || (StateName != "Reposition" && Elapsed > .1f)) return;
            _magicCheck = Age + 8f;
            _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
            if (Actor.Status.Energy <= 50f || (_random & 1) == 0) return;
            _resume = StateName; _resumeElapsed = Elapsed; _resumePulses = Pulses;
            Transition("Magic", Phase, _flipped ? Actor.Duration("Flight", 1f) : Actor.Duration("Magic", 1f));
        }
        protected override void AfterTransition(string name)
        {
            if (name == "Flip") { _cycle = 0f; Go("AirReposition"); }
            else if (name == "Magic")
            {
                uint revision = _restoreRevision;
                Actor.CastRule();
                if (revision != _restoreRevision) return;
                Go(_resume); Elapsed = _resumeElapsed; Pulses = _resumePulses;
            }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            switch (name)
            {
                case "Rest":
                    if (_flipped) Actor.Cue("Flight", Actor.Duration("Flight"), true);
                    break;
                case "Reposition": Actor.Cue("Walk", Actor.Duration("Walk", .6f), true); break;
                case "Hide":
                    _cycle = 0f;
                    _nextBurrow = Age + Mathf.Max(1f, Config.RabbitBurrowCooldown);
                    float away = HasTarget ? Mathf.Sign(Actor.transform.position.x - Actor.Target.position.x) : -Actor.Facing;
                    if (Mathf.Abs(away) < .1f) away = -Actor.Facing;
                    _burrowX = ClampArenaX((HasTarget ? Actor.Target.position.x : Actor.transform.position.x)
                        + away * Mathf.Min(Config.PreferredDistance, RangedRange));
                    // 墙角只能朝玩家移动时改为反击，不倒退进对方身上。
                    _canBurrow = away * (_burrowX - Actor.transform.position.x) > 1f;
                    break;
                case "Burrow":
                    Actor.Cue("Burrow", Actor.Duration("Burrow"), true);
                    break;
                case "AirReposition":
                    _cycle = 0f;
                    Actor.Cue("Flight", Actor.Duration("Flight", .5f), true);
                    ChooseAirPoint();
                    _nextBurrow = Age + Mathf.Max(1f, Config.RabbitBurrowCooldown) + Config.RabbitAirMoveDuration;
                    break;
                case "Throw":
                case "HideThrow":
                    float height = HasTarget ? Vector2.Dot(Actor.Target.position - Actor.transform.position, Actor.Motor.Up) : 0f;
                    _lob = name == "HideThrow" || CombatDistance < Config.PreferredDistance * .7f
                        || height > Config.BodySize.y * .35f || (_pattern % 3 == 2 && CombatDistance < Config.PreferredDistance + 1f);
                    _aim = PredictThrowTarget(.25f);
                    Actor.Cue(name, Mathf.Max(Actor.Duration(name, .5f), Config.RabbitThrowInterval));
                    break;
                case "FlipThrow":
                    _aim = PredictThrowTarget(.25f);
                    Actor.Cue(name, .4f + Mathf.Clamp(Config.RabbitBurstCount, 1, 3) * Mathf.Max(.2f, Config.RabbitBurstInterval), true);
                    break;
            }
        }
        protected override void TickAction(string name)
        {
            switch (name)
            {
                case "Pop":
                    if (Finished) Go(HasTarget && CombatDistance <= RangedRange ? "Throw" : "Reposition");
                    break;
                case "Hide": if (Finished) Go(_canBurrow ? "Burrow" : "HideThrow"); break;
                case "Burrow":
                    float difference = _burrowX - Actor.transform.position.x;
                    Actor.Motor.Intent = Mathf.Abs(difference) > .3f ? Vector2.right * Mathf.Sign(difference) : Vector2.zero;
                    Actor.Motor.SpeedMultiplier = Mathf.Max(1f, Config.RabbitBurrowSpeedMultiplier);
                    if (!HasTarget || Mathf.Abs(difference) <= .3f || Elapsed >= Mathf.Max(.2f, Config.RabbitBurrowDuration)
                        || AtArenaEdge(Mathf.Sign(difference)))
                    {
                        // 退场与冒出的长动画不消耗接下来允许玩家逼近的间隔。
                        _nextBurrow = Age + Actor.Duration("Pop") + Mathf.Max(1f, Config.RabbitBurrowCooldown);
                        Go("Pop");
                    }
                    break;
                case "Reposition":
                    PositionForThrow();
                    if (!HasTarget) break;
                    if (Elapsed >= Mathf.Max(.6f, Config.RabbitRepositionDuration)
                        || Mathf.Abs(CombatDistance - Config.PreferredDistance) <= Config.SpacingTolerance)
                        Go(CombatDistance <= RangedRange ? "Throw" : "Reposition");
                    break;
                case "Throw":
                case "HideThrow":
                    // 先跟踪，再给玩家一段固定锁定窗口，仍在原画第十二帧附近出手。
                    float releaseTime = (name == "HideThrow" ? 41f : 12f) / 8f;
                    if (Pulses == 0 && Elapsed < releaseTime - Mathf.Max(.1f, Config.RabbitAimLockLead))
                        _aim = PredictThrowTarget(.25f);
                    if (HasTarget && CombatDistance <= RangedRange
                        && Impact(Mathf.Min(1f, releaseTime / Actor.AnimationDuration)))
                    {
                        FireLob(_lob ? .6f : .18f, _lob ? 8f : 10f);
                    }
                    if (Finished) FinishVolley();
                    break;
                case "AirReposition":
                    if (!HasTarget) { Actor.Motor.Stop(); break; }
                    Vector2 offset = _airDestination - (Vector2)Actor.transform.position;
                    Actor.FaceTarget();
                    Actor.Motor.Intent = offset.sqrMagnitude > .16f ? offset.normalized : Vector2.zero;
                    Actor.Motor.SpeedMultiplier = Mathf.Max(1f, Config.RabbitAirSpeedMultiplier);
                    if (offset.sqrMagnitude <= .16f || Elapsed >= Mathf.Max(.5f, Config.RabbitAirMoveDuration))
                    {
                        if (CombatDistance <= RangedRange) Go("FlipThrow");
                        else RestAfterAttack("AirReposition");
                    }
                    break;
                case "FlipThrow":
                    int count = Mathf.Clamp(Config.RabbitBurstCount, 1, 3);
                    if (HasTarget && CombatDistance <= RangedRange && Pulses < count
                        && Elapsed >= .35f + Pulses * Mathf.Max(.2f, Config.RabbitBurstInterval))
                    {
                        Vector2 direction = (_aim - (Vector2)Actor.transform.position).normalized;
                        float spread = Mathf.Clamp(Config.RabbitFanAngle, 5f, 40f);
                        float sweep = ((Pulses + _pattern) & 1) == 0 ? -spread * .25f : spread * .25f;
                        for (int i = -1; i <= 1; i++)
                        {
                            float angle = (sweep + i * spread) * Mathf.Deg2Rad;
                            Vector2 shot = new Vector2(direction.x * Mathf.Cos(angle) - direction.y * Mathf.Sin(angle),
                                direction.x * Mathf.Sin(angle) + direction.y * Mathf.Cos(angle));
                            Actor.Shoot(shot, 9f, .2f, visualIndex: 2);
                        }
                        Pulses++;
                        _aim = PredictThrowTarget(.2f);
                    }
                    if (Finished) FinishVolley();
                    break;
                case "Hurt": if (Finished) Go(_flipped ? "AirReposition" : "Reposition"); break;
            }
        }
        private void FireLob(float gravity, float speed)
        {
            Vector2 delta = _aim - (Vector2)Actor.transform.position;
            float time = Mathf.Clamp(Mathf.Abs(delta.x) / speed, _lob ? .65f : .2f, 1.3f);
            // 按飞行时间解出完整初速度；再压回固定总速会丢失重力补偿，尤其在重力增强时提前砸地。
            Vector2 velocity = delta / time - Physics2D.gravity * (gravity * time * .5f);
            Actor.Shoot(velocity, velocity.magnitude, gravity, visualIndex: _lob ? 1 : 0);
        }
        private Vector2 PredictThrowTarget(float lead)
        {
            if (!HasTarget) return Actor.transform.position;
            Vector2 offset = PredictTarget(lead) - (Vector2)Actor.Target.position;
            var collider = Actor.Target.GetComponent<Collider2D>() ?? Actor.Target.GetComponentInParent<Collider2D>();
            Vector2 center = collider != null && collider.enabled ? (Vector2)collider.bounds.center : (Vector2)Actor.Target.position;
            if (offset.sqrMagnitude < .0001f) return center;
            Vector2 size = collider != null && collider.enabled ? (Vector2)collider.bounds.size : Vector2.one * .55f;
            // 用目标身体扫掠预判位移，落地或撞墙后就停止外推，不把锁定点放进地形。
            RaycastHit2D hit = Physics2D.BoxCast(center, size, 0f, offset.normalized,
                offset.magnitude, Actor.Motor.SolidMask);
            return center + offset.normalized * (hit.collider != null ? Mathf.Max(0f, hit.distance - .02f) : offset.magnitude);
        }
        private void FinishVolley()
        {
            _pattern++;
            RestAfterAttack(_flipped ? "AirReposition" : "Reposition");
        }
        private void ChooseAirPoint()
        {
            if (!HasTarget) { _airDestination = Actor.transform.position; return; }
            Vector2 target = Actor.Target.position;
            if (_airSide == 0) _airSide = Actor.transform.position.x < target.x ? -1 : 1;
            else _airSide = -_airSide;
            float distance = Mathf.Min(Config.PreferredDistance, RangedRange * .75f);
            float x = ClampArenaX(target.x + _airSide * distance);
            if (Mathf.Abs(x - target.x) < distance * .6f)
            {
                _airSide = -_airSide;
                x = ClampArenaX(target.x + _airSide * distance);
            }
            float margin = Config.BodySize.y * .5f + .15f;
            float y = (target + Actor.Motor.Up * Mathf.Max(1f, Config.RabbitAirHeight)).y;
            y = Actor.ArenaY.y - Actor.ArenaY.x > margin * 2f
                ? Mathf.Clamp(y, Actor.ArenaY.x + margin, Actor.ArenaY.y - margin)
                : (Actor.ArenaY.x + Actor.ArenaY.y) * .5f;
            _airDestination = new Vector2(x, y);
        }
        private void PositionForThrow()
        {
            if (!HasTarget) { Actor.Motor.Intent = Vector2.zero; return; }
            Actor.FaceTarget();
            float difference = CombatDistance - Mathf.Min(Config.PreferredDistance, RangedRange);
            float direction = Mathf.Abs(difference) > Config.SpacingTolerance ? Mathf.Sign(difference) : 0f;
            float movement = Actor.Facing * direction;
            if (AtArenaEdge(movement)) movement = 0f;
            Actor.Motor.Intent = Vector2.right * movement;
            Actor.Motor.SpeedMultiplier = direction < 0f ? Config.RetreatSpeedMultiplier : 1f;
        }
    }
}
