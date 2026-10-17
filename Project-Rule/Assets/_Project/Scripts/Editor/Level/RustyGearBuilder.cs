using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**生锈齿轮**（摩擦触发的机关）：一个会转的齿轮装置 + 汉字标签。
    /// </summary>
    /// <remarks>
    /// <para><b>什么时候用</b>：要「摩擦变小 / 失效」才响的开关 ——
    /// 平时锈死，释放「摩擦 × 削弱」或「摩擦 × 失效」之后转起来，把机关激活。
    /// 与 <see cref="ImpactButton"/>（⑨ 巨大按钮）是同一类开关，出口形态一致。</para>
    ///
    /// <para>⚠️ <b>没有碰撞体</b>：齿轮是装饰 + 触发源，不挡路 —— 和按钮（实心、能站上去）不同，
    /// 摆在墙上、齿轮坑里都行，不需要玩家碰到它。</para>
    ///
    /// <para><b>转动表现</b>：白盒阶段用自转占位（圆 + 一圈齿，转起来一眼看得出）；
    /// 正式美术的「两帧循环」到位后，把两帧填进 Inspector 的「转动帧」就自动切换成帧动画。</para>
    ///
    /// <para><b>转起来之后触发什么还没定</b>：把接收方拖到 Inspector 的「转动时」里
    /// （UnityEvent，不写代码），或者代码里订阅 <c>OnActivated</c>。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑩ 生锈齿轮（摩擦松脱机关）。</para>
    /// </remarks>
    public static class RustyGearBuilder
    {
        private const string SquareSpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string CircleSpritePath = "Assets/_Project/Art/Sprites/WhiteBoxCircle.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        private const string RootName = "RustyGear";
        private const string ArtName = "Art";
        private const string ToothName = "Tooth";
        private const string LabelName = "Label";

        /// <summary>齿轮本体（圆）的直径（世界单位）。</summary>
        private const float BodyDiameter = 1.6f;

        /// <summary>齿数：转起来一眼看得出在转，又不至于糊成一圈。</summary>
        private const int ToothCount = 8;

        /// <summary>齿心所在的圆半径。</summary>
        private const float ToothRingRadius = 0.8f;

        /// <summary>单个齿的边长。</summary>
        private const float ToothSize = 0.36f;

        private const float LabelHeight = 1.25f;

        private static readonly Color BodyColor  = new Color(0.55f, 0.58f, 0.62f, 1f);
        private static readonly Color ToothColor = new Color(0.42f, 0.45f, 0.50f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑩ 生锈齿轮（摩擦松脱机关）", false, 59)]
        public static void CreateGear()
        {
            Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(SquareSpritePath);
            Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
            if (square == null || circle == null)
            {
                Debug.LogError("[地图组件] 找不到白盒精灵：\n" + SquareSpritePath + "\n" + CircleSpritePath);
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "创建生锈齿轮");

            RustyGear gear = root.AddComponent<RustyGear>();

            // 齿轮本体：圆 + 一圈齿。自转时连齿一起转，肉眼一眼看得出在转。
            Transform art = BuildPart(root.transform, ArtName, circle,
                                      Vector2.one * BodyDiameter, Vector2.zero, BodyColor, 0);
            BuildTeeth(art, square);

            TextMesh label = BuildLabel(root.transform);

            gear.SetWhitebox(art, label);
            EditorUtility.SetDirty(gear);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}：整体直径约 {BodyDiameter + ToothSize} 单位，" +
                      "**以物体原点为中心**。\n" +
                      "· 没有碰撞体 —— 齿轮是装饰 + 触发源，不挡路（不用管层）\n" +
                      "· 触发：释放「摩擦 × 削弱」或「摩擦 × 失效」后开始转（美术帧未接入前用自转占位）\n" +
                      "· 转起来之后触发什么还没定：把接收方拖进「转动时」即可");
        }

        /// <summary>一圈齿：绕本体中心均分摆放，随本体一起自转。</summary>
        private static void BuildTeeth(Transform art, Sprite square)
        {
            for (int i = 0; i < ToothCount; i++)
            {
                float angle = i * (360f / ToothCount) * Mathf.Deg2Rad;
                var at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ToothRingRadius;

                BuildPart(art, ToothName + i, square,
                          Vector2.one * ToothSize, at, ToothColor, 0);
            }
        }

        /// <summary>一块白盒方块。方块精灵是 1×1 单位，缩放多少就是世界尺寸多少。</summary>
        private static Transform BuildPart(Transform parent, string name, Sprite sprite,
                                           Vector2 size, Vector2 localPosition, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            Undo.RegisterCreatedObjectUndo(go, "创建生锈齿轮");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;

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
            go.transform.localPosition = new Vector3(0f, LabelHeight, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建生锈齿轮");

            var text = go.AddComponent<TextMesh>();
            text.text = RustyGear.LabelText(1f);
            text.font = font;
            text.fontSize = 64;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = LabelColor;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (font.material != null) renderer.sharedMaterial = font.material;
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
