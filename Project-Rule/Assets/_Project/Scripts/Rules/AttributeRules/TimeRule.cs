using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 时间属性规则。
    /// </summary>
    /// <remarks>
    /// <para><b>三个参数关系</b>（增强 / 削弱 / 失效）写时间管理器；
    /// <b>两个行为关系</b>（恒定 / 反转）走快照系统。</para>
    ///
    /// <para><b>它每帧把时间管理器对齐到规则表</b>。原因：<c>ISnapshotSystem.Restore</c>
    /// 会直接覆盖规则槽位，**且按 §3.2 契约不触发任何规则系统事件** ——
    /// 所以快照一恢复，时间管理器上的倍率 / 开关就成了「表上已经没有对应规则、
    /// 但它还留着」的残留。每帧重对齐让这类残留自己消失，不需要为它发明新事件。</para>
    ///
    /// <para><b>拍照节奏归本类，快照存储归快照系统</b>（2026-10-14 定）：
    /// 环形缓冲的滚动间隔与「何时 Pin 定点」是规则策略，留在 <c>Tick</c> / <c>OnConstant</c>；
    /// 拍下来的 <c>Snapshot</c> 由 <c>ISnapshotSystem</c> 持有，本类只拿句柄。</para>
    ///
    /// <para><b>依赖为什么是懒取的</b>：<c>TimeManager</c> / <c>SnapshotSystem</c> 都是
    /// 基础设施单例，而且 <c>SnapshotSystem</c> 的构造需要 <c>RuleSystem</c> ——
    /// 在 <c>RuleSystem</c> 构造期注入它会成环。<c>IRuleTable</c> 没有单例，走构造注入。</para>
    /// </remarks>
    public class TimeRule : AttributeRuleBase, ITimeRewindControl
    {
        // ------------------------------------------------------------------
        //  数值：⚠️ 待策划确认
        // ------------------------------------------------------------------

        /// <summary>增强：时间加快的倍率。</summary>
        public const float EnhancedScale = 2f;

        /// <summary>削弱：时间减慢的倍率。</summary>
        public const float WeakenedScale = 0.5f;

        /// <summary>反转环形缓冲的采集间隔（秒）。§4.10：回溯范围 5-10 秒。</summary>
        public const float BufferInterval = 5f;

        // ------------------------------------------------------------------
        //  依赖
        // ------------------------------------------------------------------

        private readonly IRuleTable _table;

        /// <summary>「时间管理器还没起来」只警告一次，别刷屏。</summary>
        private static bool _warnedNoTimeManager;

        /// <summary>恒定开着的那张**定点快照的句柄**。0 = 窗口没开。</summary>
        /// <remarks>快照本身存在 <c>ISnapshotSystem</c> 的定点槽里，本类不再持有 <c>Snapshot</c>。</remarks>
        private int _fixedPointHandle;

        /// <summary>环形缓冲的采集计时。</summary>
        private float _bufferTimer;

        /// <summary>是否已经垫过第一张缓冲快照。</summary>
        private bool _primed;

        public TimeRule(IAttributeManager attributes, IRuleTable table) : base(attributes)
        {
            _table = table ?? throw new System.ArgumentNullException(nameof(table));
        }

        /// <summary>懒取单例：构造期它们还不一定存在，而且快照系统与规则系统有环。</summary>
        private static ITimeManager TimeManagerRef => TimeManager.Instance;

        private static ISnapshotSystem Snapshots => SnapshotSystem.Instance;

        // ==================================================================
        //  五种关系
        // ==================================================================

        /// <summary>反转：时间倒流 —— 回溯到环形缓冲里的**倒数第二张**。</summary>
        /// <remarks>
        /// <para><b>回溯目标有兜底</b>：优先取倒数第二张（保证至少回到 5 秒前）；
        /// 缓冲不足两张时退到**最旧的一张** —— 开局第一帧垫的那张，所以开局就有得退。
        /// 只有缓冲**完全为空**时才返回 false，<c>RuleSystem</c> 记 <c>NoResource</c>，
        /// <c>RuleReleaseService</c> 据此**不扣能量**。</para>
        /// <para>⚠️ 回溯会连规则表一起回退，所以回溯到的那一时刻如果还没有反转规则，
        /// 这条规则会**当场被抹掉**（下一帧的 <see cref="Tick"/> 对表时收摊）。
        /// 而能量不退（<c>RestoreMode.KeepEnergy</c>）—— 付能量、拿回溯、技能结束，
        /// 这个成本模型是自洽的。</para>
        /// </remarks>
        protected override bool OnRevert()
        {
            ISnapshotSystem snapshots = Snapshots;
            if (snapshots == null) return false;

            // 优先倒数第二张；不足两张时兜底到最旧的一张（开局垫的那张）。
            if (!snapshots.TryPeekSecondLast(out Snapshot snap)
                && !snapshots.TryPeekOldest(out snap)) return false;

            snapshots.Restore(snap, RestoreMode.KeepEnergy);
            snapshots.DiscardNewerThan(snap.Seq);      // 回退后未来作废
            return true;
        }

        /// <summary>失效：所有基于时间的机制失效（冷却 / Buff / Debuff），动画不变。</summary>
        /// <remarks>只关开关，**不动 <c>Time.timeScale</c>** —— 动画要照常播。</remarks>
        protected override bool OnNullify() => AlignAndSucceed();

        /// <summary>恒定：开一个定点回溯窗口。</summary>
        /// <remarks>
        /// <para>拍一张定点快照（<c>Pin</c>），窗口内玩家可按键**无限次**回溯到它，不耗能；
        /// 窗口随规则时长自然结束。控制入口见 <c>ITimeRewindControl</c>。</para>
        /// <para>⚠️ 矩阵里写的「时停 + 编排」是旧版描述，**已作废** ——
        /// 恒定不碰 <c>Time.timeScale</c>，整个游戏除了 UI 暂停没有任何地方设它为 0。</para>
        /// </remarks>
        protected override bool OnConstant()
        {
            ISnapshotSystem snapshots = Snapshots;
            if (snapshots == null) return false;

            _fixedPointHandle = snapshots.Pin();
            if (_fixedPointHandle == 0) return false;

            AlignTimeManagerWithTable();    // Constant 分支不碰时间管理器，这里只是保持单一入口
            return true;
        }

        /// <summary>增强：时间加快。</summary>
        protected override bool OnEnhance() => AlignAndSucceed();

        /// <summary>削弱：时间减慢。</summary>
        protected override bool OnWeaken() => AlignAndSucceed();

        /// <summary>
        /// 到期 / 被覆盖 / 重开关卡时把时间管理器恢复原样，并解除定点。
        /// </summary>
        /// <remarks>
        /// ⚠️ <c>RuleSystem</c> 在**覆盖前**调本方法，那时规则表里还是**旧关系**，
        /// 所以这里必须直接写死「恢复默认」，不能去对表 —— 对表会把旧关系又应用一遍。
        /// </remarks>
        public override void Clear()
        {
            TimeManagerRef?.RemoveScale(TimeSource.Rule);
            TimeManagerRef?.SetTimeMechanicsEnabled(true);
            UnpinFixedPoint();
        }

        // ==================================================================
        //  每帧
        // ==================================================================

        /// <summary>
        /// 每帧两件事：<b>把时间管理器对齐到规则表</b>，<b>滚环形缓冲</b>。
        /// </summary>
        public override void Tick(float deltaTime)
        {
            AlignTimeManagerWithTable();
            RollSnapshotBuffer(deltaTime);
        }

        /// <summary>
        /// 快照恢复后的静默重施。
        /// </summary>
        /// <remarks>
        /// ⚠️ **不能走基类的默认实现**：默认会对当前关系调 <c>Request</c>，
        /// 而「反转」的 <c>OnRevert</c> 会再执行一次回溯、「恒定」的 <c>OnConstant</c> 会再 Pin 一张。
        /// 恢复后只需要把时间管理器对齐到恢复出来的表，并保证定点句柄与表一致。
        /// </remarks>
        public override void Reapply(ActiveRule? slot) => AlignTimeManagerWithTable();

        /// <summary>
        /// 把时间管理器对齐到规则表当前状态；并让定点句柄与「恒定是否生效」一致。
        /// </summary>
        /// <remarks>
        /// <para>快照恢复会直接覆盖槽位而不调 <see cref="Clear"/>，规则类没法靠事件知道；
        /// 每帧对一次表，倍率 / 开关的残留就自己消失了。</para>
        /// <para><c>PushScale</c> / <c>SetTimeMechanicsEnabled</c> / <c>RemoveScale</c>
        /// 内部都有「值没变就不做事」的短路，所以每帧调它是廉价的。</para>
        /// </remarks>
        private void AlignTimeManagerWithTable()
        {
            ActiveRule? slot = _table.GetSlot(AttributeId.Time);
            ITimeManager time = TimeManagerRef;

            if (time != null)
            {
                if (!slot.HasValue)
                {
                    time.RemoveScale(TimeSource.Rule);
                    time.SetTimeMechanicsEnabled(true);
                }
                else
                {
                    switch (slot.Value.Relation)
                    {
                        case RelationId.Enhance:
                            time.PushScale(TimeSource.Rule, EnhancedScale);
                            time.SetTimeMechanicsEnabled(true);
                            break;

                        case RelationId.Weaken:
                            time.PushScale(TimeSource.Rule, WeakenedScale);
                            time.SetTimeMechanicsEnabled(true);
                            break;

                        case RelationId.Nullify:
                            time.RemoveScale(TimeSource.Rule);      // 失效不碰 TimeScale
                            time.SetTimeMechanicsEnabled(false);
                            break;

                        default:                                    // Revert / Constant
                            time.RemoveScale(TimeSource.Rule);
                            time.SetTimeMechanicsEnabled(true);
                            break;
                    }
                }
            }

            // 定点与时间管理器无关，单独对齐：
            // 恒定生效中且没有定点 → 补 Pin 一张（恢复把「恒定生效中」的表带回来时走这里）；
            // 其余情况 → 解除定点。定点槽不参与快照内容，所以不会被环形缓冲回溯冲掉。
            if (slot.HasValue && slot.Value.Relation == RelationId.Constant)
            {
                if (_fixedPointHandle == 0) _fixedPointHandle = Snapshots?.Pin() ?? 0;
            }
            else
            {
                UnpinFixedPoint();
            }
        }

        /// <summary>
        /// 环形缓冲的滚动采集。
        /// </summary>
        /// <remarks>
        /// <para>反转要回溯到「5-10 秒前」，所以缓冲必须在规则生效**之前**就在滚 ——
        /// 这就是采集放 <see cref="Tick"/> 而不是 <c>OnRevert</c> 的原因。</para>
        /// <para>⚠️ 代价：每 <see cref="BufferInterval"/> 秒一次 <c>Capture()</c>，
        /// 每次为名册里每个实体建一条记录。实体数量上百或间隔调小时要改成增量采集。</para>
        /// </remarks>
        private void RollSnapshotBuffer(float deltaTime)
        {
            // 第一帧先垫一张：不垫的话「倒数第二张」要等两个采集间隔（10 秒）才存在，
            // 反转在开局前 10 秒内永远是 NoResource —— 测试时看起来就是「没反应」。
            // 垫一张后，5 秒就够反转用了。
            if (!_primed)
            {
                _primed = true;
                _bufferTimer = 0f;
                Snapshots?.CaptureToBuffer();
                return;
            }

            _bufferTimer += deltaTime;
            if (_bufferTimer < BufferInterval) return;

            _bufferTimer -= BufferInterval;
            Snapshots?.CaptureToBuffer();
        }

        // ==================================================================
        //  ITimeRewindControl
        // ==================================================================

        /// <inheritdoc />
        public bool IsWindowOpen => _fixedPointHandle != 0;

        /// <inheritdoc />
        public bool TryRewind()
        {
            if (_fixedPointHandle == 0) return false;

            ISnapshotSystem snapshots = Snapshots;
            if (snapshots == null) return false;

            return snapshots.TryRestorePinned(_fixedPointHandle, RestoreMode.KeepEnergy);
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>三个参数关系的公共路径：对表 + 成功。</summary>
        /// <remarks>
        /// <c>RuleSystem</c> 是**先写槽位再调算法**的，所以这里读到的已经是新关系。
        /// </remarks>
        private bool AlignAndSucceed()
        {
            AlignTimeManagerWithTable();
            return true;
        }

        /// <summary>解除定点；没有定点时是空操作。</summary>
        private void UnpinFixedPoint()
        {
            if (_fixedPointHandle == 0) return;
            Snapshots?.Unpin(_fixedPointHandle);
            _fixedPointHandle = 0;
        }
    }
}
