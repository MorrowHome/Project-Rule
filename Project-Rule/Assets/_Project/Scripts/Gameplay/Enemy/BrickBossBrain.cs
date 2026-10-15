using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>距离选招，三个分裂阈值只触发一次；半血先 Angry，再 Split。</summary>
    public sealed class BrickBossBrain : EnemyBrain
    {
        private readonly bool[] _thresholds = new bool[3];
        private float _decideUntil;
        protected override string[] ActionNames => new[] { "Idle", "Walk", "HammerPlayer", "HammerGround", "Throw", "Hurt" };
        public BrickBossBrain(EnemyActor actor) : base(actor) { }
        protected override object CaptureExtra() => new object[] { (bool[])_thresholds.Clone(), _decideUntil };
        protected override void RestoreExtra(object state)
        {
            if (!(state is object[] data)) return;
            System.Array.Copy((bool[])data[0], _thresholds, 3); _decideUntil = (float)data[1];
        }
        protected override void TickGlobal()
        {
            if (Root.ActiveStateName != "Alive") return;
            float ratio = Actor.Status.Health / Config.Health;
            for (int i = 0; i < 3; i++)
            {
                if (_thresholds[i] || ratio > 0.75f - i * 0.25f) continue;
                _thresholds[i] = true;
                Transition(i == 1 ? "Angry" : "Split", i + 2);
                return;
            }
        }
        protected override void TickTransition()
        {
            if (TransitionName == "Split" && Impact(0.6f)) Actor.SpawnMinions(false, 3);
        }
        protected override void AfterTransition(string name)
        {
            if (name == "Angry") { Actor.CastRule(); Transition("Split", Phase); }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Idle") _decideUntil = Age + 0.55f;
        }
        protected override void TickAction(string name)
        {
            switch (name)
            {
                case "Idle":
                    if (Actor.Target == null || Age < _decideUntil) return;
                    if (Actor.Distance > 12f) { Go("Walk"); return; }
                    if (Age < NextAttack) return;
                    Go(Actor.Distance < Config.AttackRange ? "HammerPlayer" : Actor.Distance < 7f ? "HammerGround" : "Throw");
                    break;
                case "Walk":
                    WalkToTarget();
                    if (Actor.Distance < 10f) Go("Idle");
                    break;
                case "HammerPlayer":
                    if (Impact()) Actor.Attack(Config.AttackRange, true);
                    if (Pulses > 0 && Elapsed < Actor.AnimationDuration * 0.8f) Actor.Attack(Config.AttackRange);
                    if (Finished) RestAfterAttack();
                    break;
                case "HammerGround":
                    if (Impact())
                    {
                        Actor.Shoot(Vector2.left, 8f, 0f, 0.35f);
                        Actor.Shoot(Vector2.right, 8f, 0f, 0.35f);
                    }
                    if (Finished) RestAfterAttack();
                    break;
                case "Throw":
                    if (Impact()) Actor.Shoot(Actor.TowardTarget + Vector2.up * 0.35f, 9f, 0.45f, 0.4f);
                    if (Finished) RestAfterAttack();
                    break;
                case "Hurt": if (Finished) Go("Idle"); break;
            }
        }
    }
}
