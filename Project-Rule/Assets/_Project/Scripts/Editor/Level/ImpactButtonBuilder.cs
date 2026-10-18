using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**巨大按钮**（动量触发的机关）：比玩家宽的实心台子 + 一块会下沉的帽。
    /// </summary>
    /// <remarks>
    /// <para><b>什么时候用</b>：要「砸」或「撞」才响的开关 —— 走上去不算数。
    /// 「质量 × 速度」沿按压方向超过阈值才触发，与压力板的区别见
    /// <see cref="ImpactButton"/> 的类注释。</para>
    ///
    /// <para>⚠️ <b>落 <c>Ground</c> 层</b>（层 8）：按钮是实心的，玩家能站在上面、
    /// 箱子能压在它上面，动量才算得准。放错层就是一块看得见却穿得过去的空气。</para>
    ///
    /// <para><b>阈值默认 20（kg·单位/秒）</b>：参考 —— 玩家走路 8（撞不动）、冲刺 26、
    /// 铁箱推着走 ≈24、掉下来砸 240。要改在 Inspector 的「触发」栏里改。</para>
    ///
    /// <para><b>按下之后触发什么还没定</b>：把接收方拖到 Inspector 的「按下时」里
    /// （UnityEvent，不写代码），或者代码里订阅 <c>OnPressed</c>。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑨ 巨大按钮（动量触发机关）。</para>
    /// </remarks>
    public static class ImpactButtonBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>按钮本体层：实心、可站 —— 与其余地图组件一致。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "ImpactButton";
        private const string BaseName = "Base";
        private const string CapName = "Cap";
        private const string LabelName = "Label";

        /// <summary>白盒尺寸（世界单位）。比玩家宽 —— 「巨大」按钮，人和箱子砸上去都盖得住。</summary>
        private static readonly Vector2 Size = new Vector2(2f, 1f);

        /// <summary>按钮帽：比底座窄一圈，看着像按得下去的东西。</summary>
        private static readonly Vector2 CapSize = new Vector2(1.6f, 0.45f);

        /// <summary>帽的弹起位置：顶面与按钮顶面齐平（0.5 − 帽高 ÷ 2）。</summary>
        private static readonly float CapRestY = Size.y * 0.5f - CapSize.y * 0.5f;

        private const float LabelHeight = 0.9f;

        private static readonly Color BaseColor  = new Color(0.45f, 0.47f, 0.52f, 1f);
        private static readonly Color CapColor   = new Color(0.78f, 0.26f, 0.24f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑨ 巨大按钮（动量触发机关）", false, 58)]
        public static void CreateButton()
        {
            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "创建巨大按钮");

            if (Build(root, OneWayGate.PassDirection.ToDown) == null)
            {
                Undo.DestroyObjectImmediate(root);   // 装不起来就别留一个空物体在场景里
                return;
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（{SolidLayerName} 层）：{Size.x} × {Size.y} 单位，" +
                      "**以物体原点为中心**（上下各 " + (Size.y * 0.5f) + "）。\n" +
                      "· 触发阈值在 Inspector 的「触发」栏（默认 20 ≈ 冲刺撞得动、走路撞不动）\n" +
                      "· 按压方向默认「朝下」（被从上方砸）；装墙上横着撞就改这一栏\n" +
                      "· 按下之后触发什么还没定：把接收方拖进「按下时」即可");
        }

        /// <summary>
        /// 在一个**现成的**物体上装一个巨大按钮，返回按钮组件；装不起来（白盒方块 / 层缺失）返回 <c>null</c>。
        /// </summary>
        /// <remarks>
        /// <para>抽出来是给别的**复合机关**复用的：巨石断剑机关天花板上的那个按钮就是它
        /// （见 <c>BoulderSwordBuilder</c>）。菜单入口只负责建根物体与选中，装配全在这里，
        /// 免得两处白盒写歪。</para>
        /// <para>调用方负责摆位置、负责给根物体登记 <c>Undo</c>；子物件由本方法登记。</para>
        /// </remarks>
        public static ImpactButton Build(GameObject root, OneWayGate.PassDirection press)
        {
            if (root == null) return null;

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return null;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，按钮放下去谁也撞不到它。");
                return null;
            }

            root.layer = layer;

            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;

            ImpactButton button = root.AddComponent<ImpactButton>();

            BuildPart(root.transform, BaseName, sprite, Size, Vector2.zero, BaseColor, -1);
            Transform cap = BuildPart(root.transform, CapName, sprite, CapSize,
                                      new Vector2(0f, CapRestY), CapColor, 0);
            TextMesh label = BuildLabel(root.transform);

            // 帽与标签的引用在这里写进组件（SetWhitebox 会顺手记下帽的弹起位置）。
            button.SetWhitebox(cap, label);
            button.SetPress(press);
            EditorUtility.SetDirty(button);

            return button;
        }

        /// <summary>一块白盒方块。方块精灵是 1×1 单位，缩放多少就是世界尺寸多少。</summary>
        private static Transform BuildPart(Transform parent, string name, Sprite sprite,
                                           Vector2 size, Vector2 localPosition, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            Undo.RegisterCreatedObjectUndo(go, "创建巨大按钮");

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
            // 根 scale 是 1，所以这里的局部坐标就是世界距离；写在按钮顶面之上。
            go.transform.localPosition = new Vector3(0f, LabelHeight, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建巨大按钮");

            var text = go.AddComponent<TextMesh>();
            text.text = ImpactButton.LabelText(20f);
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
