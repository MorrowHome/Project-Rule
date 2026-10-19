using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class PatrolGuardBrain : FieldMinionBrain
    {
        protected override string FirstAction => "Patrol";
        public PatrolGuardBrain(EnemyActor actor) : base(actor) { }
        protected override void EnterBehavior(string name)
        {
            if (name == "Patrol" || name == "Chase") Actor.Cue("Walk", Actor.Duration("Walk"), true);
            else if (name == "Ignore") Actor.Cue("Idle", Actor.Duration("Idle"), true);
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
            if (name == "Ignore")
            {
                // 攻击后的 3 秒只是「不再出手」，不该变成「转身往回走」。
                // 有目标就站定面对，没目标才巡逻；否则玩家贴在脸上也会看到它后退。
                if (HasTarget) { Actor.FaceTarget(); Actor.Motor.Stop(); }
                else Patrol();
                if (Elapsed >= Config.MinionIgnoreDuration) Go("Patrol");
                return;
            }
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
