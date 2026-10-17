using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 可动滑轨：滑块沿一条**竖直导轨**上下走，停在哪一档由**世界的质量法则**决定。
    /// </summary>
    /// <remarks>
    /// <para><b>它是会动的支撑面这一族的第三个成员</b>：三者都实现 <see cref="IMovingSurface"/>
    /// （站在上面的实体被带走），区别只在「目标位置由什么决定」——
    /// <c>MovingPlatform</c> 由**时间**（匀速往返），<c>SpringPlatform</c> 由**负载**（弹簧积分），
    /// 本组件由**质量属性**（三档换位）。要按时间往返的平台请用 <c>MovingPlatform</c>，
    /// 本组件不会自己走。</para>
    ///
    /// <para><b>三档怎么定</b>（对应 <c>规则矩阵.json</c> 的质量一列）：</para>
    /// <list type="bullet">
    /// <item><b>增强</b>（质量变大）→ 重 → <b>低档</b>，沉到底；</item>
    /// <item><b>恒定</b> / 无规则（1 倍）→ <b>中档</b>，也就是摆场景时的常态；</item>
    /// <item><b>削弱 / 失效</b>（变轻）→ <b>高档</b>，浮到顶；</item>
    /// <item><b>反转</b>（重的变轻、轻的变重）→ **逐台**分流：<see cref="_baseMass"/> 比参考质量
    /// 重的浮上去、比参考质量轻的沉下来 —— 两根 <see cref="_baseMass"/> 不同的滑轨会**互相对调**，
    /// 天生就是一对配重，不需要任何特例代码。</item>
    /// </list>
    /// <para>判据只关心「比基准重了还是轻了」，不关心具体数值，所以质量规则那 5 个关系自然收敛成三档。
    /// 从高处往下排：越重越低、越轻越高 —— 这正是「世界变了」而不是「操作变了」。</para>
    ///
    /// <para><b>为什么读属性而不是订阅 <c>IRuleSystem.OnRulesChanged</c></b>：
    /// 每物理帧读一次 <see cref="IAttributeManager"/> 只是一个结构体拷贝，代价可忽略；
    /// 不订阅就没有生命周期问题 —— 规则在滑轨**生成之前**就已生效时，第一次读就拿到正确档位，
    /// 不需要「先补算一次」这种补丁。（<c>RuleAffectedBody</c> 走订阅，是因为它要在规则变化的那一刻
    /// 去改刚体与材质，不是在每帧做决策。）</para>
    ///
    /// <para><b>层级：轨道是父，滑块是子。</b>⚠️ 这与 <c>MovingPlatform</c> / <c>SpringPlatform</c>
    /// （碰撞体 + 刚体 + 脚本全挂在根的同一个物体上）不同，是有意的 —— 本组件天然有**两个物体**：
    /// 静止的轨道（要接美术、要能按段拼长度）与会动的滑块。若把轨道也挂在滑块底下，
    /// 就得每帧反过来搬它才看着不动。所以：</para>
    /// <list type="bullet">
    /// <item><b>根</b> = 轨道：只挂本脚本 + 轨道美术，**不挂碰撞体 / 刚体**（它是景物，不该挡路）；</item>
    /// <item><b>子物体</b>（默认名 <c>Carriage</c>）= 滑块：<c>Rigidbody2D</c>（Kinematic）
    /// + <c>Collider2D</c>（**Ground 层**，玩家只认这个层）+ 美术。</item>
    /// </list>
    /// <para>承载照常成立：玩家侧 <c>ContactSensor</c> 用 <c>GetComponentInParent&lt;IMovingSurface&gt;()</c>
    /// 找承载面，从滑块碰撞体往上一步就是本组件。</para>
    ///
    /// <para><b>档位写在本地坐标</b>（相对轨道原点的 y）：迁移 / 复制这一段时只要拖根物体，
    /// 三档跟着走，不会像世界坐标那样留在原地。⚠️ 代价是**不要缩放根物体** ——
    /// 父级缩放会被子物体继承，滑块的碰撞箱与位移都会跟着变形；
    /// 白盒轨道美术请缩放 <c>Rail</c> 子物体（见 <see cref="SyncRailVisual"/>）。</para>
    ///
    /// <para><b>暂停 / 时间规则</b>：暂停时物理已停步进（<c>Physics2D.simulationMode = Script</c>），
    /// 滑轨也必须停 —— 读 <c>TimeManager.IsPaused</c> 早退（单一真相是 TimeManager）；
    /// 换档用 <c>Time.fixedDeltaTime</c>，随 <c>Time.timeScale</c> 缩放 —— 世界变慢，滑块也跟着变慢。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑦ 可动滑轨（三档升降）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RailLift : MonoBehaviour, IMovingSurface
    {
        /// <summary>滑块停在哪一档。</summary>
        public enum Tier
        {
            /// <summary>沉到底。质量**变重**时到这里。</summary>
            Low = 0,

            /// <summary>常态。无规则 / 质量恒定时在这里（也就是摆场景时看到的那个位置）。</summary>
            Middle = 1,

            /// <summary>浮到顶。质量**变轻**时到这里。</summary>
            High = 2,
        }

        /// <summary>「反转」的参考质量，与 <c>RuleAffectedBody</c> 同一口径。</summary>
        private const float ReferenceMass = 1f;

        /// <summary>比基准重 / 轻的判定容差。质量规则的取值都是 1 / 3 / 0.3 / 0.05，离得很开。</summary>
        private const float MassEpsilon = 1e-3f;

        /// <summary>白盒轨道柱两端各多留一点，免得看着像滑块悬在轨道外面。</summary>
        private const float RailMargin = 0.25f;

        /// <summary>白盒标签离轨道顶端的距离。</summary>
        private const float LabelGap = 0.5f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("三档高度（本地坐标 = 相对轨道原点的 y）")]
        [Tooltip("中档：**无规则时**滑块停在哪。摆场景时按常态摆，自动美术与碰撞都从这里对齐。\n" +
                 "用 Inspector 上的「中档 = 滑块当前位置」按钮填。")]
        [SerializeField] private float _midHeight = 0f;

        [Tooltip("中档 → 高档的高度差（质量**变轻**时往上浮这么多）。0 = 轻了也不动。")]
        [SerializeField, Min(0f)] private float _riseHeight = 1.5f;

        [Tooltip("中档 → 低档的深度差（质量**变重**时往下沉这么多）。0 = 重了也不动。")]
        [SerializeField, Min(0f)] private float _sinkDepth = 1.5f;

        [Header("换档")]
        [Tooltip("换档速度（单位 / 秒）。三档之间是**匀速走**过去，不是瞬移 ——\n" +
                 "瞬移带不动站在上面的人（承载是按速度给的）。想读起来像瞬间跳档就把它调大（例如 60）。")]
        [SerializeField, Min(0.01f)] private float _speed = 3f;

        [Header("质量")]
        [Tooltip("本机构的**基准质量**：判定「重的变轻 / 轻的变重」时拿它跟参考质量 1 比。\n" +
                 "⚠️ 它只是判定的基准，不是滑块刚体的 mass（Kinematic 刚体的质量物理不参与）。\n" +
                 "两根 _baseMass 一个大于 1、一个小于 1 的滑轨，在「质量 × 反转」下会**互相对调**。")]
        [SerializeField, Min(0.01f)] private float _baseMass = 1f;

        [Header("引用")]
        [Tooltip("滑块。留空则自动找子物体上的第一个 Rigidbody2D。")]
        [SerializeField] private Transform _carriage;

        [Header("白盒")]
        [Tooltip("白盒轨道柱的美术。**会被沿 Y 轴拉伸到三档的全行程**：\n" +
                 "所以这张图要按「1 世界单位高」做（与白盒方块一致）。\n" +
                 "**接正式美术时把这个引用清空**，把美术作为轨道（根物体）的子物体摆好 —— 代码不碰它。")]
        [SerializeField] private Transform _railVisual;

        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;

        /// <summary>滑块当前的本地 y。**自己记账，不信 transform** ——
        /// <c>MovePosition</c> 要到物理步结束才落到 transform 上，读它拿到的可能是上一步的值。</summary>
        private float _carriageY;

        /// <summary>当前档位（由质量属性算出）。</summary>
        private Tier _tier = Tier.Middle;

        private bool _warnedNoCarriage;

        /// <summary>本物理帧滑块的速度（单位 / 秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        /// <remarks>由「这一步实际走了多远 ÷ dt」算出，与位移严格一致 ——
        /// 承载方加进自己的速度后，走出的距离与滑块**逐帧相等**，不会累积漂移。</remarks>
        public Vector2 Velocity { get; private set; }

        /// <summary>当前档位（表现层要换算「重 / 轻」提示就读它）。</summary>
        public Tier CurrentTier => _tier;

        /// <summary>滑块当前的本地 y。</summary>
        public float CarriageY => _carriageY;

        /// <summary>中档高度（本地 y）。</summary>
        public float MidHeight => _midHeight;

        /// <summary>中档 → 高档的高度差。</summary>
        public float RiseHeight => _riseHeight;

        /// <summary>中档 → 低档的深度差。</summary>
        public float SinkDepth => _sinkDepth;

        /// <summary>换档速度（单位 / 秒）。</summary>
        public float Speed => _speed;

        /// <summary>基准质量（只用于判定重的变轻 / 轻的变重）。</summary>
        public float BaseMass => _baseMass;

        /// <summary>滑块。摆场景 / 编辑器工具用。</summary>
        public Transform Carriage => _carriage;

        // ==================================================================
        //  档位判定（纯函数：不读字段、不碰 Unity 对象，便于断言）
        // ==================================================================

        /// <summary>
        /// 按当前质量属性算出滑块该在哪一档。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <paramref name="values"/> **按值传**，不用 <c>in</c>：
        /// 这点拷贝开销不值得让 MCP 的 <c>codedom</c> 编译器（只到 C# 5）测不了它。</para>
        /// <para>「反转」的分流依据是 <paramref name="baseMass"/> 与参考质量的大小关系，
        /// 不是当前有效质量 —— 反转的语义就是「以参考质量为中心取倒数」，见 <c>MassRule</c>。</para>
        /// </remarks>
        public static Tier ResolveTier(AttributeValues values, float baseMass)
        {
            if (values.MassInverted)
            {
                if (baseMass > ReferenceMass + MassEpsilon) return Tier.High;   // 重的变轻 → 浮
                if (baseMass < ReferenceMass - MassEpsilon) return Tier.Low;    // 轻的变重 → 沉
                return Tier.Middle;                                            // 正好在参考质量上 → 不倒
            }

            if (values.MassScale > 1f + MassEpsilon) return Tier.Low;    // 增强 = 变重 = 沉
            if (values.MassScale < 1f - MassEpsilon) return Tier.High;   // 削弱 / 失效 = 变轻 = 浮
            return Tier.Middle;
        }

        /// <summary>某一档对应的本地 y。</summary>
        public float HeightOf(Tier tier)
        {
            switch (tier)
            {
                case Tier.Low:  return _midHeight - _sinkDepth;
                case Tier.High: return _midHeight + _riseHeight;
                default:        return _midHeight;
            }
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake() => Rebuild();

        /// <summary>重开一局 / 对象池取出时回到当前档位，不能接着上一次的位置走。</summary>
        private void OnEnable() => Rebuild();

        private void FixedUpdate()
        {
            // 暂停：物理已停步进，滑轨也必须停（单一真相是 TimeManager）。
            if (TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                return;
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，
            // 所以这两个都要全限定（与 MovingPlatform / SpringPlatform / Tube 同一处理）。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;
            if (!EnsureCarriage()) return;

            UpdateTier();
            Advance(dt);
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>读一次属性，档位真的变了才刷新记账（顺带刷新白盒标签）。</summary>
        private void UpdateTier()
        {
            Tier wanted = ResolveTier(ReadAttributes(), _baseMass);
            if (wanted == _tier) return;

            _tier = wanted;
            RefreshLabel();
        }

        /// <summary>朝目标档位匀速走一步。</summary>
        private void Advance(float dt)
        {
            float target = HeightOf(_tier);
            float next = Mathf.MoveTowards(_carriageY, target, _speed * dt);

            // 到位时 MoveTowards 返回的就是目标本身，所以这里可以直接比相等
            // —— 用 Approximately 会把「最后那一小步」也吞掉，滑块就永远差一点点。
            if (next == _carriageY)
            {
                Velocity = Vector2.zero;
                return;
            }

            Vector2 previous = _body.position;
            Vector2 world = ToWorld(next);

            Velocity = (world - previous) / dt;
            _carriageY = next;
            _body.MovePosition(world);
        }

        /// <summary>把「档位点」摆回当前档位并清空速度。开局 / 对象池取出时调用。</summary>
        private void Rebuild()
        {
            if (!EnsureCarriage()) return;

            _tier = ResolveTier(ReadAttributes(), _baseMass);
            _carriageY = HeightOf(_tier);

            if (_body != null) _body.position = ToWorld(_carriageY);
            Velocity = Vector2.zero;

            SyncRailVisual();
            RefreshLabel();
        }

        /// <summary>当前世界的属性值。规则系统没起来时按默认值（= 中档）。</summary>
        private static AttributeValues ReadAttributes()
        {
            IAttributeManager attributes = RuleSystem.Instance?.Attributes;
            return attributes != null ? attributes.Values : AttributeValues.Default;
        }

        /// <summary>滑块本体。没在 Inspector 里指定时自动找子物体上的第一个刚体。</summary>
        private bool EnsureCarriage()
        {
            if (_carriage == null)
            {
                Rigidbody2D found = GetComponentInChildren<Rigidbody2D>();
                if (found != null) _carriage = found.transform;
            }

            if (_carriage == null)
            {
                if (!_warnedNoCarriage)
                {
                    _warnedNoCarriage = true;
                    Debug.LogError(
                        "[RailLift] 没有滑块 —— 轨道上找不到 Rigidbody2D。用菜单 " +
                        "Tools/规则游戏/地图组件/⑦ 生成，或在「引用」栏指定滑块。", this);
                }
                return false;
            }

            if (_body == null)
            {
                _body = _carriage.GetComponent<Rigidbody2D>();
                if (_body == null)
                {
                    Debug.LogError("[RailLift] 滑块上没有 Rigidbody2D，滑块不会移动。", this);
                    return false;
                }
                ConfigureBody();
            }

            return true;
        }

        /// <summary>档位点的本地坐标 → 世界坐标。</summary>
        /// <remarks>滑块的 x / z 一直是摆场景时的值，代码只改 y。</remarks>
        private Vector2 ToWorld(float localY)
        {
            Vector3 local = _carriage.localPosition;
            return transform.TransformPoint(new Vector3(local.x, localY, local.z));
        }

        private void ConfigureBody()
        {
            // Kinematic：位移完全由代码决定，重力 / 摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;

            // 定步长物理 + 高帧率渲染：不开插值换档会一格一格跳。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>滑块当前的本地 y（摆完之后用「中档 = 滑块当前位置」按钮读它）。</summary>
        public float CarriageLocalY => _carriage != null ? _carriage.localPosition.y : _midHeight;

        private float CarriageLocalX => _carriage != null ? _carriage.localPosition.x : 0f;

        /// <summary>只给编辑器工具用：一次改三档。</summary>
        public void SetTiers(float midHeight, float riseHeight, float sinkDepth)
        {
            _midHeight = midHeight;
            _riseHeight = Mathf.Max(0f, riseHeight);
            _sinkDepth = Mathf.Max(0f, sinkDepth);
            SyncRailVisual();
        }

        /// <summary>只给编辑器工具用：指定滑块。</summary>
        public void SetCarriage(Transform carriage)
        {
            _carriage = carriage;
            _body = null;
        }

        /// <summary>只给编辑器工具用：接上白盒轨道柱与标签的引用。</summary>
        public void SetWhitebox(Transform railVisual, TextMesh label)
        {
            _railVisual = railVisual;
            _label = label;
            SyncRailVisual();
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float riseHeight, float sinkHeight)
        {
            return "滑轨 升" + riseHeight.ToString("0.#") + " 沉" + sinkHeight.ToString("0.#");
        }

        /// <summary>
        /// 把白盒轨道柱拉成「三档全行程」，并把它与标签摆到对应高度。
        /// </summary>
        /// <remarks>
        /// <para>柱子是 1 世界单位高的图（与白盒方块一致），所以缩放 y 直接就是高度
        /// —— 与 <c>SpringPlatform</c> 的弹簧柱同一套做法。</para>
        /// <para><b>只有在 <see cref="_railVisual"/> 挂上时才碰美术</b>：正式轨道是拼出来的
        /// （绿 / 红 / 紫分开画，红色段可无缝衔接延长），那种图的长度是画死的，不该被拉伸
        /// —— 接正式美术时清空这个引用即可。</para>
        /// <para>写在**根物体的局部坐标**里：轨道柱、标签都是根的子物体，根不动它们就不动。</para>
        /// </remarks>
        private void SyncRailVisual()
        {
            if (_railVisual == null) return;

            float x = CarriageLocalX;
            float span = _riseHeight + _sinkDepth;
            float bottom = _midHeight - _sinkDepth;
            float height = Mathf.Max(span + RailMargin * 2f, 0.5f);

            _railVisual.localPosition = new Vector3(x, bottom + span * 0.5f, 0f);

            Vector3 scale = _railVisual.localScale;
            scale.y = height;
            _railVisual.localScale = scale;

            if (_label != null)
            {
                _label.transform.localPosition =
                    new Vector3(x, _midHeight + _riseHeight + LabelGap, 0f);
            }
        }

        private void OnValidate()
        {
            if (_sinkDepth < 0f) _sinkDepth = 0f;
            if (_riseHeight < 0f) _riseHeight = 0f;
            if (_speed < 0.01f) _speed = 0.01f;
            if (_baseMass < 0.01f) _baseMass = 0.01f;

            if (!Application.isPlaying) SyncRailVisual();
            RefreshLabel();
        }

        /// <summary>把档位与行程写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_riseHeight, _sinkDepth);
            // 播放中额外报当前档位 —— 白盒测试时不用去猜它为什么不动。
            if (Application.isPlaying) wanted += " · " + TierName(_tier);

            if (_label.text != wanted) _label.text = wanted;
        }

        private static string TierName(Tier tier)
        {
            switch (tier)
            {
                case Tier.Low:  return "低档（重）";
                case Tier.High: return "高档（轻）";
                default:        return "中档";
            }
        }

        private void OnDrawGizmos()
        {
            float x = CarriageLocalX;
            float lowY  = _midHeight - _sinkDepth;
            float highY = _midHeight + _riseHeight;

            Vector3 low  = transform.TransformPoint(new Vector3(x, lowY, 0f));
            Vector3 mid  = transform.TransformPoint(new Vector3(x, _midHeight, 0f));
            Vector3 high = transform.TransformPoint(new Vector3(x, highY, 0f));

            // 轨道：低档 → 高档。
            Gizmos.color = new Color(0.55f, 0.75f, 0.95f, 0.9f);
            Gizmos.DrawLine(low, high);

            // 三档。颜色即语义：红 = 重（沉到底）、黄 = 常态、青 = 轻（浮到顶）。
            Gizmos.color = new Color(1f, 0.45f, 0.35f, 0.95f);
            Gizmos.DrawWireSphere(low, 0.14f);

            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.95f);
            Gizmos.DrawWireSphere(mid, 0.14f);

            Gizmos.color = new Color(0.45f, 0.95f, 1f, 0.95f);
            Gizmos.DrawWireSphere(high, 0.14f);

            // 播放中：滑块现在在哪一档。
            if (!Application.isPlaying) return;

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(x, _carriageY, 0f)), 0.22f);
        }
    }
}
