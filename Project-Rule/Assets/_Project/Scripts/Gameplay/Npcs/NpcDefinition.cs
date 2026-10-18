using System;
using UnityEngine;

namespace RuleGame.Gameplay.Npcs
{
    public enum NpcRole { Merchant, Mayor, Resident, GearKeeper, Guard, MysteryMerchant }
    public enum NpcState { Idle, Walk, Talk, Talk1, Talk2, Talk3, Talk4, Talk5, Sell, Bye, PassKey, Attack }

    [Serializable]
    public sealed class NpcProduct
    {
        public string Id;
        public string DisplayName;
        public string Effect;
        [Tooltip("需求表未提供价格，-1 表示尚未配置，不可据此扣款。")]
        public int Price = -1;
    }

    /// <summary>共享的角色配置；任务条件与交易结果由关卡及已有系统持有。</summary>
    [CreateAssetMenu(menuName = "RuleGame/NPC/角色配置", fileName = "Npc_Definition")]
    public sealed class NpcDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Region;
        public NpcRole Role;
        public Sprite IdleSprite;
        public Sprite TalkSprite;
        [Min(0.1f)] public float InteractionRadius = 2f;
        [Min(0f)] public float RepeatDelay = 1f;
        [Min(0f)] public float PatrolRadius = 1.5f;
        [Min(0f)] public float WalkSpeed = 0.65f;
        [Min(0f), Tooltip("物理驱动力上限；默认足以克服 3 kg 身体的地面摩擦。质量/摩擦增强后仍可能推不动。")]
        public float WalkForce = 60f;
        [Min(0.1f)] public float IdleDuration = 2f;
        [Min(0.1f)] public float ActionDuration = 0.65f;
        [Tooltip("完整动作未交付时保留静态姿态，不能据此认定动作已接入。")]
        [TextArea] public string MissingAnimations;
        public NpcProduct[] Products = Array.Empty<NpcProduct>();

        public string SpeakerId(NpcState state) => Id + "_" + state;
    }
}
