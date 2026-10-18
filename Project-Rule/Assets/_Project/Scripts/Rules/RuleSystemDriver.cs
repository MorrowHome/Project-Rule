using UnityEngine;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 规则系统的启动器与心跳。
    /// </summary>
    /// <remarks>
    /// 挂在常驻场景对象上（或做成跨场景的 Prefab）。
    /// 负责两件事：<b>初始化单例</b>、<b>每帧 Tick</b>。
    /// </remarks>
    [DefaultExecutionOrder(-100)]   // 比其他脚本先 Awake
    public class RuleSystemDriver : MonoBehaviour
    {
        [Tooltip("规则目录资产，放在 Assets/_Project/Data/Rules/")]
        [SerializeField] private RuleCatalog _catalog;

        /// <summary>
        /// 时间管理器是否由本组件创建。
        /// </summary>
        /// <remarks>常驻宿主（GameRoot）先建时本组件只是复用，销毁时就不能连带把全局实例清掉。</remarks>
        private bool _ownsTimeManager;

        /// <summary>
        /// 当前持有全局单例的那一份驱动。
        /// </summary>
        /// <remarks>
        /// ⚠️ 为什么需要它：<see cref="OnDestroy"/> 会把 <c>RuleSystem</c> / 注册表 / 快照全部清空（为了下次进
        /// Play 或切场景重新初始化）。但如果场景里同时存在两份驱动（例如 Boot 里那份还在，关卡又实例化了一份），
        /// 先销毁的那份会把**正在使用**的单例一起清掉 —— 表现就是「规则系统没初始化、所有伤害打不出来」，
        /// 而 Console 里只有 <c>[RuleAffectedBody] RuleSystem 尚未初始化</c> 这种二手线索。
        /// 只有持有者自己销毁时才允许 Reset。
        /// </remarks>
        private static RuleSystemDriver _owner;

        /// <summary>
        /// 设置暂停状态。转发给 <see cref="TimeManager"/>。
        /// </summary>
        /// <remarks>
        /// <para>暂停的<b>唯一真相</b>在 <c>ITimeManager</c>（2026-10-12 决策 3）：
        /// 它同时做两件事 —— <c>Physics2D.simulationMode = Script</c> 停物理步进、
        /// <see cref="ITimeManager.DeltaTime"/> 归零停规则时钟。旧的 <c>IsPaused</c>
        /// 布尔量已删除。</para>
        /// <para>⚠️ <b>过渡期转发器</b>：现有调用方（<c>P3DebugOverlay</c>）仍在调这里。
        /// 拼装界面接线时应直接调 <c>TimeManager.Instance.SetPaused</c>，届时删掉本方法。</para>
        /// </remarks>
        public void SetPaused(bool paused) => TimeManager.Instance?.SetPaused(paused);

        private void Awake()
        {
            // 对象注册表先起来：实体在 OnEnable 里会来找它。
            WorldRegistry.Initialize();

            // 快照名册是**另一个集合**：成员资格 = 生命周期，与是否启用无关。
            // 活动实体进 WorldRegistry，快照采集读 SnapshotRoster（含禁用中的对象）。
            SnapshotRoster.Initialize();

            // 时间管理器在规则系统之前：下面 Update 的规则时钟要读它的 delta。
            // 常驻宿主（GameRoot）已经建过就复用 —— 关卡重建不能冲掉暂停状态与倍率。
            _ownsTimeManager = TimeManager.EnsureInitialized();

            if (_catalog == null)
            {
                Debug.LogError("[RuleSystemDriver] 没有指定 RuleCatalog，规则系统无法初始化。", this);
                return;
            }
            RuleSystem.Initialize(_catalog);
            if (RuleSystem.Instance != null) _owner = this;

            // 快照系统依赖规则系统与名册，最后初始化。
            // RuleSystem 初始化失败（没配目录）时跳过，避免半可用的快照系统。
            if (RuleSystem.Instance != null)
            {
                SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);

                // 扫描整个场景补上「初始就禁用」的预置对象 —— 它们不会走 Awake / OnEnable。
                // 本脚本执行顺序 -100，早于所有实体 Awake；活动对象随后还会自己登记一次（名册去重）。
                int count = SnapshotScope.Rebuild();
                if (count == 0)
                {
                    Debug.LogWarning(
                        "[RuleSystemDriver] 快照名册为空：场景里没有任何实现 IRuleAffected 的实体。" +
                        "时间恒定 / 反转将无法恢复任何对象。", this);
                }
            }
        }

        private void Update()
        {
            // 暂停由两层组成（策划案 §3.1，禁止 Time.timeScale = 0）：
            //   ① 物理层：ITimeManager.SetPaused 切 Physics2D.simulationMode = Script
            //   ② 时钟层：DeltaTime 暂停时返回 0 —— 本帧什么都不 Tick，规则时限不再流失
            float dt = TimeManager.Instance != null ? TimeManager.Instance.DeltaTime : Time.deltaTime;
            if (dt <= 0f) return;
            RuleSystem.Instance?.Tick(dt);
        }

        private void OnDestroy()
        {
            // 不是当前持有者：说明还有另一份驱动在用它，不能把别人的单例清掉。
            if (_owner != this) return;
            _owner = null;

            // 让下次进 Play / 切场景重新初始化。
            // 规则 / 注册表 / 快照是「世界」的一部分，随关卡重建 —— 这正是「世界回设计初态」。
            RuleSystem.Reset();
            WorldRegistry.Reset();
            SnapshotRoster.Reset();
            SnapshotSystem.Reset();

            // 时间管理器是全局权威：只有本组件创建的那一份才由本组件销毁。
            // 常驻宿主（GameRoot）持有的实例活到游戏结束，不在这里 Reset。
            if (_ownsTimeManager) TimeManager.Reset();
        }
    }
}
