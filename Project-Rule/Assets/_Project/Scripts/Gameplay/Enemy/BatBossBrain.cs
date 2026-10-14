using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class BatBossBrain : EnemyBrain
    {
        private float _nextSummon, _nextPoint, _nextRule, _nextDash;
        private float _dashFacing;
        private bool _passiveShake;
        protected override string[] ActionNames => new[] { "Idle", "Fly", "Slash", "Shake", "Roar", "WingSpread", "Point", "Dash", "Hurt" };
        public BatBossBrain(EnemyActor actor) : base(actor) { _nextSummon = 4f; }
        protected override object CaptureExtra() => new object[] { _nextSummon, _nextPoint, _nextRule, _nextDash, _dashFacing, _passiveShake };
        protected override void RestoreExtra(object state)
        {
            if (!(state is object[] data)) return;
            _nextSummon = (float)data[0]; _nextPoint = (float)data[1]; _nextRule = (float)data[2];
            _nextDash = (float)data[3]; _dashFacing = (float)data[4]; _passiveShake = (bool)data[5];
        }
        public override float ClampHealth(float health)
            => Mathf.Max(Phase == 1 ? Config.Health * 2f / 3f : Phase == 2 ? Config.Health / 3f : 0f, health);
        protected override void TickGlobal()
        {
            if (Root.ActiveStateName != "Alive") return;
            float threshold = Phase == 1 ? Config.Health * 2f / 3f : Config.Health / 3f;
            if (Phase < 3 && Actor.Status.Health <= threshold + 0.01f) Transition("Weak", Phase + 1, Actor.Duration("Weak", 2.5f));
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Dash") { _dashFacing = Actor.Facing; Actor.Cue(name, 1.5f, true); }
            if (name == "Shake" && _passiveShake) Actor.Cue(name, 0.7f);
        }
        protected override void TickAction(string name)
        {
            switch (name)
            {
                case "Idle":
                    if (Actor.Target == null || Age < NextAttack) return;
                    if (Phase >= 3 && Age >= _nextDash) { _nextDash = Age + 10f; Go("Dash"); }
                    else if (Phase >= 2 && Actor.Status.Energy >= Config.Energy - 0.01f && Age >= _nextRule)
                    { _nextRule = Age + 8f; Go("WingSpread"); }
                    else if (Phase >= 2 && Actor.SummonCount > 0 && Age >= _nextPoint)
                    { _nextPoint = Age + 8f; Go("Point"); }
                    else if (Phase >= 2 && Age >= _nextSummon && Actor.SummonCount < 3)
                    { _nextSummon = Age + 12f; _nextPoint = Age + 3f; Go("Roar"); }
                    else if (Actor.Distance < Config.AttackRange) Go("Slash");
                    else if (Actor.Distance > 7f) Go("Fly");
                    else { _passiveShake = false; Go("Shake"); }
                    break;
                case "Fly":
                    Actor.FaceTarget();
                    Actor.Motor.Intent = Actor.TowardTarget;
                    if (Actor.Distance < Config.AttackRange || Elapsed > 3f) Go("Idle");
                    break;
                case "Slash":
                    if (Pulses < 3 && Elapsed >= Actor.AnimationDuration * (0.3f + Pulses * 0.23f))
                    { Actor.Attack(Config.AttackRange, true); Pulses++; }
                    if (Finished) RestAfterAttack();
                    break;
                case "Shake":
                    if (Elapsed >= 0.25f * (Pulses + 1) && Pulses < (_passiveShake ? 1 : 4))
                    {
                        for (int i = -2; i <= 2; i++) Actor.Shoot(Actor.TowardTarget + Vector2.up * i * 0.22f, 7f);
                        Pulses++;
                    }
                    if (Finished) RestAfterAttack();
                    break;
                case "Roar": if (Impact()) Actor.SpawnMinions(true, 3); if (Finished) RestAfterAttack(); break;
                case "WingSpread": if (Impact()) Actor.CastRule(); if (Finished) RestAfterAttack(); break;
                case "Point": if (Impact()) Actor.CommandSummons(); if (Finished) RestAfterAttack(); break;
                case "Dash":
                    Actor.Motor.Intent = Vector2.right * _dashFacing;
                    Actor.Motor.SpeedMultiplier = 5f;
                    Actor.Attack(3f);
                    if (Finished || Actor.transform.position.x > Actor.ArenaX.y || Actor.transform.position.x < Actor.ArenaX.x) RestAfterAttack();
                    break;
                case "Hurt": if (Finished) { _passiveShake = true; Go("Shake"); } break;
            }
        }
    }
}
