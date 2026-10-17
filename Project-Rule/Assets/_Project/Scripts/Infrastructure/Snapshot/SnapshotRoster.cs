using System.Collections.Generic;
using System.Collections.ObjectModel;
using RuleGame.Infrastructure.Common;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// <see cref="ISnapshotRoster"/> 的默认实现：一个保持登记顺序的列表。
    /// </summary>
    /// <remarks>
    /// <para>与 <c>WorldRegistry</c> 同一套单例模式，由 <c>RuleSystemDriver</c> 初始化。</para>
    /// <para><b>为什么用 List 而不是 HashSet</b>：登记顺序要稳定，快照采集才有确定的顺序。</para>
    /// </remarks>
    public sealed class SnapshotRoster : ISnapshotRoster
    {
        private static SnapshotRoster _instance;

        /// <summary>全局单例。由 <c>RuleSystemDriver</c> 在 Awake 里初始化。</summary>
        public static SnapshotRoster Instance => _instance;

        /// <summary>初始化单例。重复调用会覆盖（重开关卡 / 切场景时有用）。</summary>
        public static void Initialize() => _instance = new SnapshotRoster();

        /// <summary>仅测试用：注入替身。</summary>
        public static void SetInstanceForTesting(SnapshotRoster instance) => _instance = instance;

        /// <summary>清空单例。切场景 / 退出时调用。</summary>
        public static void Reset() => _instance = null;

        private readonly List<IRuleAffected> _targets = new List<IRuleAffected>();
        private readonly ReadOnlyCollection<IRuleAffected> _view;

        public SnapshotRoster()
        {
            _view = _targets.AsReadOnly();
        }

        /// <summary>名册里的对象数量（已剔除销毁的）。</summary>
        public int Count
        {
            get
            {
                PruneDestroyed();
                return _targets.Count;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IRuleAffected> Targets
        {
            get
            {
                PruneDestroyed();
                return _view;
            }
        }

        /// <inheritdoc />
        public void Register(IRuleAffected target)
        {
            if (target == null) return;
            if (_targets.Contains(target)) return;
            _targets.Add(target);
        }

        /// <inheritdoc />
        public void Unregister(IRuleAffected target)
        {
            if (target == null) return;
            _targets.Remove(target);
        }

        /// <inheritdoc />
        public void Clear() => _targets.Clear();

        /// <summary>倒序扫描并移除已销毁的对象。</summary>
        private void PruneDestroyed()
        {
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                if (IsDestroyed(_targets[i])) _targets.RemoveAt(i);
            }
        }

        /// <summary>
        /// 对象是否已经失效。
        /// </summary>
        /// <remarks>⚠️ 这里会调用 <c>target.Body</c>，要求实现者即使刚体被销毁也不要让这个 getter 抛异常。</remarks>
        private static bool IsDestroyed(IRuleAffected target)
        {
            if (target == null) return true;
            return target.Body == null;
        }
    }
}
