using System;
using UnityEngine;

namespace RuleGame.Infrastructure.LevelFlow
{
    /// <summary>
    /// 关卡表：关卡号 → 场景路径 / 下一关的数据资产。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：<see cref="ILevelFlow.RequestNextLevel"/> 只携带「当前关号」，
    /// 而「下一关是几号、场景在哪」不该硬编码进加载器 ——
    /// 关卡号与场景文件名没有必然对应，隐藏关 / 分支关会打破「关号 +1」的假设。</para>
    /// <para><b>为什么放在 Infrastructure</b>：存档（<c>RuleGame.Persistence</c>）需要读关卡表校验进度，
    /// 而它只依赖 Core + Infrastructure，看不到 <c>Gameplay</c>。放这里两边都能读。</para>
    /// <para>⚠️ 表里的场景必须登记进 Build Settings，否则运行时加载会失败。</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "RuleGame/Level Catalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        /// <summary>一条关卡记录。</summary>
        [Serializable]
        public struct LevelEntry
        {
            [Tooltip("关卡号，从 1 起，与 ILevelFlow.CurrentLevel 同一套编号")]
            public int Level;

            [Tooltip("场景路径，形如 Assets/_Project/Scenes/Level_01.unity")]
            public string ScenePath;

            [Tooltip("下一关的关卡号；0 表示没有下一关（终章）")]
            public int NextLevel;
        }

        [SerializeField] private LevelEntry[] _levels = Array.Empty<LevelEntry>();

        /// <summary>记录条数。</summary>
        public int Count => _levels?.Length ?? 0;

        /// <summary>表里的最小关卡号；空表返回 0。</summary>
        public int FirstLevel
        {
            get
            {
                int first = 0;
                for (int i = 0; i < Count; i++)
                {
                    int level = _levels[i].Level;
                    if (first == 0 || level < first) first = level;
                }
                return first;
            }
        }

        /// <summary>按关卡号取记录。找不到返回 false。</summary>
        public bool TryGet(int level, out LevelEntry entry)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_levels[i].Level != level) continue;
                entry = _levels[i];
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>取下一关的关卡号。本关不存在或已无下一关时返回 false。</summary>
        public bool TryGetNext(int level, out int nextLevel)
        {
            if (TryGet(level, out LevelEntry entry) && entry.NextLevel != 0)
            {
                nextLevel = entry.NextLevel;
                return true;
            }

            nextLevel = 0;
            return false;
        }
    }
}
