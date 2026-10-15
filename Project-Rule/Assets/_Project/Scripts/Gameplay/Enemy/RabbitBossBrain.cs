using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>快弹与高抛交替，近身藏身撤退，翻转后使用有限散射连发。</summary>
    public sealed class RabbitBossBrain : EnemyBrain
    {
        private float _cycle, _magicCheck = 5f, _nextBurrow;
        private bool _flipped, _canBurrow;
        private string _resume = "Pop";
        private float _resumeElapsed, _burrowX;
        private int _resumePulses, _pattern;
        private uint _random = 3917;
        private Vector2 _aim;
        protected override string[] ActionNames => new[] { "Pop", "Reposition", "Throw", "Hide", "Burrow", "HideThrow", "FlipThrow", "Hurt" };
        protected override string FirstAction => "Pop";
        public RabbitBossBrain(EnemyActor actor) : base(actor) { }
        private sealed class Data
        {
            public float Cycle, Magic, Burrow, ResumeElapsed, BurrowX;
            public bool Flipped, CanBurrow;
            public string Resume;
            public int ResumePulses, Pattern;
            public uint Random;
            public Vector2 Aim;
        }
        protected override object CaptureExtra() => new Data
        {
            Cycle = _cycle, Magic = _magicCheck, Burrow = _nextBurrow, ResumeElapsed = _resumeElapsed,
            BurrowX = _burrowX, Flipped = _flipped, CanBurrow = _canBurrow, Resume = _resume,
            ResumePulses = _resumePulses, Pattern = _pattern, Random = _random, Aim = _aim
        };
        protected override void RestoreExtra(object state)
        {
            if (!(state is Data data)) return;
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
                Transition("Flip", 2, .8f);
                return;
            }
            if (IsResting || StateName == "Rest" || !HasTarget) return;
            bool preparing = StateName == "Reposition" || StateName == "Throw" || StateName == "FlipThrow";
            if (!preparing) return;
            if (Age >= _nextBurrow && Pulses == 0
                && (CombatDistance < Mathf.Max(1f, Config.RabbitEscapeDistance) || _cycle >= Config.RabbitCycle))
            {
                Go("Hide");
                return;
            }
            // 只在投掷起手或重新站位时选择施法，不中断已经释放的散射或藏身。
            if (Age < _magicCheck || Pulses != 0 || (StateName != "Reposition" && Elapsed > .1f)) return;
            _magicCheck = Age + 5f;
            _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
            if (Actor.Status.Energy <= 50f || (_random & 1) == 0) return;
            _resume = StateName; _resumeElapsed = Elapsed; _resumePulses = Pulses;
            Transition("Magic", Phase, 1f);
        }
        protected override void AfterTransition(string name)
        {
            if (name == "Flip") { _cycle = 0f; Go("FlipThrow"); }
            else if (name == "Magic")
            {
                Actor.CastRule(); Go(_resume); Elapsed = _resumeElapsed; Pulses = _resumePulses;
            }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            switch (name)
            {
                case "Reposition": Actor.Cue("Pop", Actor.Duration("Pop", .6f)); break;
                case "Hide":
                    _cycle = 0f;
                    _nextBurrow = Age + Mathf.Max(1f, Config.RabbitBurrowCooldown);
                    float away = HasTarget ? Mathf.Sign(Actor.transform.position.x - Actor.Target.position.x) : -Actor.Facing;
                    if (Mathf.Abs(away) < .1f) away = -Actor.Facing;
                    _burrowX = ClampArenaX((HasTarget ? Actor.Target.position.x : Actor.transform.position.x)
                        + away * Mathf.Min(Config.PreferredDistance, RangedRange));
                    _canBurrow = Mathf.Abs(_burrowX - Actor.transform.position.x) > 1f;
                    break;
                case "Burrow":
                    Actor.Cue("HideThrow", Actor.Duration("HideThrow"), true);
                    break;
                case "Throw":
                case "HideThrow":
                    _aim = PredictTarget(.25f);
                    Actor.Cue(name, Mathf.Max(.5f, Config.RabbitThrowInterval));
                    break;
                case "FlipThrow":
                    _aim = PredictTarget(.25f);
                    Actor.Cue(name, .4f + Mathf.Clamp(Config.RabbitBurstCount, 1, 3) * Mathf.Max(.2f, Config.RabbitBurstInterval));
                    break;
            }
        }
        protected override void TickAction(string name)
        {
            switch (name)
            {
                case "Pop": if (Finished) Go(_flipped ? "FlipThrow" : "Throw"); break;
                case "Hide": if (Finished) Go(_canBurrow ? "Burrow" : "HideThrow"); break;
                case "Burrow":
                    float difference = _burrowX - Actor.transform.position.x;
                    Actor.Motor.Intent = Mathf.Abs(difference) > .3f ? Vector2.right * Mathf.Sign(difference) : Vector2.zero;
                    Actor.Motor.SpeedMultiplier = Mathf.Max(1f, Config.RabbitBurrowSpeedMultiplier);
                    if (!HasTarget || Mathf.Abs(difference) <= .3f || Elapsed >= Mathf.Max(.2f, Config.RabbitBurrowDuration)
                        || AtArenaEdge(Mathf.Sign(difference))) Go("Pop");
                    break;
                case "Reposition":
                    PositionForThrow();
                    if (Elapsed >= .6f || (HasTarget && Mathf.Abs(CombatDistance - Config.PreferredDistance) <= Config.SpacingTolerance))
                        Go(_flipped ? "FlipThrow" : "Throw");
                    break;
                case "Throw":
                case "HideThrow":
                    if (HasTarget && CombatDistance <= RangedRange && Impact(.35f))
                    {
                        bool lob = name == "HideThrow" || (_pattern & 1) != 0;
                        FireLob(lob ? .6f : .18f, lob ? 8f : 10f);
                    }
                    if (Finished) FinishVolley();
                    break;
                case "FlipThrow":
                    int count = Mathf.Clamp(Config.RabbitBurstCount, 1, 3);
                    if (HasTarget && CombatDistance <= RangedRange && Pulses < count
                        && Elapsed >= .35f + Pulses * Mathf.Max(.2f, Config.RabbitBurstInterval))
                    {
                        Vector2 direction = (_aim - (Vector2)Actor.transform.position).normalized;
                        for (int i = -1; i <= 1; i++) Actor.Shoot(direction + Actor.Motor.Up * (i * .32f), 9f, .2f);
                        Pulses++;
                    }
                    if (Finished) FinishVolley();
                    break;
                case "Hurt": if (Finished) Go("Reposition"); break;
            }
        }
        private void FireLob(float gravity, float speed)
        {
            Vector2 delta = _aim - (Vector2)Actor.transform.position;
            float time = Mathf.Clamp(Mathf.Abs(delta.x) / speed, .2f, 1.3f);
            Vector2 direction = delta - Physics2D.gravity * (gravity * time * time * .5f);
            Actor.Shoot(direction, speed, gravity);
        }
        private void FinishVolley()
        {
            _pattern++;
            RestAfterAttack("Reposition");
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
