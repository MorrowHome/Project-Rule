using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一根**白盒破旧金属水管**：上下两根实心金属壁夹出一段管腔，
    /// 玩家从侧面走进管腔，管子就绕中心顺时针翻转一次。
    /// </summary>
    /// <remarks>
    /// <para><b>结构</b>：</para>
    /// <code>
    /// Tube              Rigidbody2D(Kinematic) + Tube
    ///  ├── Art          SpriteRenderer，整根管子的半透明底色（**无碰撞，可穿过**）
    ///  ├── Wall_Top     BoxCollider2D 实心（层 = Ground）+ 同尺寸视觉
    ///  └── Wall_Bottom  BoxCollider2D 实心（层 = Ground）+ 同尺寸视觉
    /// </code>
    ///
    /// <para><b>为什么壁必须实心、Art 必须没碰撞</b>：玩法就是「走进管子里面再翻过来」——
    /// 壁是挡人的墙，中间的管腔是开放的。两根壁的碰撞体在生成时就拖进 <c>Tube</c> 的
    /// Inspector 字段里，不需要再手接。</para>
    ///
    /// <para><b>管腔高度按玩家尺寸定</b>：玩家胶囊 0.75 × 1.13，管腔做成 2.5 × 1.5 ——
    /// 走得进去、跳一下也顶不到头。</para>
    ///
    /// <para><b>落在 Ground 层</b>（层 8）：玩家的 <c>MotionTuning.SolidLayer</c> 只认这个层，
    /// 放错层两根壁就只是两块能穿过去的空气。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑤ 破旧金属水管。生成后会出现在 Scene 视图中心；
    /// **管道长度**（`_length`）、翻转角度都在 Inspector 里改 —— 长度是实时生效的：
    /// 两根壁和整根视觉一起伸缩，管腔高度 / 壁厚不变。改完白盒标签和 gizmo 会一起跟着变。</para>
    /// </remarks>
    public static class TubeBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "Tube";
        private const string ArtName = "Art";
        private const string WallTopName = "Wall_Top";
        private const string WallBottomName = "Wall_Bottom";
        private const string LabelName = "Label";

        /// <summary>整根水管的外形（世界单位）：2.5 长 × 2.0 高。</summary>
        private static readonly Vector2 OuterSize = new Vector2(2.5f, 2f);

        /// <summary>金属壁厚度。两根壁 + 1.5 高的管腔 = 2.0 的外高。</summary>
        private const float WallThickness = 0.25f;

        /// <summary>半透明底色：表示中间是空腔，玩家可以走进去。</summary>
        private static readonly Color ArtColor = new Color(0.66f, 0.70f, 0.74f, 0.30f);

        /// <summary>上下两根实心壁的颜色：不透明的深金属灰。</summary>
        private static readonly Color WallColor = new Color(0.52f, 0.56f, 0.62f, 1f);

        private static readonly Color LabelColor = new Color(0.08f, 0.10f, 0.22f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑤ 破旧金属水管（走进管腔翻转）", false, 54)]
        public static void CreateTube()
        {
            Sprite whitebox = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (whitebox == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，水管的壁挡不住人。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;                    // 壁的层从根继承不了，子物体各自设
            Undo.RegisterCreatedObjectUndo(root, "创建破旧金属水管");

            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.linearDamping = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            // 不锁旋转 —— 它就是要转的。

            Tube tube = root.AddComponent<Tube>();

            // 整根管子的底色：半透明，代表管腔 —— 没有碰撞体，玩家能走进去。
            Transform art = BuildArt(root, whitebox);

            // 上下两根实心壁：挡人、也由它们夹出管腔。
            float halfOuter = OuterSize.y * 0.5f;
            float offset = halfOuter - WallThickness * 0.5f;
            BoxCollider2D wallTop = BuildWall(root, whitebox, layer, WallTopName, offset);
            BoxCollider2D wallBottom = BuildWall(root, whitebox, layer, WallBottomName, -offset);

            TextMesh label = BuildLabel(root.transform);
            tube.SetWalls(wallTop, wallBottom);
            tube.SetArt(art);
            tube.SetWhitebox(label);
            EditorUtility.SetDirty(tube);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（层级 {SolidLayerName}）：外径 {OuterSize.x} × {OuterSize.y} 单位，" +
                      $"管腔 {OuterSize.x} × {OuterSize.y - WallThickness * 2f}（玩家 0.75 × 1.13，走得进去）。" +
                      $"**管道长度**在 Inspector 的「尺寸 → 长度」里改，实时伸缩（最短 0.5）。" +
                      $"上下两根壁是**实心**碰撞体，中间的空腔没有碰撞 —— 玩家从侧面走进来就触发翻转。" +
                      $"翻转角度在「翻转」里改（正数 = 屏幕上顺时针）。只认玩家。");
        }

        /// <summary>整根管子的半透明底色 —— **不加碰撞体**，中间必须是能走进去的空腔。</summary>
        private static Transform BuildArt(GameObject root, Sprite sprite)
        {
            var art = new GameObject(ArtName);
            art.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建破旧金属水管");
            art.transform.localScale = new Vector3(OuterSize.x, OuterSize.y, 1f);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 和别的地图组件一致：压在角色后面，玩家走进管腔时是挡在管子前的。
            renderer.sortingOrder = -1;

            return art.transform;
        }

        /// <summary>一根实心金属壁：碰撞体和视觉同尺寸、在同一个物体上。</summary>
        private static BoxCollider2D BuildWall(GameObject root, Sprite sprite, int layer, string name, float y)
        {
            var wall = new GameObject(name);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = new Vector3(0f, y, 0f);
            wall.layer = layer;                    // 挡人的那块必须住在 Ground 层
            Undo.RegisterCreatedObjectUndo(wall, "创建破旧金属水管");

            var box = wall.AddComponent<BoxCollider2D>();
            // ⚠️ size 保持 (1,1)，尺寸只用 localScale 撑 —— 两边都设会平方放大。
            box.size = Vector2.one;
            box.isTrigger = false;                 // 实心：它是墙，不是判定范围

            var renderer = wall.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = WallColor;
            renderer.sortingOrder = -1;
            wall.transform.localScale = new Vector3(OuterSize.x, WallThickness, 1f);

            return box;
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
            // 写在管子**正上方**：别让字盖住管腔，玩家要一眼看出哪里能走进去。
            go.transform.localPosition = new Vector3(0f, OuterSize.y * 0.5f + 0.35f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建破旧金属水管");

            var text = go.AddComponent<TextMesh>();
            text.text = Tube.LabelText(90f);
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
