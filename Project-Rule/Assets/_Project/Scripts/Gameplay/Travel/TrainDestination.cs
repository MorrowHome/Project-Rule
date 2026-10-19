namespace RuleGame.Gameplay.Travel
{
    /// <summary>
    /// 面板里的一行：一个可选的目的地。<b>不持有场景对象</b> ——
    /// 没加载的站也要能列出来，所以只带数据。
    /// </summary>
    public readonly struct TrainDestination
    {
        public TrainDestination(string id, string name, string scenePath, int order, bool loaded, bool current, bool locked)
        {
            Id = id;
            Name = name;
            ScenePath = scenePath;
            Order = order;
            Loaded = loaded;
            Current = current;
            Locked = locked;
        }

        /// <summary>站点编号（与 <c>TrainStation.Id</c> 一致）。</summary>
        public string Id { get; }

        /// <summary>显示名。</summary>
        public string Name { get; }

        /// <summary>所在场景。空 = 没有目录信息，只能在场的站。</summary>
        public string ScenePath { get; }

        /// <summary>排序。</summary>
        public int Order { get; }

        /// <summary>这个站此刻是否已经在场（不在场要先去加载）。</summary>
        public bool Loaded { get; }

        /// <summary>是不是玩家脚下这一站。</summary>
        public bool Current { get; }

        /// <summary>玩家还没解锁这一站 —— 选不了。</summary>
        public bool Locked { get; }
    }
}
