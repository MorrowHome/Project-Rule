using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class PlushSealBrain : FieldMinionBrain
    {
        private Vector2 _iceAim;
        private uint _random = 7139;
        private float _wanderAt, _slideWindup;
        private bool _sliding;
        protected override string FirstAction => "Wander";
        public PlushSealBrain(EnemyActor actor) : base(actor) { ReadyAt = Config.MinionSkillInterval; }
        protected override void EnterBehavior(string name)
        {
            if (name == "Wander") Actor.Cue("Walk", Actor.Duration("Walk"), true);
            else
            {
                ReadyAt = Age + Config.MinionSkillInterval;
                if (name == "Slide")
                {
                    Heading = Actor.Facing; _slideWindup = Actor.Duration("Slide") * .35f; _sliding = false;
                    Actor.Motor.Stop(); Actor.Cue("Idle", _slideWindup);
                }
                if (name == "BellySlap") _iceAim = PredictTarget(.2f);
            }
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Wander")
            {
                if (Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return;
                if (!CanPursue && ReturnHome()) { CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f); return; }
                if (Age >= _wanderAt)
                {
                    _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
                    Heading = (_random & 1) == 0 ? -1f : 1f;
                    _wanderAt = Age + 1.3f + (_random % 100) * .01f;
                }
                if (CanPursue && SeesTarget)
                {
                    Actor.FaceTarget();
                    if (Actor.Distance > Config.AttackRange * 1.25f) Chase();
                    else Actor.Motor.Stop();
                }
                else Patrol();
                CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
                if (CanPursue && SeesTarget && Age >= ReadyAt)
                {
                    Actor.FaceTarget();
                    int skill = Pattern % 3;
                    float reach = Config.MoveSpeed * Config.MinionDashMultiplier * Config.MinionSlideDuration;
                    bool slidePossible = Mathf.Abs(Actor.Target.position.y - Actor.transform.position.y) <= Config.BodySize.y
                        && Mathf.Abs(Actor.Target.position.x - Actor.transform.position.x) <= reach
                        && !Actor.Motor.HasHorizontalObstacle(Actor.Facing);
                    if (skill == 0 && !slidePossible) skill = 1;
                    if (skill == 1 && !Actor.CanCastRule) skill = 2;
                    Pattern = skill + 1;
                    Go(skill == 0 ? "Slide" : skill == 1 ? "Howl" : "BellySlap");
                }
                return;
            }
            if (name == "Slide")
            {
                // 先压低身体蓄力，再按锁定方向滑行；不即时贴身命中，也不追踪拐弯。
                if (Elapsed < _slideWindup) { Actor.Motor.Intent = Vector2.zero; return; }
                if (!_sliding)
                {
                    _sliding = true;
                    Actor.Cue("Slide", Config.MinionSlideDuration, true);
                }
                Actor.Motor.Intent = Vector2.right * Heading;
                Actor.Motor.SpeedMultiplier = Config.MinionDashMultiplier;
                if (TouchesTarget) { Actor.AreaAttack(Config.AttackRange); Go("Wander"); }
                else if (Elapsed >= _slideWindup + Config.MinionSlideDuration || Actor.Motor.HasHorizontalObstacle(Heading)) Go("Wander");
                return;
            }
            if (Impact())
            {
                if (name == "Howl") Actor.CastRule();
                if (name == "BellySlap" && HasTarget)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector2 origin = _iceAim + Vector2.right * i + Actor.Motor.Up * 4f;
                        origin.x = Mathf.Clamp(origin.x, Actor.ArenaX.x + .3f, Actor.ArenaX.y - .3f);
                        origin.y = Mathf.Clamp(origin.y, Actor.ArenaY.x + .3f, Actor.ArenaY.y - .3f);
                        Actor.Shoot(-Actor.Motor.Up, 3f, 1f, .3f, origin: origin);
                    }
                }
            }
            if (Finished) Go("Wander");
        }
        protected override object CaptureBehavior() => new object[] { _iceAim, _random, _wanderAt, _slideWindup, _sliding };
        protected override void RestoreBehavior(object state)
        {
            if (state is object[] data) { _iceAim = (Vector2)data[0]; _random = (uint)data[1]; _wanderAt = (float)data[2]; _slideWindup = (float)data[3]; _sliding = (bool)data[4]; }
        }
    }
}
