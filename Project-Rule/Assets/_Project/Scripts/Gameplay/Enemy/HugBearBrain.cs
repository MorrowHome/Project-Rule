using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class HugBearBrain : FieldMinionBrain
    {
        private PlayerController _held;
        private float _damageAt;
        protected override string FirstAction => "Idle";
        public override bool MovementEnabled => base.MovementEnabled && StateName != "Hug";
        public HugBearBrain(EnemyActor actor) : base(actor) { }
        private void Hold()
        {
            if (HasTarget) _held = Actor.Target.GetComponentInParent<PlayerController>();
            if (_held != null) _held.SetMovementHeld(this, true);
        }
        public override void OnDisabled()
        {
            if (_held != null) _held.SetMovementHeld(this, false);
            _held = null;
        }
        protected override void EnterBehavior(string name)
        {
            OnDisabled();
            if (name == "Hug") { _damageAt = Age; Actor.Motor.Stop(); Hold(); }
            if (name == "Chase" || name == "Return") Actor.Cue("Walk", Actor.Duration("Walk"), true);
        }
        protected override void TickBehavior(string name)
        {
            switch (name)
            {
                case "Idle": if (SeesTarget) Go("Stand"); break;
                case "Stand": if (Finished) Go("Chase"); break;
                case "Chase":
                    if (!CanPursue && Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) break;
                    if (!CanPursue) { Go("Return"); break; }
                    Chase();
                    if (Age >= ReadyAt && TouchesTarget) Go("Hug");
                    break;
                case "Hug":
                    if (!TouchesTarget || !HasTarget || Elapsed >= Config.MinionHugDuration)
                    {
                        ReadyAt = Age + Config.AttackCooldown; Go("Chase"); break;
                    }
                    if (Age >= _damageAt)
                    {
                        _damageAt = Age + Config.MinionHugTickInterval;
                        Actor.AreaAttack(Config.AttackRange);
                    }
                    break;
                case "Return":
                    if (CanPursue && SeesTarget) { Go("Chase"); break; }
                    if (Config.MinionNavigationEnabled)
                    {
                        Actor.Navigation.MoveTo(Home, Home, Age);
                        if (Vector2.Distance(Actor.transform.position, Home) <= .25f) Go("Sit");
                        break;
                    }
                    float delta = Home.x - Actor.transform.position.x;
                    Actor.Motor.Intent = Mathf.Abs(delta) > .15f ? Vector2.right * Mathf.Sign(delta) : Vector2.zero;
                    Actor.FaceDirection(Actor.Motor.Intent);
                    if (Mathf.Abs(delta) <= .15f) Go("Sit");
                    else if (Elapsed >= Config.MinionIgnoreDuration && Actor.Motor.HasHorizontalObstacle(Mathf.Sign(delta))) Go("Sit");
                    break;
                case "Sit": if (Finished) Go("Idle"); break;
            }
        }
        public override void OnHit(float damage)
        {
            if (StateName == "Hug" && damage > 0f) { ReadyAt = Age + Config.AttackCooldown; Go("Chase"); }
            base.OnHit(damage);
        }
        protected override object CaptureBehavior() => _damageAt;
        protected override void RestoreBehavior(object state)
        {
            if (state is float value) _damageAt = value;
            if (StateName == "Hug") Hold();
        }
    }
}
