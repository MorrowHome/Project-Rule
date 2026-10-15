using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>指针作为并行攻击时钟；眩晕窗口只攻击发条，失败仅恢复 Boss 自身血量。</summary>
    public sealed class ClockBossBrain : EnemyBrain
    {
        private readonly float[] _hands = { 0.8f, 1.5f, 2.2f };
        private float _nextHour;
        private float _chargeFacing;
        protected override string[] ActionNames => new[] { "Idle", "Walk", "JumpShake", "Charge", "Hurt" };
        public override bool DamageLocked => Root.ActiveStateName == "Transition";
        public ClockBossBrain(EnemyActor actor) : base(actor) { _nextHour = actor.Definition.ClockHourInterval; }
        protected override object CaptureExtra() => new object[] { (float[])_hands.Clone(), _nextHour, _chargeFacing };
        protected override void RestoreExtra(object state)
        {
            if (!(state is object[] data)) return;
            System.Array.Copy((float[])data[0], _hands, 3); _nextHour = (float)data[1]; _chargeFacing = (float)data[2];
        }
        public override float ClampHealth(float health)
        {
            float floor = Phase == 1 ? Config.Health * 2f / 3f : Phase == 2 ? Config.Health / 3f : 0f;
            return Mathf.Max(floor, health);
        }
        protected override void TickGlobal()
        {
            if (Root.ActiveStateName != "Alive") return;
            if (Phase == 1 && Actor.Status.Health <= Config.Health * 2f / 3f + 0.01f) Phase = 2;
            if (Phase == 2 && Actor.Status.Health <= Config.Health / 3f + 0.01f)
            {
                Actor.WeakPoint?.Open(Config.ClockCoreHealth);
                Transition("Stun", 2, Config.ClockCoreWindow);
                return;
            }
            // 三根指针独立推进，行走/攻击恢复不会重启秒针，也不会串成一次长动作。
            for (int i = 0; i < Phase; i++)
            {
                _hands[i] -= Delta;
                if (_hands[i] > 0f) continue;
                _hands[i] += 2f + i;
                float rotationPeriod = Config.ClockHourInterval * (i + 1);
                Actor.Beam(90f - Age / rotationPeriod * 360f, 0.1f + i * 0.1f);
            }
            if (HasTarget && CombatDistance <= RangedRange && Age >= _nextHour && Age >= NextAttack
                && (StateName == "Idle" || StateName == "Walk"))
            {
                _nextHour = Age + Config.ClockHourInterval;
                Go("JumpShake");
            }
        }
        protected override void TickTransition()
        {
            if (TransitionName != "Stun") return;
            if (Actor.WeakPoint != null && Actor.WeakPoint.Broken)
            {
                Actor.WeakPoint.Close();
                Transition("Recover", 3, 0.6f);
            }
        }
        protected override void AfterTransition(string name)
        {
            if (name == "Stun")
            {
                Actor.WeakPoint?.Close();
                Transition("Rewind", 2, 0.9f);
            }
            else if (name == "Rewind")
            {
                Actor.Status.ApplyHealthDelta(Config.Health * 0.6f - Actor.Status.Health);
                _hands[0] = 0.8f; _hands[1] = 1.5f;
            }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Charge") { _chargeFacing = Actor.Facing; Actor.Cue("Charge", 3f, true); }
        }
        protected override void TickAction(string name)
        {
            switch (name)
            {
                case "Idle":
                case "Walk":
                    if (TargetInMeleeRange && Age >= NextAttack)
                    {
                        Go("JumpShake");
                        return;
                    }
                    KeepDistance("Walk", Mathf.Min(Config.PreferredDistance, RangedRange));
                    break;
                case "JumpShake": if (Finished) { Actor.CastRule(); Go("Charge"); } break;
                case "Charge":
                    Actor.Motor.SpeedMultiplier = 3.5f;
                    if (Actor.transform.position.x > Actor.ArenaX.y) _chargeFacing = -1f;
                    if (Actor.transform.position.x < Actor.ArenaX.x) _chargeFacing = 1f;
                    Actor.Motor.Intent = Vector2.right * _chargeFacing;
                    Actor.Attack(2.5f);
                    if (Finished) RestAfterAttack();
                    break;
                case "Hurt": if (Finished) Go("Idle"); break;
            }
        }
    }
}
