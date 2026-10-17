using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一扇**白盒单向门**（只允许顺着一个方向穿过去，反方向是墙）。
    /// </summary>
    /// <remarks>
    /// <para>白盒阶段用「方块 + 汉字」占位：<c>Art</c> 子物体是一块竖着的白盒方块，
    /// <c>Label</c> 子物体写着 <c>单向门 →</c> —— 箭头就是「允许通过的方向」。</para>
    ///
    /// <para><b>层级为什么这么分</b>：碰撞箱（<c>BoxCollider2D</c>）挂在根上，
    /// 根 scale 恒为 1、<c>size</c> 直接就是世界尺寸；美术放 <c>Art</c> 子物体上撑缩放。
    /// **不要把缩放设在根上** —— 子物体会继承缩放，标签会被横向拉长、纵向压扁。</para>
    ///
    /// <para><b>落在 Ground 层</b>（层 8）：玩家的 <c>MotionTuning.SolidLayer</c> 只认这个层，
    /// 放错层这扇门就只是一块能穿过去的空气。</para>
    ///
    /// <para><b>不需要刚体</b>：门自己不动，就是一块静态碰撞体 —— 玩家侧的
    /// <c>Rigidbody2D.Slide</c> 查询照样扫得到它。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/③ 单向门。生成后会出现在 Scene 视图中心，
    /// 方向在 Inspector 的「通过方向」里改 —— 改完白盒标签和 gizmo 箭头会一起跟着变。</para>
    /// </remarks>
    public static class OneWayGateBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "OneWayGate";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>白盒尺寸（世界单位）：1 格 = 0.5 单位 → 1 格厚 × 8 格高。
        /// 8 格高是为了让「从门顶上跳过去」没那么容易（门还是应该配天花板用）。</summary>
        private static readonly Vector2 Size = new Vector2(0.5f, 4f);

        /// <summary>白盒方块的颜色。挑了个蓝的，和橙色的平移平台区分开。</summary>
        private static readonly Color ArtColor = new Color(0.42f, 0.72f, 1f, 1f);

        private static readonly Color LabelColor = new Color(0.08f, 0.10f, 0.22f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/③ 单向门（单方向通过）", false, 52)]
        public static void CreateGate()
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，单向门既挡不住人也放不了行。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;                    // 挡路的那块必须住在 Ground 层
            Undo.RegisterCreatedObjectUndo(root, "创建单向门");

            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;

            OneWayGate gate = root.AddComponent<OneWayGate>();

            SpriteRenderer art = BuildArt(root, sprite);
            TextMesh label = BuildLabel(root.transform, gate.Pass);
            gate.SetWhitebox(art, label);
            EditorUtility.SetDirty(gate);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（层级 {SolidLayerName}）：{Size.x} × {Size.y} 单位，" +
                      $"**以物体原点为中心**（上下各 {Size.y * 0.5f}）。" +
                      $"通过方向在 Inspector 的「通过方向」里改，默认是「从左往右」。" +
                      $"⚠️ 门只认玩家，敌人和子弹两个方向都会被挡；" +
                      $"门要够高或者上面有天花板，否则玩家能从门顶跳过去。");
        }

        private static SpriteRenderer BuildArt(GameObject root, Sprite sprite)
        {
            var art = new GameObject(ArtName);
            art.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建单向门");
            // 方块精灵是 1×1 单位，缩放多少就是世界尺寸多少 —— 和碰撞箱同尺寸。
            art.transform.localScale = new Vector3(Size.x, Size.y, 1f);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 和平移平台一致：压在地面美术和角色后面，人从门里穿过去时是挡在门前的。
            renderer.sortingOrder = -1;
            return renderer;
        }

        private static TextMesh BuildLabel(Transform parent, OneWayGate.PassDirection pass)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，跳过标签。");
                return null;
            }

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            // 写在门**正中央**：箭头就是「允许穿过去的方向」，一眼就能读。
            go.transform.localPosition = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(go, "创建单向门");

            var text = go.AddComponent<TextMesh>();
            text.text = OneWayGate.LabelText(pass);
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
