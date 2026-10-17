using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 风扇：朝一个方向吹出**一条风道** —— 风场的实体载体，与 <see cref="WindZone"/> 姊妹。
    /// </summary>
    /// <remarks>
    /// <para><b>与 <see cref="WindZone"/> 的分工</b>：WindZone 是「这里有一块区域」——
    /// 区域自己就是一切，力沿**世界 y** 从下往上衰减；风扇是「从这里朝那边吹」——
    /// 风道从扇体出发沿**风向**延伸，力沿**离出风口的距离**衰减。
    /// 贴地朝上 = 上升气流，贴墙朝外 = 横向风道。二者的受力规则完全一样
    /// （报力、由实体自己除以质量），只是摆的姿势不同。</para>
    ///
    /// <para><b>范围与风力都在这一个组件上</b>：<see cref="_length"/> × <see cref="_width"/>
    /// 是风道尺寸，<see cref="_force"/> → <see cref="_forceAtFar"/> 是出风口到远端的力。
    /// 两者都画在 Gizmo 里 —— 选中风扇就能看见风吹到哪、每一段多强。</para>
    ///
    /// <para><b>为什么不组合一个 WindZone 子物体</b>：那样范围与风力要么存两份
    /// （风扇一份、风场一份，谁说了算不好办），要么用户得对着两个对象调。
    /// 这里把「风道从哪吹到哪」的几何直接长在自己身上：一个对象、一份参数，
    /// 而且 <see cref="IWindField"/> 本来就是为「不认识的场景组件照样吹得动」留的口子。</para>
    ///
    /// <para><b>碰撞体是「查询入口」，必须覆盖整条风道</b>：WindSensor 是拿玩家碰撞体中心
    /// <c>OverlapPoint</c> 查 Wind 层，命中后走接口调到 <see cref="ForceAt"/>。
    /// 所以本组件在 <c>OnValidate</c> / <c>OnEnable</c> 里把风道尺寸写进自己的 BoxCollider2D
    /// —— 那不是「碰撞」，只是让查询命中得到；真正的受力判定在矩形取样里。
    /// 碰撞体是 trigger：不挡人（与风场同一约定）。</para>
    ///
    /// <para>⚠️ <b>必须落 Wind 层</b>：玩家的 <c>WindSensor</c> 只查这个层（<c>_windLayer</c>），
    /// 放错层风扇白放、且没有任何报错。生成工具会自动设好。</para>
    ///
    /// <para>⚠️ <b>不要旋转风扇</b>：风向由 <see cref="_direction"/> 决定，风道与碰撞体都按它算；
    /// 旋转对象只会让碰撞体与风道错位。</para>
    ///
    /// <para>⚠️ <b>风道下方要有地形</b>：风场是判定不是墙 —— 被托出风道边缘的人会直接掉下去
    /// （上升气流的经典事故，见 <c>Doc/玩家动作设计.md</c> §9.7）。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/② 风扇（吹出风场）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class Fan : MonoBehaviour, IWindField
    {
        /// <summary>朝哪吹。风道从扇体出发沿这个方向延伸。</summary>
        public enum BlowDirection
        {
            /// <summary>朝右吹。</summary>
            Right = 0,

            /// <summary>朝上吹（默认，上升气流）。</summary>
            Up = 1,

            /// <summary>朝左吹。</summary>
            Left = 2,

            /// <summary>朝下吹。</summary>
            Down = 3,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("风向与范围")]
        [Tooltip("朝哪吹。风道从扇体出发沿这个方向延伸「风道长度」。\n" +
                 "⚠️ 改这个字段而不是旋转对象 —— 风道与碰撞体都按它算，旋转会让两者错位。")]
        [SerializeField] private BlowDirection _direction = BlowDirection.Up;

        [Tooltip("风道长度：从扇体起沿风向吹多远（世界单位）。0 = 不吹。")]
        [SerializeField, Min(0f)] private float _length = 10f;

        [Tooltip("风道宽度：垂直于风向的那一维（世界单位），以扇体为中轴左右各一半。")]
        [SerializeField, Min(0.05f)] private float _width = 4f;

        [Header("风力（牛顿）")]
        [Tooltip("出风口（近端）的风力大小，沿风向吹。150 是风场的实测基准 —— " +
                 "质量 1 的人会被托起来，质量规则一改浮空高度当场变化。")]
        [SerializeField, Min(0f)] private float _force = 150f;

        [Tooltip("风道远端的风力大小。比近端小 = 越远越弱 —— " +
                 "玩家会浮在「风力 = 自身重力」的那一段距离上（与 WindZone 同一条玩法）。\n" +
                 "与近端相同 = 恒定力（那样只有阈值没有平衡点：人会一路加速冲出去）。")]
        [SerializeField, Min(0f)] private float _forceAtFar = 0f;

        [Header("空气阻力")]
        [Tooltip("风道内的空气阻力（每秒速度衰减率）。0 = 带着动能冲过平衡点、被弹出风道（实测出场速度 18+）；" +
                 "2 左右就会收敛到平衡位置。它就是风的「耗散」。")]
        [SerializeField, Min(0f)] private float _drag = 2f;

        [Header("白盒")]
        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        private BoxCollider2D _area;

        /// <summary>出风口（风扇自己）的世界坐标。风道从这里朝外延伸。</summary>
        public Vector2 Origin => transform.position;

        /// <summary>风向（单位向量）。</summary>
        public Vector2 DirectionVector
        {
            get
            {
                switch (_direction)
                {
                    case BlowDirection.Right: return Vector2.right;
                    case BlowDirection.Up:    return Vector2.up;
                    case BlowDirection.Left:  return Vector2.left;
                    default:                  return Vector2.down;
                }
            }
        }

        /// <summary>风道长度（沿风向）。</summary>
        public float Length => _length;

        /// <summary>风道宽度（垂直于风向）。</summary>
        public float Width => _width;

        /// <summary>出风口的风力（牛顿）。</summary>
        public float Force => _force;

        /// <summary>风道远端的风力（牛顿）。</summary>
        public float FarForce => _forceAtFar;

        /// <summary>风道内的空气阻力（每秒速度衰减率）。</summary>
        public float Drag => _drag;

        /// <summary>当前风向。</summary>
        public BlowDirection Direction => _direction;

        // ==================================================================
        //  IWindField
        // ==================================================================

        /// <inheritdoc />
        /// <remarks>点不在风道内、或本组件被关掉时返回 <see cref="Vector2.zero"/>。
        /// 关掉组件即可让风扇停用 —— 调用方不需要额外判断（与 <see cref="WindZone"/> 同约定）。</remarks>
        public Vector2 ForceAt(Vector2 point)
        {
            if (!TrySample(point, out float t)) return Vector2.zero;

            return DirectionVector * Mathf.Lerp(_force, _forceAtFar, t);
        }

        /// <inheritdoc />
        public float DragAt(Vector2 point) => TrySample(point, out float _) ? _drag : 0f;

        /// <summary>
        /// 点是否落在风道内；在的话给出「离出风口多远」的归一化位置（0 = 出风口，1 = 远端）。
        /// </summary>
        /// <remarks>把点投影到风向轴上：沿风向的分量决定远近、垂直分量决定宽度。
        /// 与碰撞体无关 —— 碰撞体只是查询入口，这里才是真正的形状。</remarks>
        private bool TrySample(Vector2 point, out float along01)
        {
            along01 = 0f;

            if (!isActiveAndEnabled) return false;
            if (_length <= 0f) return false;

            Vector2 dir  = DirectionVector;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            Vector2 offset = point - Origin;
            float along  = Vector2.Dot(offset, dir);
            float across = Vector2.Dot(offset, perp);

            if (along < 0f || along > _length) return false;
            if (Mathf.Abs(across) > _width * 0.5f) return false;

            along01 = along / _length;
            return true;
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void OnEnable() => SyncArea();

        private void OnValidate()
        {
            if (_length < 0f) _length = 0f;
            if (_width < 0.05f) _width = 0.05f;

            SyncArea();
            RefreshLabel();
        }

        /// <summary>把风道尺寸写进碰撞体 —— 让 WindSensor 的 OverlapPoint 查得到。</summary>
        /// <remarks>风道从扇体出发**朝外**延伸：碰撞体中心在风道中点上（半径 = 长度的一半），
        /// 沿风向的那一维是长度、垂直那一维是宽度。</remarks>
        private void SyncArea()
        {
            if (_area == null) _area = GetComponent<BoxCollider2D>();
            if (_area == null) return;

            Vector2 dir = DirectionVector;

            _area.size = Mathf.Abs(dir.x) > 0.5f
                       ? new Vector2(_length, _width)
                       : new Vector2(_width, _length);
            _area.offset = dir * (_length * 0.5f);
            _area.isTrigger = true;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接上白盒标签的引用。</summary>
        public void SetWhitebox(TextMesh label)
        {
            _label = label;
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float force, BlowDirection direction)
        {
            string way;
            switch (direction)
            {
                case BlowDirection.Right: way = "右"; break;
                case BlowDirection.Up:    way = "上"; break;
                case BlowDirection.Left:  way = "左"; break;
                default:                  way = "下"; break;
            }

            return "风扇 " + way + " " + force.ToString("0.#") + "N";
        }

        /// <summary>把当前风力 / 风向写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声（与弹簧平台同一处理）。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_force, _direction);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            Vector2 dir  = DirectionVector;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 origin = Origin;
            Vector2 halfW  = perp * (_width * 0.5f);
            Vector2 far    = origin + dir * _length;

            // 风道轮廓（真实形状，与受力判定一致）。
            Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.85f);
            Vector2 cornerA = origin - halfW;
            Vector2 cornerB = origin + halfW;
            Vector2 cornerC = far + halfW;
            Vector2 cornerD = far - halfW;
            Gizmos.DrawLine(cornerA, cornerB);
            Gizmos.DrawLine(cornerB, cornerC);
            Gizmos.DrawLine(cornerC, cornerD);
            Gizmos.DrawLine(cornerD, cornerA);

            // 三排箭头：近 / 中 / 远，长度按该处的力 —— 一眼看出「越远越弱」。
            DrawArrowRow(origin + dir * (_length * 0.12f), perp, _width, dir,
                         Mathf.Lerp(_force, _forceAtFar, 0.12f));
            DrawArrowRow(origin + dir * (_length * 0.5f), perp, _width, dir,
                         Mathf.Lerp(_force, _forceAtFar, 0.5f));
            DrawArrowRow(origin + dir * (_length * 0.88f), perp, _width, dir,
                         Mathf.Lerp(_force, _forceAtFar, 0.88f));

            // 扇体。
            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            Gizmos.DrawWireCube(origin, new Vector3(0.7f, 0.7f, 0f));
        }

        /// <summary>沿风道横截面画一排箭头示意风力（100 牛顿 ≈ 1 个单位长度）。</summary>
        private static void DrawArrowRow(Vector2 center, Vector2 perp, float width, Vector2 dir, float force)
        {
            const float NewtonsPerUnit = 100f;

            float length = Mathf.Clamp(force / NewtonsPerUnit, 0f, 2f);
            if (length <= 0.01f) return;

            Gizmos.color = new Color(0.9f, 0.95f, 0.4f, 0.9f);

            Vector2 head = -dir * (length * 0.28f);
            Vector2 wing = perp * (length * 0.12f);

            const int arrows = 4;
            for (int i = 0; i < arrows; i++)
            {
                float u = arrows == 1 ? 0.5f : i / (float)(arrows - 1);

                // 箭头在宽度方向上一字排开，两端各留 10% 的边。
                Vector2 basePoint = center + perp * (width * 0.8f * (u - 0.5f));
                Vector2 tip = basePoint + dir * length;

                Gizmos.DrawLine(basePoint, tip);
                Gizmos.DrawLine(tip, tip + head + wing);
                Gizmos.DrawLine(tip, tip + head - wing);
            }
        }
    }
}
