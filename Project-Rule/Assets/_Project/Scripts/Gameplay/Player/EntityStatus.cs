using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 实体状态组件实现（玩家用）。
    /// </summary>
    /// <remarks>
    /// <para>敌人可复用本类，或另写一个实现 —— 只要满足 <see cref="IEntityStatus"/> 即可。</para>
    /// <para><b>冷却用 <c>Time.deltaTime</c> 计时</b>，但<b>不能靠它自动冻结</b>：
    /// 策划案禁止用 <c>Time.timeScale = 0</c> 做暂停（取消暂停时会瞬移穿碰撞体），
    /// 所以暂停期间 <c>deltaTime</c> 照常流逝。冻结改由 <see cref="SetPaused"/> 显式控制 ——
    /// 暂停的持有者（P1 的时间管理器）负责调用它。</para>
    /// </remarks>
    public class EntityStatus : MonoBehaviour, IEntityStatus
    {
        // ==================================================================
        //  序列化字段
        // ==================================================================

        [Header("能量")]
        [SerializeField] private float _energyMax = 100f;
        [SerializeField] private float _energy    = 100f;

        [Header("生命")]
        [SerializeField] private float _healthMax = 100f;
        [SerializeField] private float _health    = 100f;

        [Header("持有物")]
        [SerializeField] private int _throwableCount;

        private object _heldItem;

        // ==================================================================
        //  状态
        // ==================================================================

        public float Energy    => _energy;
        public float EnergyMax => _energyMax;
        public float Health    => _health;
        public float HealthMax => _healthMax;

        public object HeldItem       => _heldItem;
        public int    ThrowableCount => _throwableCount;

        // ==================================================================
        //  暂停
        // ==================================================================

        /// <summary>暂停中。冷却与其它按帧推进的状态都不前进。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// 设置暂停状态。由暂停的持有者调用 —— 拼装界面暂停时 P1 的时间管理器会走到这里。
        /// </summary>
        /// <remarks>不在 <see cref="IEntityStatus"/> 契约里：接口冻结时暂停还假定由
        /// <c>Time.timeScale = 0</c> 自动完成，那条路已被策划案否掉。<b>待团队确认后并入冻结文档。</b></remarks>
        public void SetPaused(bool paused) => IsPaused = paused;

        // 冷却：按 属性 × 关系 索引，值 = 剩余秒数
        private readonly Dictionary<(AttributeId, RelationId), float> _cooldowns = new();
        private readonly List<(AttributeId, RelationId)> _cooldownKeys = new();

        // ==================================================================
        //  事件
        // ==================================================================

        public event Action<float, float> OnEnergyChanged;
        public event Action<float, float> OnHealthChanged;
        public event Action<AttributeId, RelationId, float> OnCooldownStarted;

        /// <summary>新生成实体的初始生命/能量配置；只在首次初始化调用，回溯仍使用 ApplyData。</summary>
        public void InitializeVitals(float healthMax, float energyMax)
        {
            _healthMax = Mathf.Max(1f, healthMax);
            _energyMax = Mathf.Max(0f, energyMax);
            _health = _healthMax;
            _energy = _energyMax;
            OnHealthChanged?.Invoke(_health, _healthMax);
            OnEnergyChanged?.Invoke(_energy, _energyMax);
        }

        // ==================================================================
        //  能量
        // ==================================================================

        public bool TryConsumeEnergy(float amount)
        {
            if (amount <= 0f) return true;
            if (_energy < amount) return false;      // 不足：不扣除

            _energy -= amount;
            OnEnergyChanged?.Invoke(_energy, _energyMax);
            return true;
        }

        public void RestoreEnergy(float amount)
        {
            if (amount <= 0f) return;

            _energy = Mathf.Min(_energy + amount, _energyMax);
            OnEnergyChanged?.Invoke(_energy, _energyMax);
        }

        // ==================================================================
        //  生命
        // ==================================================================

        /// <summary>施加生命变化（负数为受伤）。供战斗系统调用。</summary>
        /// <remarks>不在 <see cref="IEntityStatus"/> 契约里 —— 受伤走 <c>IDamageable</c>，
        /// 由承受方自己的实现调到这里。</remarks>
        public void ApplyHealthDelta(float delta)
        {
            if (Mathf.Approximately(delta, 0f)) return;

            _health = Mathf.Clamp(_health + delta, 0f, _healthMax);
            OnHealthChanged?.Invoke(_health, _healthMax);
        }

        // ==================================================================
        //  冷却
        // ==================================================================

        public bool IsOnCooldown(AttributeId attr, RelationId rel)
            => _cooldowns.TryGetValue((attr, rel), out var t) && t > 0f;

        public void BeginCooldown(AttributeId attr, RelationId rel, float seconds)
        {
            if (seconds <= 0f) return;               // 0 = 无冷却

            _cooldowns[(attr, rel)] = seconds;
            OnCooldownStarted?.Invoke(attr, rel, seconds);
        }

        public float GetCooldownRemaining(AttributeId attr, RelationId rel)
            => _cooldowns.TryGetValue((attr, rel), out var t) ? Mathf.Max(t, 0f) : 0f;

        private void Update()
        {
            if (IsPaused || RuleGame.Infrastructure.Time.TimeManager.Instance?.TimeMechanicsEnabled == false) return; // 动画时钟照常，冷却计时暂停
            if (_cooldowns.Count == 0) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 先拷 key 再改，避免遍历字典时修改
            _cooldownKeys.Clear();
            foreach (var key in _cooldowns.Keys) _cooldownKeys.Add(key);

            foreach (var key in _cooldownKeys)
            {
                float left = _cooldowns[key] - dt;
                if (left <= 0f) _cooldowns.Remove(key);
                else            _cooldowns[key] = left;
            }
        }

        // ==================================================================
        //  快照 / 存档桥接
        // ==================================================================

        public EntityStatusData ToData() => new EntityStatusData
        {
            Energy         = _energy,
            Health         = _health,
            HeldItem       = _heldItem,
            ThrowableCount = _throwableCount,
        };

        public void ApplyData(EntityStatusData data)
        {
            if (data == null) return;

            _energy         = Mathf.Clamp(data.Energy, 0f, _energyMax);
            _health         = Mathf.Clamp(data.Health, 0f, _healthMax);
            _heldItem       = data.HeldItem;
            _throwableCount = data.ThrowableCount;

            _cooldowns.Clear();     // 冷却不进存档 / 快照，回滚后不该带着「未来的冷却」

            OnEnergyChanged?.Invoke(_energy, _energyMax);
            OnHealthChanged?.Invoke(_health, _healthMax);
        }
    }
}
