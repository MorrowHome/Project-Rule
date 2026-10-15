using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class RabbitBossBrain : EnemyBrain
    {
        private float _cycle, _magicCheck = 5f;
        private bool _flipped;
        private string _resume = "Pop";
        private float _resumeElapsed;
        private int _resumePulses;
        private uint _random = 3917;
        protected override string[] ActionNames => new[] { "Pop", "Throw", "Hide", "HideThrow", "FlipThrow", "Hurt" };
        protected override string FirstAction => "Pop";
        public RabbitBossBrain(EnemyActor actor) : base(actor) { }
        protected override object CaptureExtra() => new object[] { _cycle, _magicCheck, _flipped, _resume, _resumeElapsed, _random, _resumePulses };
        protected override void RestoreExtra(object state)
        {
            if (!(state is object[] data)) return;
            _cycle = (float)data[0]; _magicCheck = (float)data[1]; _flipped = (bool)data[2];
            _resume = (string)data[3]; _resumeElapsed = (float)data[4]; _random = (uint)data[5];
            _resumePulses = (int)data[6];
        }
        protected override void TickGlobal()
        {
            if (Root.ActiveStateName != "Alive") return;
            _cycle += Delta;
            if (!_flipped && Actor.Status.Health <= 50f)
            {
                _flipped = true;
                Transition("Flip", 2, 0.8f);
                return;
            }
            if (Age < _magicCheck || StateName == "Hurt" || !HasTarget) return;
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
                Actor.CastRule(); Go(_resume); Elapsed = _resumeElapsed;
                // 规则施法恢复原投掷进度，不能把已经发出的毛团再发一次。
                Pulses = _resumePulses;
            }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Throw" || name == "HideThrow") Actor.Cue(name, Config.RabbitThrowInterval);
            if (name == "FlipThrow") Actor.Cue(name, 0.5f);
        }
        protected override void TickAction(string name)
        {
            switch (name)
            {
                case "Pop": if (Finished) { _cycle = 0f; Go("Throw"); } break;
                case "Hide": if (Finished) { _cycle = 0f; Go("HideThrow"); } break;
                case "Throw":
                case "HideThrow":
                    if (_cycle >= Config.RabbitCycle) { Go(name == "Throw" ? "Hide" : "Pop"); return; }
                    if (HasTarget && CombatDistance <= RangedRange && Impact(0.35f))
                        Actor.Shoot(Actor.TowardTarget + Vector2.up * (TargetInMeleeRange ? 0.1f : 0.45f), 8f, 0.6f);
                    // 藏身时保持原位；出头时在出手后的恢复段拉开距离或追入射程。
                    if (name == "Throw" && (Pulses > 0 || CombatDistance > RangedRange))
                        PositionForThrow();
                    else Actor.Motor.Intent = Vector2.zero;
                    if (Finished) Go(name);
                    break;
                case "FlipThrow":
                    if (HasTarget && CombatDistance <= RangedRange && Impact(0.4f))
                        for (int i = -1; i <= 1; i++) Actor.Shoot(Actor.TowardTarget + Vector2.up * (i * 0.35f), 8f, 0.2f);
                    if (Pulses > 0 || CombatDistance > RangedRange) PositionForThrow();
                    if (Finished) Go(name);
                    break;
                case "Hurt": if (Finished) Go(_flipped ? "FlipThrow" : "Throw"); break;
            }
        }
        private void PositionForThrow()
        {
            if (!HasTarget) { Actor.Motor.Intent = Vector2.zero; return; }
            Actor.FaceTarget();
            float difference = CombatDistance - Mathf.Min(Config.PreferredDistance, RangedRange);
            float direction = Mathf.Abs(difference) > Config.SpacingTolerance ? Mathf.Sign(difference) : 0f;
            float movement = Actor.Facing * direction;
            float margin = Config.BodySize.x * 0.5f + 0.15f;
            if ((movement < 0f && Actor.transform.position.x <= Actor.ArenaX.x + margin)
                || (movement > 0f && Actor.transform.position.x >= Actor.ArenaX.y - margin)) movement = 0f;
            Actor.Motor.Intent = Vector2.right * movement;
            Actor.Motor.SpeedMultiplier = direction < 0f ? Config.RetreatSpeedMultiplier : 1f;
        }
    }
}
