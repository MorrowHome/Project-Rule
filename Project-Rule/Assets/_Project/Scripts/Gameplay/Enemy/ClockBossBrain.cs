using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>瞄准激光与错位封路，预警后沿锁定路线冲撞；保留发条弱点与失败回溯。</summary>
    public sealed class ClockBossBrain : EnemyBrain
    {
        private readonly float[] _hands = { 0.8f, 1.5f, 2.2f };
        private float _nextHour;
        private float _chargeFacing;
        private int _beamVolleys;
        private float _aimUntil;
        private uint _restoreRevision;
        protected override string[] ActionNames => new[] { "Idle", "Walk", "JumpShake", "Charge", "Hurt" };
        public override bool DamageLocked => Root.ActiveStateName == "Transition";
        public ClockBossBrain(EnemyActor actor) : base(actor) { _nextHour = 2.4f; }
        protected override object CaptureExtra() => new object[] { (float[])_hands.Clone(), _nextHour, _chargeFacing, _beamVolleys, _aimUntil };
        protected override void RestoreExtra(object state)
        {
            if (!(state is object[] data)) return;
            // 这是调用栈失效标记，不随快照回退；施法期间也可能同步恢复当前 Brain。
            _restoreRevision++;
            System.Array.Copy((float[])data[0], _hands, 3); _nextHour = (float)data[1]; _chargeFacing = (float)data[2];
            _beamVolleys = (int)data[3];
            _aimUntil = (float)data[4];
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
            // 休息时冻结指针出手时钟，结束后继续剩余间隔，不补发积压的激光。
            if (IsResting || StateName == "Rest") return;
            // 预警、冲撞与普通受击期间不叠加新激光；远处先靠近有效作战范围。
            if (!HasTarget || CombatDistance > RangedRange + 2f || (StateName != "Idle" && StateName != "Walk")) return;
            if (Age < _aimUntil) return;
            if (TargetInMeleeRange && Age >= NextAttack && Age >= _nextHour)
            {
                Go("JumpShake");
                return;
            }
            bool volley = false;
            for (int i = 0; i < Phase; i++)
            {
                if (_beamVolleys >= Mathf.Max(1, Config.ClockBeamVolleysBeforeRest)) break;
                _hands[i] -= Delta;
                if (_hands[i] > 0f) continue;
                _hands[i] += 2f + i;
                FireHand(i);
                volley = true;
            }
            if (volley) { _beamVolleys++; return; }
            if (_beamVolleys >= Mathf.Max(1, Config.ClockBeamVolleysBeforeRest)
                && (StateName == "Idle" || StateName == "Walk"))
            {
                if (Age >= _nextHour && CombatDistance <= RangedRange
                    && Mathf.Abs(Actor.Target.position.y - Actor.transform.position.y) <= Config.BodySize.y * .6f)
                    Go("JumpShake");
                else RestAfterAttack();
                return;
            }
        }
        private void FireHand(int hand)
        {
            Vector2 origin = Actor.BeamOrigin != null ? (Vector2)Actor.BeamOrigin.position : (Vector2)Actor.transform.position;
            Vector2 aim = PredictTarget(.2f) - origin;
            float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
            float spread = Mathf.Clamp(Config.ClockBeamSpread, 5f, 35f);
            float warning = Mathf.Max(.3f, Config.ClockBeamWarning);
            _aimUntil = Age + warning + .35f;
            Actor.Motor.Stop();
            if (hand == 2)
            {
                Actor.Beam(angle - spread, .18f, warning);
                Actor.Beam(angle + spread, .18f, warning);
            }
            else Actor.Beam(angle + (hand == 1 ? -Actor.Facing * spread : 0f), .1f + hand * .08f, warning);
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
                _beamVolleys = 0; _nextHour = Age + 1f;
            }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Rest") _beamVolleys = 0;
            if (name == "JumpShake")
            {
                float direction = PredictTarget(.25f).x - Actor.transform.position.x;
                _chargeFacing = Mathf.Abs(direction) > .1f ? Mathf.Sign(direction) : Actor.Facing;
                _nextHour = Age + Mathf.Max(1f, Config.ClockChargeCooldown);
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
                    // 激光预警和判定期间站定，避免发射原点随走位滑动。
                    if (Age < _aimUntil) { Actor.Motor.Intent = Vector2.zero; return; }
                    if (TargetInMeleeRange && Age >= NextAttack && Age >= _nextHour)
                    {
                        Go("JumpShake");
                        return;
                    }
                    KeepDistance("Walk", Mathf.Min(Config.PreferredDistance, RangedRange));
                    break;
                case "JumpShake":
                    if (Finished)
                    {
                        uint revision = _restoreRevision;
                        Actor.CastRule();
                        // 时间反转已恢复位置、动作和锁定方向，旧响铃回调不能再覆盖恢复后的状态。
                        if (revision == _restoreRevision) Go("Charge");
                    }
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
