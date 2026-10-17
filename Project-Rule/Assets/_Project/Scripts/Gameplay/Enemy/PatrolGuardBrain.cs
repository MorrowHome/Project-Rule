using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class PatrolGuardBrain : FieldMinionBrain
    {
        protected override string FirstAction => "Patrol";
        public PatrolGuardBrain(EnemyActor actor) : base(actor) { }
        protected override void EnterBehavior(string name)
        {
            if (name == "Patrol" || name == "Ignore" || name == "Chase") Actor.Cue("Walk", Actor.Duration("Walk"), true);
            if (name == "Attack") { ReadyAt = Age + Config.AttackCooldown; Actor.Motor.Stop(); }
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Attack")
            {
                TickMeleeHit();
                if (Finished) Go("Ignore");
                return;
            }
            if (name == "Ignore") { Patrol(); if (Elapsed >= Config.MinionIgnoreDuration) Go("Patrol"); return; }
            if (!CanPursue && Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return;
            if (Age >= ReadyAt && CanPursue && SeesTarget && TargetInMeleeRange) { Go("Attack"); return; }
            if (CanPursue)
            {
                if (name != "Chase") Go("Chase");
                Chase();
            }
            else
            {
                if (name != "Patrol") Go("Patrol");
                if (!ReturnHome()) Patrol();
            }
        }
        protected override bool HandleLethal()
        {
            if (Config.Kind == EnemyKind.StoneGuard) Actor.AreaAttack(Config.MinionExplosionRadius);
            return false;
        }
    }
}
