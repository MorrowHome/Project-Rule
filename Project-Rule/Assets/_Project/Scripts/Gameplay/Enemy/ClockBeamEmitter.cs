using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>指针缓慢旋转，激光按独立的停火/预警/短扫射节奏工作，不读取玩家位置。</summary>
    public sealed class ClockBeamEmitter
    {
        private readonly EnemyActor _actor;
        private readonly float[] _remaining = new float[3];
        private readonly float[] _cooldowns = new float[3];
        private readonly EnemyBeam[] _beams = new EnemyBeam[3];
        private int _unlockedHands = 1;
        private EnemyDefinition Config => _actor.Definition;

        public ClockBeamEmitter(EnemyActor actor)
        {
            _actor = actor;
            _unlockedHands = Mathf.Clamp(actor.Brain?.Phase ?? 1, 1, 3);
            for (int i = 0; i < _remaining.Length; i++)
            {
                _remaining[i] = RotationPeriod(i);
                _cooldowns[i] = HandInterval(i);
            }
        }
        public float HandInterval(int hand) => Mathf.Max(.3f, hand == 0 ? Config.ClockHourInterval
            : hand == 1 ? Config.ClockMinuteInterval : Config.ClockSecondInterval);
        public float RotationPeriod(int hand) => Mathf.Max(1f, hand == 0 ? Config.ClockHourRotationPeriod
            : hand == 1 ? Config.ClockMinuteRotationPeriod : Config.ClockSecondRotationPeriod);
        public bool IsHandActive(int hand) => hand >= 0 && hand < _unlockedHands;
        public float HandProgress(int hand) => IsHandActive(hand)
            ? Mathf.Clamp01(1f - _remaining[hand] / RotationPeriod(hand)) : 0f;
        public float HandRotation(int hand) => -HandProgress(hand) * 360f;
        public Vector2 HandDirection(int hand, float secondsAhead = 0f) => _actor.transform.TransformDirection(
            Quaternion.Euler(0f, 0f, HandRotation(hand) - Mathf.Max(0f, secondsAhead) * 360f / RotationPeriod(hand)) * Vector3.up);
        public float FireDuration(int hand) => Mathf.Min(Mathf.Max(.05f, Config.ClockBeamDuration),
            RotationPeriod(hand) * Mathf.Clamp(Config.ClockBeamMaxSweepAngle, 30f, 180f) / 360f);

        public void Tick(float delta)
        {
            if (delta <= 0f || _actor.Defeated || _actor.Status.Health <= 0f
                || _actor.Status.IsPaused || TimeManager.Instance?.IsPaused == true) return;
            int previousHands = _unlockedHands;
            int phase = Mathf.Clamp(_actor.Brain?.Phase ?? 1, 1, 3);
            while (_unlockedHands < phase)
            {
                _remaining[_unlockedHands] = RotationPeriod(_unlockedHands);
                _cooldowns[_unlockedHands] = HandInterval(_unlockedHands);
                _unlockedHands++;
            }
            for (int i = 0; i < _unlockedHands; i++)
            {
                // 新针从十二点开始；周期余量保留，掉帧也不会改变扫射转速。
                if (i < previousHands)
                {
                    float interval = RotationPeriod(i);
                    _remaining[i] -= delta;
                    if (_remaining[i] <= .00001f)
                        _remaining[i] = interval - Mathf.Max(0f, -_remaining[i]) % interval;
                }
                EnemyBeam beam = _beams[i];
                if (beam != null && beam.gameObject.activeSelf && beam.Source == _actor && beam.ClockHand == i) continue;
                if (i >= previousHands) continue;
                // 时间失效关闭冷却机制：指针动画仍旋转，已有激光收尾，但不再启动新一束。
                if (TimeManager.Instance?.TimeMechanicsEnabled == false) continue;
                // 停火计时从上一束光消失后开始，不能让短周期把预警与扫射拼成常驻光束。
                _cooldowns[i] -= delta;
                if (_cooldowns[i] <= .00001f)
                {
                    _beams[i] = EnemyBeam.SpawnClockHand(_actor, i,
                        Mathf.Max(.05f, Config.ClockBeamWidth + i * Config.ClockBeamWidthStep), Mathf.Max(.3f, Config.ClockBeamWarning));
                    _cooldowns[i] = HandInterval(i);
                }
            }
        }
        private sealed class Data
        {
            public float[] Remaining;
            public float[] Cooldowns;
            public EnemyBeam[] Beams;
            public int UnlockedHands;
        }
        public object Capture() => new Data
        {
            Remaining = (float[])_remaining.Clone(), Cooldowns = (float[])_cooldowns.Clone(),
            Beams = (EnemyBeam[])_beams.Clone(), UnlockedHands = _unlockedHands
        };
        public void Restore(object state)
        {
            if (!(state is Data data)) return;
            for (int i = 0; i < _beams.Length; i++)
            {
                // 回到解锁/生成之前，移除未来时间线多出的光束；已有光束由自己的快照恢复预警和伤害冷却。
                if (_beams[i] != null && _beams[i] != data.Beams[i] && _beams[i].Source == _actor)
                    _beams[i].gameObject.SetActive(false);
                _beams[i] = data.Beams[i];
            }
            System.Array.Copy(data.Remaining, _remaining, _remaining.Length);
            System.Array.Copy(data.Cooldowns, _cooldowns, _cooldowns.Length);
            _unlockedHands = data.UnlockedHands;
        }
    }
}
