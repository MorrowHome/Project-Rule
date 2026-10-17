using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class PlushBirdBrain : FieldMinionBrain
    {
        private Vector2 _aim;
        protected override string FirstAction => "Fly";
        public PlushBirdBrain(EnemyActor actor) : base(actor) { }
        public override bool IsFlying => !Actor.Defeated;
        protected override void EnterBehavior(string name)
        {
            if (name == "Attack") { _aim = PredictTarget(.2f); ReadyAt = Age + Config.AttackCooldown; }
            else Actor.Cue("Fly", Actor.Duration("Fly", .5f), true);
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Attack")
            {
                FlyWithSpacing();
                if (Impact() && HasTarget) Actor.Shoot(_aim - (Vector2)Actor.transform.position, Config.MinionProjectileSpeed);
                if (Finished) Go("Fly");
                return;
            }
            FlyWithSpacing();
            if (CanPursue && Age >= ReadyAt && SeesTarget && Actor.Distance <= Config.AttackRange) Go("Attack");
        }
        private void FlyWithSpacing()
        {
            if (CanPursue && SeesTarget)
            {
                Actor.FaceTarget();
                float gap = Mathf.Abs(Actor.Target.position.x - Actor.transform.position.x);
                float preferred = Mathf.Min(Config.MinionPreferredRange, Config.AttackRange * .8f);
                float side = gap < preferred - .4f ? -Actor.Facing : gap > preferred + .4f ? Actor.Facing : 0f;
                if (side != 0f && (AtArenaEdge(side) || Actor.Motor.HasHorizontalObstacle(side))) side = 0f;
                Actor.Motor.Intent = Vector2.right * side;
            }
            else Patrol();
            Actor.Motor.Intent = new Vector2(Actor.Motor.Intent.x, Mathf.Clamp(Home.y - Actor.transform.position.y, -1f, 1f));
        }
        protected override object CaptureBehavior() => _aim;
        protected override void RestoreBehavior(object state) { if (state is Vector2 aim) _aim = aim; }
    }
}
