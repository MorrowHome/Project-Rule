using System.Text;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Rules
{
    /// <summary>
    /// 属性规则验证的公共骨架。
    /// </summary>
    /// <remarks>
    /// <para>收集断言结果、提供内存测试目录、提供「造一个受规则影响的实体」的工具。</para>
    /// <para>⚠️ 必须在 <b>Play 模式</b>下用：<c>RuleAffectedBody</c> 的登记与应用发生在
    /// <c>Awake</c> / <c>OnEnable</c>，编辑模式不触发。</para>
    /// </remarks>
    public sealed class RuleVerifyHarness
    {
        public const float Tolerance = 0.001f;

        /// <summary>Unity 2D 的默认摩擦，没挂材质时生效。</summary>
        public const float UnityDefaultFriction = 0.4f;

        /// <summary>五个关系，顺序固定，方便遍历。</summary>
        public static readonly RelationId[] AllRelations =
        {
            RelationId.Revert, RelationId.Nullify, RelationId.Constant,
            RelationId.Enhance, RelationId.Weaken,
        };

        private readonly StringBuilder _log = new StringBuilder();
        private int _pass;
        private int _fail;

        public int Passed => _pass;
        public int Failed => _fail;

        public void Section(string title) => _log.Append('\n').Append('[').Append(title).Append(']');

        public void Check(string label, bool ok, string detail)
        {
            if (ok) { _pass++; _log.Append("\n  ✓ ").Append(label); }
            else { _fail++; _log.Append("\n  ✗ ").Append(label).Append("   → ").Append(detail); }
        }

        public string Report(string title)
            => $"===== {title}：通过 {_pass}，失败 {_fail} ====={_log}";

        // ==================================================================
        //  共享工具
        // ==================================================================

        /// <summary>临时的内存目录。避免为了测试去写游戏数据资产。</summary>
        public sealed class TestCatalog : IRuleCatalog
        {
            private readonly RuleDef[] _defs;

            public TestCatalog(RuleDef[] defs) => _defs = defs;

            public RuleDef Get(AttributeId attr, RelationId rel)
            {
                foreach (RuleDef d in _defs)
                {
                    if (d != null && d.Attribute == attr && d.Relation == rel) return d;
                }
                return null;
            }
        }

        /// <summary>给某个属性的五个关系各造一个内存定义。</summary>
        public static RuleDef[] MakeDefs(AttributeId attr, float duration)
        {
            var defs = new RuleDef[AllRelations.Length];
            for (int i = 0; i < AllRelations.Length; i++) defs[i] = MakeDef(attr, AllRelations[i], duration);
            return defs;
        }

        public static RuleDef MakeDef(AttributeId attr, RelationId rel, float duration)
        {
            var d = ScriptableObject.CreateInstance<RuleDef>();
            d.name = $"TestDef_{attr}_{rel}";
            d.Attribute = attr;
            d.Relation = rel;
            d.Duration = duration;
            d.ActivationCost = 0f;
            d.Cooldown = 0f;
            return d;
        }

        /// <summary>不用 LINQ，避免为一个测试引入依赖。</summary>
        public static bool Contains(IRuleAffected target)
        {
            WorldRegistry reg = WorldRegistry.Instance;
            if (reg == null) return false;

            var all = reg.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (ReferenceEquals(all[i], target)) return true;
            }
            return false;
        }

        /// <summary>造一个 Dynamic 刚体 + BoxCollider2D + RuleAffectedBody 的测试实体。</summary>
        public static GameObject SpawnBody(string name, float mass, PhysicsMaterial2D colliderMaterial,
                                           out Rigidbody2D rb, out BoxCollider2D col, out RuleAffectedBody body)
        {
            var go = new GameObject(name);
            rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.mass = mass;

            col = go.AddComponent<BoxCollider2D>();
            col.sharedMaterial = colliderMaterial;

            body = go.AddComponent<RuleAffectedBody>();
            return go;
        }

        /// <summary>造一个可当克隆来源的临时材质。</summary>
        public static PhysicsMaterial2D MakeMaterial(float friction)
        {
            var src = Resources.Load<PhysicsMaterial2D>("RuleDefaultMaterial2D");
            var m = Object.Instantiate(src);
            m.name = "VerifyMaterial";
            m.friction = friction;
            return m;
        }

        // ==================================================================
        //  入口
        // ==================================================================

        /// <summary>跑全部属性规则的验证。</summary>
        public static string RunAll()
            => MassRuleVerifier.Run()
             + "\n\n" + FrictionRuleVerifier.Run()
             + "\n\n" + DamageRuleVerifier.Run()
             + "\n\n" + TimeRuleVerifier.Run()
             + "\n\n" + TimeEffectVerifier.Run()
             + "\n\n" + CausalityRuleVerifier.Run()
             + "\n\n" + DamageProcessorVerifier.Run()
             + "\n\n" + RuleSystemVerifier.Run();
    }
}
