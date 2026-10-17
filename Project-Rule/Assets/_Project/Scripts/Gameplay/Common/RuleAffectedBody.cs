using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 可复用的「受规则影响的实体」适配组件。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么问题</b>：摩擦和质量这两个属性**没有引擎全局载体**，
    /// 只能逐个改实体本身。没有这个组件，P2 的测试箱、P3 的玩家、P4 的门
    /// 就要各自重写一遍「登记 / 订阅 / 应用 / 还原」。</para>
    ///
    /// <para><b>工作流程</b>（不需要任何新事件）：</para>
    /// <list type="number">
    /// <item><c>OnEnable</c> → 向 <see cref="WorldRegistry"/> 登记自己，
    /// 并**立刻**读一次属性值应用上去（覆盖「实体在规则生效后才生成」的情况）。</item>
    /// <item>订阅 <see cref="IRuleSystem.OnRulesChanged"/>；规则一变就重新读值应用。</item>
    /// <item><c>OnDisable</c> → 取消订阅、注销、把材质还原回原样。</item>
    /// </list>
    ///
    /// <para><b>为什么是「实体自己读」而不是「规则类遍历实体写」</b>：
    /// 规则类只改 <see cref="IAttributeManager"/> 里的一个缩放系数（一次赋值），
    /// 实体各自读取。新生成的实体天然拿到最新值，规则类也不必认识注册表。</para>
    ///
    /// <para>⚠️ <b>摩擦的实现限制（重要）</b>：<c>Collider2D.sharedMaterial</c> 是共享的，
    /// 直接改它的 <c>friction</c> 会污染所有用同一材质的物体。所以要缩放一个实体的摩擦，
    /// 必须给它一份**独占**的 <see cref="PhysicsMaterial2D"/>。</para>
    ///
    /// <para>而 <see cref="PhysicsMaterial2D"/> <b>没有公开构造函数</b>
    /// （Unity 官方 XML 文档里整个 Physics2D 模块只有 <c>PhysicsShapeGroup2D</c>
    /// 一个 <c>#ctor</c>），也没有内置资源可以 <c>Resources.GetBuiltinResource</c>。
    /// 因此独占材质只能靠 <see cref="Object.Instantiate(Object)"/> <b>克隆一份已有材质</b>得到。
    /// 克隆来源按以下顺序解析（见 <see cref="ResolveMaterialSource"/>）：</para>
    /// <list type="number">
    /// <item><see cref="_frictionSource"/> —— 在 Inspector 里指定的模板</item>
    /// <item><c>Rigidbody2D.sharedMaterial</c> —— 挂在刚体上的材质</item>
    /// <item>第一个非空的 <c>Collider2D.sharedMaterial</c></item>
    /// <item><c>Resources.Load</c> 默认模板 <c>RuleDefaultMaterial2D</c></item>
    /// </list>
    /// <para>四个都拿不到时，会打一条警告并**跳过摩擦**（质量仍然生效）。</para>
    ///
    /// <para>⚠️ <b>另一个前提</b>：同一实体的所有碰撞体必须共用同一摩擦值，
    /// 否则一份独占材质无法同时表达它们。不一致时 <c>Awake</c> 会打警告。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class RuleAffectedBody : MonoBehaviour, IRuleAffected, IRuleApplier
    {
        /// <summary>摩擦独占材质的兜底克隆来源，放在 <c>Assets/_Project/Data/Resources/</c>。</summary>
        private const string DefaultMaterialResourceName = "RuleDefaultMaterial2D";

        /// <summary>Unity 2D 的默认摩擦系数（没挂材质时生效）。</summary>
        private const float UnityDefaultFriction = 0.4f;

        /// <summary>「正常」质量的参考值。质量反转时以它为中心取倒数。</summary>
        private const float ReferenceMass = 1f;

        /// <summary>质量下限。Unity 不接受 0 或负的质量。</summary>
        private const float MinMass = 0.01f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("应用范围")]
        [Tooltip("是否响应摩擦规则。关掉表示这个实体的摩擦不受规则影响。")]
        [SerializeField] private bool _applyFriction = true;

        [Tooltip("是否响应质量规则。关掉表示这个实体的质量不受规则影响。")]
        [SerializeField] private bool _applyMass = true;

        [Header("摩擦（可留空）")]
        [Tooltip("摩擦独占材质的克隆模板。留空则依次尝试刚体材质、碰撞体材质、" +
                 "最后是 Resources/RuleDefaultMaterial2D。")]
        [SerializeField] private PhysicsMaterial2D _frictionSource;

        // ==================================================================
        //  缓存
        // ==================================================================

        private Rigidbody2D _body;

        /// <summary>碰撞体缓存。<see cref="Colliders"/> 直接返回它，避免每次访问都分配。</summary>
        private Collider2D[] _colliders;

        /// <summary>每个碰撞体原本的共享材质（可能为 null = 用引擎默认）。与 <c>_colliders</c> 等长。</summary>
        private PhysicsMaterial2D[] _originalMaterials;

        /// <summary>本实体独占的材质。只有摩擦需要被改变时才创建。</summary>
        private PhysicsMaterial2D _override;

        private float _baseMass = 1f;
        private float _baseFriction = UnityDefaultFriction;
        private float _baseBounciness;

        private IRuleSystem _rules;
        private IWorldRegistry _registry;

        /// <summary>「找不到摩擦材质来源」的警告只打一次，避免几十个实体刷屏。</summary>
        private static bool _warnedNoSource;

        // ==================================================================
        //  IRuleAffected
        // ==================================================================

        /// <inheritdoc />
        public Rigidbody2D Body => _body;

        /// <inheritdoc />
        /// <remarks>返回 <c>Awake</c> 时缓存好的数组，不会分配。</remarks>
        public Collider2D[] Colliders => _colliders ?? System.Array.Empty<Collider2D>();

        // ==================================================================
        //  只读状态（供规则类 / 测试 / 调试面板使用）
        // ==================================================================

        /// <summary>是否已成功登记进 <see cref="WorldRegistry"/>。</summary>
        public bool IsRegistered { get; private set; }

        /// <summary>记录下来的原始质量，缩放基于它做乘法。</summary>
        public float BaseMass => _baseMass;

        /// <summary>记录下来的原始摩擦。</summary>
        public float BaseFriction => _baseFriction;

        /// <summary>当前是否持有一份独占材质（即摩擦正在被规则改变）。</summary>
        public bool HasMaterialOverride => _override != null;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _colliders = GetComponentsInChildren<Collider2D>(includeInactive: true);
            _originalMaterials = new PhysicsMaterial2D[_colliders.Length];

            if (_body != null) _baseMass = _body.mass;

            CaptureBaseFriction();

            // 快照名册：成员资格 = 生命周期，所以登记在 Awake、注销在 OnDestroy。
            // ⚠️ 初始就禁用的预置对象不会走到 Awake —— 那批由 SnapshotScope 场景扫描补上。
            SnapshotRoster.Instance?.Register(this);
        }

        private void OnEnable()
        {
            _registry = WorldRegistry.Instance;
            _rules = RuleSystem.Instance;

            _registry?.Register(this);
            IsRegistered = _registry != null;

            if (_rules != null)
            {
                _rules.OnRulesChanged += Apply;
            }
            else
            {
                // RuleSystemDriver 的执行顺序是 -100，正常情况下不会走到这里。
                Debug.LogWarning(
                    "[RuleAffectedBody] RuleSystem 尚未初始化，本实体不会响应规则变化。" +
                    "检查场景里有没有挂 RuleSystemDriver。", this);
            }

            // 登记之后立刻应用一次：实体可能在规则生效之后才被生成。
            Apply();
        }

        private void OnDisable()
        {
            if (_rules != null) _rules.OnRulesChanged -= Apply;

            _registry?.Unregister(this);
            IsRegistered = false;

            // 物体被禁用时不该继续占着一份材质。
            ReleaseOverride();
        }

        private void OnDestroy()
        {
            // OnDisable 正常会先跑；这里只是兜底，两个操作都是幂等的。
            if (_rules != null) _rules.OnRulesChanged -= Apply;
            ReleaseOverride();

            // 名册的注销跟着生命周期走（不是 OnDisable）—— 禁用中的对象必须留在快照里。
            SnapshotRoster.Instance?.Unregister(this);
        }

        // ==================================================================
        //  应用
        // ==================================================================

        /// <inheritdoc />
        /// <remarks>快照恢复后由 <c>RuleSystem.ReapplyActiveEffects()</c> 统一调用 —— 恢复不发规则事件。</remarks>
        public void ApplyRuleValues() => Apply();

        /// <summary>
        /// 读取当前属性值并应用到自己的刚体与碰撞体上。
        /// </summary>
        /// <remarks>
        /// 由 <see cref="IRuleSystem.OnRulesChanged"/> 自动触发，也可以手动调用。
        /// </remarks>
        public void Apply()
        {
            // 允许延迟初始化：万一 OnEnable 时规则系统还没起来，这里补一次。
            if (_rules == null) _rules = RuleSystem.Instance;
            if (_rules == null) return;

            IAttributeManager attrs = _rules.Attributes;
            if (attrs == null) return;

            AttributeValues v = attrs.Values;

            if (_applyMass && _body != null) _body.mass = ResolveMass(v);
            if (_applyFriction) ApplyFriction(v.FrictionScale);
        }

        /// <summary>
        /// 由属性值算出本实体应有的质量。
        /// </summary>
        /// <remarks>
        /// 正常是 <c>原始质量 × 倍率</c>；反转是 <c>参考质量² / 原始质量</c>，
        /// 也就是以参考质量为中心取倒数——重的变轻、轻的变重。
        /// </remarks>
        private float ResolveMass(AttributeValues v)
        {
            if (!v.MassInverted)
            {
                return Mathf.Max(_baseMass * v.MassScale, MinMass);
            }

            // 原始质量非法时没有可倒数的基准，退回参考质量。
            if (_baseMass <= 0f) return Mathf.Max(ReferenceMass, MinMass);

            return Mathf.Max(ReferenceMass * ReferenceMass / _baseMass, MinMass);
        }

        /// <summary>
        /// 摩擦**大小**的缩放。规则「摩擦 × 反转」置位的 <c>FrictionInverted</c> 在这里**刻意不响应**。
        /// </summary>
        /// <remarks>
        /// 反转的语义是「摩擦力的**方向**翻过来」，只对**有移动意图**的实体成立
        /// （玩家 / 敌人的控制器把它读成「按右往左走」，见 <c>PlayerController</c>）。
        /// 而动态物体（石头）没有意图：把它的接触摩擦方向翻过来 = 引擎持续同向加速 → 越滑越快、失控。
        /// 所以这里只消费 <c>FrictionScale</c>；反转由各运动控制器自己实现。
        /// </remarks>
        private void ApplyFriction(float scale)
        {
            if (_colliders.Length == 0) return;

            // 缩放回到 1 就完全还原，不留独占材质。
            if (Mathf.Approximately(scale, 1f))
            {
                ReleaseOverride();
                return;
            }

            PhysicsMaterial2D mat = EnsureOverride();
            if (mat == null) return;   // 找不到来源，已经警告过

            mat.friction = _baseFriction * scale;
        }

        // ==================================================================
        //  独占材质
        // ==================================================================

        /// <summary>
        /// 拿到（必要时创建）本实体独占的摩擦材质。创建不出来时返回 <c>null</c>。
        /// </summary>
        private PhysicsMaterial2D EnsureOverride()
        {
            if (_override != null) return _override;

            PhysicsMaterial2D source = ResolveMaterialSource();
            if (source == null)
            {
                if (!_warnedNoSource)
                {
                    _warnedNoSource = true;
                    Debug.LogWarning(
                        "[RuleAffectedBody] 找不到任何可克隆的 PhysicsMaterial2D，" +
                        "摩擦规则对这些实体不会生效。解决办法（任选其一）：\n" +
                        "  1) 在 Assets/_Project/Data/Resources/ 下新建一个 2D 物理材质，" +
                        "命名为 RuleDefaultMaterial2D；\n" +
                        "  2) 给实体（或它的碰撞体）挂一个 2D 物理材质；\n" +
                        "  3) 在本组件的「摩擦」栏指定一个模板材质。", this);
                }
                return null;
            }

            // PhysicsMaterial2D 没有公开构造函数，只能克隆。
            // 显式写泛型参数：Instantiate(Object) 这个非泛型重载也适用，
            // 虽然重载决议会选泛型版本，但写死了就不依赖这条规则。
            _override = Instantiate<PhysicsMaterial2D>(source);
            _override.name = name + " (rule)";
            _override.friction = _baseFriction;
            _override.bounciness = _baseBounciness;

            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null) _colliders[i].sharedMaterial = _override;
            }

            return _override;
        }

        /// <summary>按优先级解析可克隆的材质来源。</summary>
        private PhysicsMaterial2D ResolveMaterialSource()
        {
            if (_frictionSource != null) return _frictionSource;

            if (_body != null && _body.sharedMaterial != null) return _body.sharedMaterial;

            for (int i = 0; i < _originalMaterials.Length; i++)
            {
                if (_originalMaterials[i] != null) return _originalMaterials[i];
            }

            return Resources.Load<PhysicsMaterial2D>(DefaultMaterialResourceName);
        }

        private void ReleaseOverride()
        {
            if (_override == null) return;

            // 只还原「还挂着自己材质」的碰撞体：运行期别人换了材质就不要抢回来。
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider2D col = _colliders[i];
                if (col != null && col.sharedMaterial == _override)
                {
                    col.sharedMaterial = _originalMaterials[i];
                }
            }

            DestroyMaterial(_override);
            _override = null;
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>
        /// 从现有材质里读出基准摩擦 / 弹性，并检查各碰撞体是否一致。
        /// </summary>
        private void CaptureBaseFriction()
        {
            // 刚体上的材质优先：它本来就覆盖所有附属碰撞体。
            PhysicsMaterial2D bodyMat = _body != null ? _body.sharedMaterial : null;
            if (bodyMat != null)
            {
                _baseFriction = bodyMat.friction;
                _baseBounciness = bodyMat.bounciness;
            }

            bool captured = bodyMat != null;

            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider2D col = _colliders[i];
                PhysicsMaterial2D mat = col != null ? col.sharedMaterial : null;
                _originalMaterials[i] = mat;

                if (mat == null) continue;   // 用刚体材质或引擎默认值

                if (!captured)
                {
                    _baseFriction = mat.friction;
                    _baseBounciness = mat.bounciness;
                    captured = true;
                }
                else if (!Mathf.Approximately(mat.friction, _baseFriction))
                {
                    Debug.LogWarning(
                        $"[RuleAffectedBody] {name}: 碰撞体之间的原始摩擦不一致" +
                        $"（{_baseFriction} vs {mat.friction}）。" +
                        "本组件只能用一个独占材质表达统一摩擦，将以第一个为准。", this);
                }
            }
        }

        private static void DestroyMaterial(PhysicsMaterial2D mat)
        {
            if (mat == null) return;

            if (Application.isPlaying) Destroy(mat);
            else DestroyImmediate(mat);
        }
    }
}
