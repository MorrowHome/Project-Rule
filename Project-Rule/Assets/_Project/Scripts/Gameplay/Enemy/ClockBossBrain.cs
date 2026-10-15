using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>仅管理身体冲撞、受击和发条弱点；独立激光由 EnemyActor 的 ClockBeamEmitter 推进。</summary>
    public sealed class ClockBossBrain : EnemyBrain
    {
        private float _nextCharge = 2.4f;
        private float _chargeFacing;
        protected override string[] ActionNames => new[] { "Idle", "Walk", "JumpShake", "Charge", "Hurt" };
        public override bool DamageLocked => Root.ActiveStateName == "Transition";
        public ClockBossBrain(EnemyActor actor) : base(actor) { }
        // 保留旧表现/工具的查询入口，计时数据与推进均由独立发射器负责。
        public float HandInterval(int hand) => Actor.ClockBeams.HandInterval(hand);
        public float HandProgress(int hand) => Actor.ClockBeams.HandProgress(hand);
        protected override object CaptureExtra() => new object[] { _nextCharge, _chargeFacing };
        protected override void RestoreExtra(object state)
        {
            if (!(state is object[] data)) return;
            _nextCharge = (float)data[0]; _chargeFacing = (float)data[1];
        }
        public override float ClampHealth(float health)
        {
            float floor = Phase == 1 ? Config.Health * 2f / 3f : Phase == 2 ? Config.Health / 3f : 0f;
            return Mathf.Max(floor, health);
        }
        protected override void TickGlobal()
        {
            if (Root.ActiveStateName != "Alive") return;
            if (Phase == 1 && Actor.Status.Health <= Config.Health * 2f / 3f + 0.01f)
            {
                Phase = 2;
            }
            if (Phase == 2 && Actor.Status.Health <= Config.Health / 3f + 0.01f)
            {
                Actor.WeakPoint?.Open(Config.ClockCoreHealth);
                Transition("Stun", 2, Config.ClockCoreWindow);
                return;
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
                _nextCharge = Age + 1f;
            }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "JumpShake")
            {
                float direction = PredictTarget(.25f).x - Actor.transform.position.x;
                _chargeFacing = Mathf.Abs(direction) > .1f ? Mathf.Sign(direction) : Actor.Facing;
                _nextCharge = Age + Mathf.Max(1f, Config.ClockChargeCooldown);
                Actor.FaceDirection(Vector2.right * _chargeFacing);
            }
            if (name == "Charge")
            {
                Actor.FaceDirection(Vector2.right * _chargeFacing);
                Actor.Cue("Charge", Actor.Duration("Charge", 3f), true);
            }
        }
        protected override void TickAction(string name)
        {
            switch (name)
            {
                case "Idle":
                case "Walk":
                    if (HasTarget && CombatDistance <= RangedRange && Age >= NextAttack && Age >= _nextCharge
                        && Mathf.Abs(Actor.Target.position.y - Actor.transform.position.y) <= Config.BodySize.y * .6f)
                    {
                        Go("JumpShake");
                        return;
                    }
                    KeepDistance("Walk", Mathf.Min(Config.PreferredDistance, RangedRange));
                    break;
                case "JumpShake":
                    // 乱跑是从当前位置起步的移动招式，不在这里隐式释放会回溯自身位置的全局规则。
                    if (Finished) Go("Charge");
                    break;
                case "Charge":
                    Actor.Motor.SpeedMultiplier = Mathf.Max(1f, Config.ClockChargeSpeedMultiplier);
                    Actor.Motor.Intent = Vector2.right * _chargeFacing;
                    Actor.Attack(2.5f);
                    bool blocked = Elapsed > .2f && Mathf.Abs(Actor.Motor.Velocity.x) < .2f;
                    if (Elapsed >= Mathf.Max(.2f, Config.ClockChargeDuration) || AtArenaEdge(_chargeFacing) || blocked)
                        RestAfterAttack();
                    break;
                case "Hurt": if (Finished) Go("Idle"); break;
            }
        }
    }
}
