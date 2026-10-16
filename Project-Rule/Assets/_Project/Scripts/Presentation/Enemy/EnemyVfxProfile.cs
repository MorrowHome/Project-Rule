using UnityEngine;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>招式和资源之间的接线点。换美术只换本配置，不改 AI 或伤害计时。</summary>
    [CreateAssetMenu(menuName = "RuleGame/Enemy/VFX Profile")]
    public sealed class EnemyVfxProfile : ScriptableObject
    {
        public EnemyVfxClip Impact, Slash, Cast, Charge, Death, Shot;
        public Material Material;
        public Material BeamMaterial;
        public Material BeamCoreMaterial;
        public Sprite ProjectileSprite;
        public EnemySpriteSequence[] ProjectileAnimations = System.Array.Empty<EnemySpriteSequence>();
        public Material ProjectileMaterial;
        [Tooltip("实体投掷物沿用原画旋转，不附加发光拖尾或发射爆闪。")]
        public bool PhysicalProjectiles;
        public EnemySpriteSequence[] BeamAnimations = System.Array.Empty<EnemySpriteSequence>();
        public Material BeamSpriteMaterial;
        public Color Accent = new Color(1f, .6f, .2f);
        public Color Secondary = new Color(.85f, .35f, .1f);
        [Min(.1f)] public float ImpactSize = 6f;
        [Min(.1f)] public float SlashSize = 5f;
        [Min(.1f)] public float CastSize = 7f;
        [Range(0f, .3f)] public float CameraImpulse = .08f;
    }
}
