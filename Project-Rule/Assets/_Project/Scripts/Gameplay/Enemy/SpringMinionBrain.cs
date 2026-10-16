using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class SpringMinionBrain : FieldMinionBrain
    {
        private float _hopAt;
        private bool _wasAirborne;
        protected override string FirstAction => "Patrol";
        public SpringMinionBrain(EnemyActor actor) : base(actor) { }
        protected override void EnterBehavior(string name)
        {
            if (name == "Attack") { ReadyAt = Age + Config.AttackCooldown; Actor.Motor.Stop(); }
            else Actor.Cue("Hop", Actor.Duration("Hop", .5f), true);
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Attack")
            {
                TickMeleeHit();
                if (Finished) Go("Ignore");
                return;
            }
            if (name == "Ignore" && Elapsed >= Config.MinionIgnoreDuration) { Go("Patrol"); return; }
            if (!CanPursue) { Actor.Motor.Intent = Vector2.zero; return; }
            // 空中保持本次起跳方向，不在玩家换边时突然折返。
            Actor.Motor.Intent = Vector2.right * Heading;
            if (!Actor.Motor.Grounded || Vector2.Dot(Actor.Motor.Velocity, Actor.Motor.Up) > .1f)
            {
                _wasAirborne = true;
                return;
            }
            if (_wasAirborne)
            {
                _wasAirborne = false;
                _hopAt = Age + Config.MinionHopInterval;
            }
            if (name == "Patrol" && SeesTarget && TargetInMeleeRange && Age >= ReadyAt) { Go("Attack"); return; }
            Actor.Motor.Intent = Vector2.zero;
            if (Age >= _hopAt)
            {
                Actor.FaceTarget(); Heading = Actor.Facing;
                if (Actor.Motor.HasHorizontalObstacle(Heading) && !TouchesTarget) return;
                Actor.Motor.Jump((Pattern++ & 1) == 0 ? Config.MinionHopHeight : Config.MinionHighHopHeight);
                Actor.Motor.Intent = Vector2.right * Heading;
            }
        }
        protected override object CaptureBehavior() => new object[] { _hopAt, _wasAirborne };
        protected override void RestoreBehavior(object state)
        {
            if (state is object[] data) { _hopAt = (float)data[0]; _wasAirborne = (bool)data[1]; }
        }
    }
}
