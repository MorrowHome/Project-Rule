using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**可推铁箱**（有质量、吃重力的动态物块）。
    /// </summary>
    /// <remarks>
    /// <para>与其余地图组件不同，铁箱是**动态刚体**：受重力、有质量、会被推走 ——
    /// 于是「重力 × 反转」把它掀到天上、「质量 × 削弱」让一个人就能推飞它，
    /// 这两个效果都不需要额外代码。</para>
    ///
    /// <para>⚠️ <b>必须落 <c>Ground</c> 层</b>（层 8）：玩家的 <c>Slide</c> 与接触探测只认这个层。
    /// 放错层箱子既挡不住人也站不上去，就是一块看得见的空气。</para>
    ///
    /// <para><b>质量怎么调</b>：直接改 <c>Rigidbody2D.mass</c>（默认 8）。玩家的推力是
    /// 60 N，判据 <c>F &gt; μ·m·g</c>（μ 见 <c>Data/Physics/CrateMaterial2D</c>，默认 0.5）——
    /// 大约 <b>12 kg 以上就推不动了</b>。所以：
    /// 8 kg = 缓慢挪动、「质量 × 增强」（24 kg）= 一堵墙、「质量 × 削弱」（2.4 kg）= 推着走、
    /// 「质量 × 失效」（0.4 kg）= 一推就飞。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑧ 铁箱（可推重物）。</para>
    /// </remarks>
    public static class PushableCrateBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>箱子与地面共用的摩擦材质。铁箱要能推得动，摩擦别给太高。</summary>
        private const string MaterialPath = "Assets/_Project/Data/Physics/CrateMaterial2D.asset";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "PushableCrate";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>白盒尺寸（世界单位）。1 格 = 0.5 单位，所以 1×1 = 2×2 格。</summary>
        private static readonly Vector2 Size = new Vector2(1f, 1f);

        /// <summary>
        /// 默认质量（kg）。按玩家的 60 N 推力与 0.5 的摩擦算：约 12 kg 是推不动的门槛，
        /// 给 8 kg 让它在默认规则下「推得动、但明显很沉」。
        /// </summary>
        private const float DefaultMass = 8f;

        private static readonly Color ArtColor = new Color(0.70f, 0.72f, 0.78f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑧ 铁箱（可推重物）", false, 57)]
        public static void CreateCrate()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，" +
                               "箱子放下去既挡不住人也推不动。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;
            Undo.RegisterCreatedObjectUndo(root, "创建铁箱");

            BuildBody(root, sprite);

            var label = BuildLabel(root.transform);
            label.text = "铁箱";

            // 受规则影响：把质量 / 摩擦的规则值落到这个刚体上（身份 + 订阅 + 应用都在它里面）。
            var affected = root.AddComponent<RuleAffectedBody>();

            // 可推：收玩家的推力。两个组件一起挂才有「质量变了 → 推起来手感跟着变」。
            root.AddComponent<PushableBody>();

            EditorUtility.SetDirty(affected);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（{SolidLayerName} 层，质量 {DefaultMass} kg）。" +
                      "按住方向键顶住它就能推 —— 加速度 = 玩家的 60 N ÷ 这个质量。\n" +
                      "· 质量 × 增强 → 推不动（变成墙）· 质量 × 削弱 / 失效 → 一推就走 / 一推就飞\n" +
                      "· 重力 × 反转 → 箱子往上掉（它会一直醒着，不会睡在半空）");
        }

        /// <summary>刚体 + 碰撞箱 + 白盒方块。箱子是**动态**刚体，这是它与其余地图组件的根本区别。</summary>
        private static void BuildBody(GameObject root, Sprite sprite)
        {
            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;

            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(MaterialPath);
            if (material != null)
            {
                // 也当 RuleAffectedBody 的摩擦克隆来源（它优先用碰撞体上的材质）。
                box.sharedMaterial = material;
            }
            else
            {
                Debug.LogWarning("[地图组件] 找不到 " + MaterialPath +
                                 "，箱子会用引擎默认摩擦 0.4，推起来会轻一些。");
            }

            // PushableBody 在运行期会把这几项再配一遍；这里先写好是为了 Inspector 里看得见。
            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.mass = DefaultMass;
            body.gravityScale = 1f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var art = new GameObject(ArtName);
            art.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建铁箱");
            // 方块精灵是 1×1 单位，缩放多少就是世界尺寸多少 —— 和碰撞箱同尺寸。
            art.transform.localScale = new Vector3(Size.x, Size.y, 1f);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 与地形美术同层（order 1 之下、角色 order 0 之下）：人站在箱子前面。
            renderer.sortingOrder = -1;
        }

        private static TextMesh BuildLabel(Transform parent)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            // 根 scale 是 1，所以这里的局部坐标就是世界距离。
            go.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建铁箱");

            var text = go.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 64;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = LabelColor;

            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，标签会显示不出来。");
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (font != null && font.material != null) renderer.sharedMaterial = font.material;
                // 标签在白盒阶段要永远看得见，压在所有东西上面。
                renderer.sortingOrder = 100;
            }

            return text;
        }

        /// <summary>没开 Scene 视图时退回原点，不至于把物体扔到看不见的地方。</summary>
        private static Vector3 SceneViewPivot()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return Vector3.zero;

            Vector3 pivot = view.pivot;
            pivot.z = 0f;
            return pivot;
        }
    }
}
