using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>一次真实出手的只读表现数据；不负责伤害，也不依赖表现程序集。</summary>
    public readonly struct PlayerAttackCue
    {
        public Vector2 Center { get; }
        public Vector2 Size { get; }
        public float Facing { get; }
        public bool Heavy { get; }
        public int ComboIndex { get; }

        public PlayerAttackCue(Vector2 center, Vector2 size, float facing, bool heavy, int comboIndex)
        {
            Center = center;
            Size = size;
            Facing = facing;
            Heavy = heavy;
            ComboIndex = comboIndex;
        }
    }
}
