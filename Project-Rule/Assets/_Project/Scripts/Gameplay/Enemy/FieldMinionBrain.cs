using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>关卡小怪共享巡逻、感知、临时散架和快照；具体决策由各类 Brain 负责。</summary>
    public abstract class FieldMinionBrain : EnemyBrain
    {
        private static readonly string[] States = { "Idle", "Patrol", "Attack", "Ignore", "Hurt", "Down", "Rebirth",
            "Armed", "Explode", "Hop", "Pop", "Walk", "Hide", "Fly", "Stand", "Chase", "Hug", "Return", "Sit", "Wander", "Slide", "Howl", "BellySlap" };
        protected Vector2 Home;
        protected float Heading = 1f, ReadyAt;
        protected int Pattern;
        private float _staggerDamage, _lastHit, _lastSeenAt = float.NegativeInfinity;
        private Vector2 _lastSeen;
        private Transform _cachedTarget;
        private bool _pursuing, _returning;
        private RuleGame.Gameplay.Player.EntityStatus _targetStatus;
        protected Collider2D TargetBody;
        protected readonly Collider2D Body;
        protected override string[] ActionNames => States;
        protected FieldMinionBrain(EnemyActor actor) : base(actor)
        {
            Home = actor.transform.position;
            Body = actor.GetComponent<Collider2D>();
        }
        protected bool Dormant => StateName == "Down" || StateName == "Rebirth";
        public override bool BodyEnabled => !Actor.Defeated && !Dormant && !(Config.Kind == EnemyKind.ClockMouse && StateName == "Idle");
        public override bool MovementEnabled => BodyEnabled && StateName != "Armed" && StateName != "Explode";
        public override bool DamageLocked => Dormant || !BodyEnabled || StateName == "Armed" || StateName == "Explode";
        public override bool ContactDamageEnabled => BodyEnabled && Config.Kind == EnemyKind.ClockMouse && StateName == "Walk";
        protected bool LiveTarget
        {
            get
            {
                if (!HasTarget) return false;
                if (_cachedTarget != Actor.Target)
                {
                    _cachedTarget = Actor.Target;
                    TargetBody = Actor.Target.GetComponent<Collider2D>() ?? Actor.Target.GetComponentInParent<Collider2D>();
                    _targetStatus = Actor.Target.GetComponentInParent<RuleGame.Gameplay.Player.EntityStatus>();
                }
                return _targetStatus == null || _targetStatus.Health > 0f;
            }
        }
        protected bool SeesTarget
        {
            get
            {
                if (!LiveTarget || Actor.Distance > Config.MinionSenseRange
                    || Vector2.Distance(Actor.Target.position, Home) > Config.MinionLeashRange
                    || Vector2.Distance(Actor.transform.position, Home) > Config.MinionLeashRange) return false;
                return !Physics2D.Linecast(Actor.transform.position, Actor.Target.position, Actor.Motor.SolidMask);
            }
        }
        protected bool TouchesTarget
        {
            get
            {
                if (!LiveTarget || !Body.enabled) return false;
                return TargetBody != null && TargetBody.enabled
                    ? Body.Distance(TargetBody).distance <= Mathf.Max(.08f, Config.ContactPadding)
                    : Actor.Distance <= Config.BodySize.x * .5f + .4f;
            }
        }
        protected override void TickGlobal()
        {
            if (SeesTarget)
            {
                _lastSeenAt = Age; _lastSeen = Actor.Target.position;
                _pursuing = true; _returning = false;
            }
            else if (_pursuing && !CanPursue)
            {
                _pursuing = false; _returning = true; Actor.Navigation?.Repath();
            }
        }
        protected bool CanPursue => LiveTarget && Age - _lastSeenAt <= Config.MinionTargetMemory
            && Vector2.Distance(_lastSeen, Home) <= Config.MinionLeashRange
            && Vector2.Distance(Actor.transform.position, Home) <= Config.MinionLeashRange;
        protected Vector2 LastSeenPosition => _lastSeen;
        protected bool ReturnHome()
        {
            if (!_returning) return false;
            if (Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return true;
            if (Vector2.Distance(Actor.transform.position, Home) <= .25f) { _returning = false; return false; }
            if (Config.MinionNavigationEnabled)
                Actor.Navigation.MoveTo(Home, Home, Age, Config.Kind == EnemyKind.SpringToy, Config.MinionHopHeight);
            else
            {
                float side = Mathf.Sign(Home.x - Actor.transform.position.x);
                Actor.Motor.Intent = Actor.Motor.HasHorizontalObstacle(side) ? Vector2.zero : Vector2.right * side;
                Actor.FaceDirection(Actor.Motor.Intent);
            }
            return true;
        }
        protected void TrackMeleeWindup()
        {
            if (Elapsed <= Actor.AnimationDuration * Config.MinionAimLockFraction && SeesTarget) Actor.FaceTarget();
        }
        protected void TickMeleeHit()
        {
            TrackMeleeWindup();
            // 一个短命中窗容许玩家在出手中进入范围；同一挥击仍只结算一次。
            if (Elapsed >= Actor.AnimationDuration * Config.ImpactFraction
                && Elapsed <= Actor.AnimationDuration * Mathf.Min(1f, Config.ImpactFraction + .2f))
                Actor.Attack(Config.AttackRange);
        }
        protected void Patrol(float distance = -1f)
        {
            float radius = distance > 0f ? distance : Config.MinionPatrolDistance;
            float x = Actor.transform.position.x;
            if ((x >= Mathf.Min(Home.x + radius, ClampArenaX(Home.x + radius)) && Heading > 0f)
                || (x <= Mathf.Max(Home.x - radius, ClampArenaX(Home.x - radius)) && Heading < 0f)
                || Actor.Motor.HasHorizontalObstacle(Heading)) Heading = -Heading;
            Actor.FaceDirection(Vector2.right * Heading);
            Actor.Motor.Intent = Vector2.right * Heading;
        }
        protected void Chase()
        {
            if (Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return;
            if (!CanPursue) { Actor.Motor.Intent = Vector2.zero; return; }
            if (Config.MinionNavigationEnabled)
            {
                Actor.Navigation.MoveTo(_lastSeen, Home, Age);
                return;
            }
            float offset = _lastSeen.x - Actor.transform.position.x;
            Actor.FaceDirection(Vector2.right * offset);
            Actor.Motor.Intent = Mathf.Abs(offset) > .12f ? Vector2.right * Mathf.Sign(offset) : Vector2.zero;
            if (Actor.Motor.HasHorizontalObstacle(Actor.Facing) && !TouchesTarget) Actor.Motor.Intent = Vector2.zero;
        }
        protected override bool TryHandleDeath()
        {
            if (!Config.MinionReassembles) return HandleLethal();
            if (!Dormant) Go("Down");
            return true;
        }
        protected virtual bool HandleLethal() => false;
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Down") { Actor.Motor.Stop(); Actor.Cue("Dead", Actor.Duration("Dead", .6f)); }
            else if (name == "Rebirth") Actor.Cue("Rebirth", Actor.Duration("Rebirth", .8f));
            else if (name != "Hurt") EnterBehavior(name);
        }
        protected virtual void EnterBehavior(string name) { }
        protected override void TickAction(string name)
        {
            if (name == "Down") { if (Elapsed >= Mathf.Max(Actor.AnimationDuration, Config.MinionRespawnDelay)) Go("Rebirth"); return; }
            if (name == "Rebirth")
            {
                if (Finished)
                {
                    Actor.Status.ApplyHealthDelta(Config.Health - Actor.Status.Health);
                    ReadyAt = Age + .3f;
                    Go(FirstAction);
                }
                return;
            }
            if (name == "Hurt") { if (Finished) Go(FirstAction); return; }
            TickBehavior(name);
        }
        protected abstract void TickBehavior(string name);
        public override void OnHit(float damage)
        {
            if (damage <= 0f) return;
            if (Actor.Status.Health <= 0f) { OnDisabled(); return; }
            if (Dormant || StateName == "Hurt" || StateName == "Ignore" || Age < NextHurt) return;
            // 已经出手的攻击不被轻击逐帧取消；玩家仍可靠重击在前摇打断。
            bool committed = StateName == "Attack" || StateName == "Slide" || StateName == "Howl" || StateName == "BellySlap";
            if (committed && Elapsed >= Actor.AnimationDuration * Config.MinionAimLockFraction) return;
            if (Age - _lastHit > Config.StaggerResetDelay) _staggerDamage = 0f;
            _lastHit = Age;
            _staggerDamage += damage;
            if (_staggerDamage < Mathf.Max(1f, Config.MinionStaggerThreshold)) return;
            _staggerDamage = 0f;
            OnDisabled();
            Go("Hurt");
        }
        private sealed class Data
        {
            public Vector2 Home;
            public float Heading, Ready;
            public float StaggerDamage, LastHit, LastSeenAt;
            public Vector2 LastSeen;
            public int Pattern;
            public object Behavior;
            public bool Pursuing, Returning;
        }
        protected override object CaptureExtra() => new Data { Home = Home, Heading = Heading, Ready = ReadyAt, Pattern = Pattern,
            StaggerDamage = _staggerDamage, LastHit = _lastHit, LastSeenAt = _lastSeenAt, LastSeen = _lastSeen,
            Pursuing = _pursuing, Returning = _returning, Behavior = CaptureBehavior() };
        protected override void RestoreExtra(object state)
        {
            if (!(state is Data data)) return;
            OnDisabled(); Home = data.Home; Heading = data.Heading; ReadyAt = data.Ready; Pattern = data.Pattern;
            _staggerDamage = data.StaggerDamage; _lastHit = data.LastHit; _lastSeenAt = data.LastSeenAt; _lastSeen = data.LastSeen;
            _pursuing = data.Pursuing; _returning = data.Returning;
            RestoreBehavior(data.Behavior);
        }
        protected virtual object CaptureBehavior() => null;
        protected virtual void RestoreBehavior(object state) { }
    }
}
