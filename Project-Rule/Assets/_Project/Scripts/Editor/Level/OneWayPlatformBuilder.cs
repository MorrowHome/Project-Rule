using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一块**跳穿平台**（单向、可翻转）。
    /// </summary>
    /// <remarks>
    /// <para>放在 Ground 层（玩家只扫这一层）。碰撞箱在根上、美术在 <c>Art</c> 子物体上，
    /// 和滑板车等组件同一套约定。</para>
    /// <para>「跳穿」的方向由 <see cref="OneWayPlatform"/> 决定：默认实心面背着重力，
    /// 重力规则一翻它自动跟着翻；也可以关掉跟随、直接转动物体。</para>
    /// </remarks>
    public static class OneWayPlatformBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";
        private const string SolidLayerName = "Ground";
        private const string RootName = "OneWayPlatform";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>1 格 = 0.5 单位：4 格宽 × 0.2 厚。</summary>
        private static readonly Vector2 Size = new Vector2(2f, 0.2f);

        private static readonly Color ArtColor = new Color(0.55f, 0.90f, 0.95f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑪ 跳穿平台（单向可翻转）", false, 41)]
        public static void Create()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null) { Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath); return; }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0) { Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，平台放下去玩家踩不到。"); return; }

            var root = new GameObject(RootName);
            root.transform.position = SceneViewPivot();
            root.layer = layer;
            Undo.RegisterCreatedObjectUndo(root, "创建跳穿平台");

            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;

            var art = new GameObject(ArtName);
            art.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建跳穿平台");
            art.transform.localScale = new Vector3(Size.x, Size.y, 1f);
            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            renderer.sortingOrder = -1;     // 和别的机关一致：在地形美术和角色之后

            BuildLabel(root.transform);

            var platform = root.AddComponent<OneWayPlatform>();
            // 把视觉子物体接给组件：重力翻转时要跟着翻面。
            var so = new SerializedObject(platform);
            so.FindProperty("_visual").objectReferenceValue = art.transform;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(platform);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log("[地图组件] 已创建跳穿平台（Ground 层）：从背面可以直接穿过去，从实心面落地。" +
                      "重力规则翻转时它会自动把实心面翻到反方向。");
        }

        private static void BuildLabel(Transform parent)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) { Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，跳过标签。"); return; }

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建跳穿平台");

            var text = go.AddComponent<TextMesh>();
            text.text = "跳穿平台";
            text.font = font;
            text.fontSize = 64;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = LabelColor;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && font.material != null) renderer.sharedMaterial = font.material;
            if (renderer != null) renderer.sortingOrder = 100;
        }

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
