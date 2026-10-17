using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一根**可动滑轨**（三档升降：质量变轻往上浮、变重往下沉）。
    /// </summary>
    /// <remarks>
    /// <para><b>层级是有意的</b>：<c>RailLift</c> 根 = **轨道**（静态景物，只挂脚本与白盒轨道柱），
    /// 子物体 <c>Carriage</c> = **滑块**（Kinematic 刚体 + Ground 层碰撞箱 + 白盒方块）。
    /// 与 <c>MovingPlatform</c> 那种「全在根上」不同 —— 本组件有两个物体：
    /// 要接美术、要能按段拼长度的轨道，和会动的滑块。</para>
    ///
    /// <para>⚠️ <b>滑块必须落 <c>Ground</c> 层</b>（层 8）：玩家的接触探测只认这个层，
    /// 放错层就是一块穿不过去的空气，而且承载也读不到。</para>
    ///
    /// <para>默认三档：中档 0、升 1.5、沉 1.5（合计 3 个单位的行程）。摆好滑块的位置之后，
    /// 用 Inspector 上的「中档 = 滑块当前位置」把常态位置记下来。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑦ 可动滑轨（三档升降）。</para>
    /// </remarks>
    public static class RailLiftBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "RailLift";
        private const string RailName = "Rail";
        private const string CarriageName = "Carriage";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>滑块的碰撞箱尺寸（世界单位）。</summary>
        private static readonly Vector2 CarriageSize = new Vector2(1.4f, 0.3f);

        /// <summary>白盒轨道柱的宽度（世界单位）。</summary>
        private const float RailWidth = 0.18f;

        /// <summary>默认档位：中档 0、往上 1.5、往下 1.5。</summary>
        private const float DefaultRise = 1.5f;
        private const float DefaultSink = 1.5f;

        private static readonly Color RailColor = new Color(0.62f, 0.68f, 0.78f, 1f);
        private static readonly Color CarriageColor = new Color(0.98f, 0.78f, 0.32f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑦ 可动滑轨（三档升降）", false, 56)]
        public static void CreateRailLift()
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，滑块放下去玩家踩不到。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "创建可动滑轨");

            Transform carriage = BuildCarriage(root.transform, sprite, layer);
            Transform rail = BuildRail(root.transform, sprite);
            TextMesh label = BuildLabel(root.transform);

            var lift = root.AddComponent<RailLift>();
            lift.SetCarriage(carriage);
            // 档位是**本地坐标**：滑块摆在根的原地（局部 y = 0），所以中档就是 0。
            lift.SetTiers(0f, DefaultRise, DefaultSink);
            lift.SetWhitebox(rail, label);
            EditorUtility.SetDirty(lift);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（滑块在 {SolidLayerName} 层）。" +
                      $"三档默认「升 {DefaultRise} / 沉 {DefaultSink}」，行程 {DefaultRise + DefaultSink} 个单位：" +
                      "质量变轻浮上去、变重沉下来、无规则停在中间。\n" +
                      "摆好滑块位置后点 Inspector 上的「中档 = 滑块当前位置」把它记成常态。");
        }

        /// <summary>滑块：Kinematic 刚体 + Ground 层碰撞箱 + 白盒方块。</summary>
        private static Transform BuildCarriage(Transform parent, Sprite sprite, int layer)
        {
            var go = new GameObject(CarriageName);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            Undo.RegisterCreatedObjectUndo(go, "创建可动滑轨");

            // 碰撞箱挂在滑块根上：滑块 scale 保持 1，box.size 写的就是世界尺寸。
            var box = go.AddComponent<BoxCollider2D>();
            box.size = CarriageSize;
            box.offset = Vector2.zero;

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var art = new GameObject(ArtName);
            art.transform.SetParent(go.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建可动滑轨");
            // 方块精灵是 1×1 单位，缩放多少就是世界尺寸多少 —— 和碰撞箱同尺寸。
            art.transform.localScale = new Vector3(CarriageSize.x, CarriageSize.y, 1f);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = CarriageColor;
            // 压在地面美术（order 1）和角色（order 0）后面：站上去时人是挡在滑块前的。
            renderer.sortingOrder = -1;

            return go.transform;
        }

        /// <summary>轨道柱：一块会被 <c>RailLift</c> 沿 Y 拉到全行程的白盒方块（1 世界单位高）。</summary>
        private static Transform BuildRail(Transform parent, Sprite sprite)
        {
            var go = new GameObject(RailName);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "创建可动滑轨");

            // 尺寸由 RailLift.SyncRailVisual 按三档写，这里只给个能看的初始值。
            go.transform.localScale = new Vector3(RailWidth, DefaultRise + DefaultSink, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = RailColor;
            renderer.sortingOrder = -1;

            return go.transform;
        }

        private static TextMesh BuildLabel(Transform parent)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，跳过标签。");
                return null;
            }

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "创建可动滑轨");

            var text = go.AddComponent<TextMesh>();
            text.text = RailLift.LabelText(DefaultRise, DefaultSink);
            text.font = font;
            text.fontSize = 64;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = LabelColor;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && font.material != null)
            {
                renderer.sharedMaterial = font.material;
            }
            if (renderer != null)
            {
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
