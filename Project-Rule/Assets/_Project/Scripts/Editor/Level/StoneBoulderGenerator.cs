using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键生成**纯重力石头**的预制体：Dynamic 刚体、吃重力、碰到 CityButton 就按下去。
    /// </summary>
    /// <remarks>
    /// <para><b>它与 ⑫ 巨石断剑的区别</b>：⑫ 是「会碎、会露剑、有锁」的机关；
    /// 这个只是**一块石头** —— 没有碎、没有剑、没有锁。要机关用 ⑫，只要石头用 ⑬。</para>
    /// <para>外观沿用出生点那套石砖（放大 + 压暗 + 背后一圈黑影），与 ⑫ 的巨石同一套视觉。</para>
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑬ 巨石（纯重力石头，撞开关）</c> 生成
    /// <c>Prefabs/StoneBoulder.prefab</c>，关卡里直接拖。</para>
    /// </remarks>
    public static class StoneBoulderGenerator
    {
        /// <summary>一块**方的**石头（320px 贴图，带倒角 / 描边 / 裂纹）。</summary>
        private const string SpritePath = "Assets/_Project/Art/Sprites/Stone/StoneSquare.png";
        private const string WhiteboxPath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        /// <summary>
        /// 石头**专用**物理材质：摩擦 0.5，**Combine = Minimum**。
        /// </summary>
        /// <remarks>
        /// ⚠️ 不能用木箱那块（`CrateMaterial2D`，Combine = Mean）：与地面（没材质，默认 0.4）取平均，
        /// 会把「摩擦 × 削弱」砍掉一半 —— 0.15 变成 0.275，静摩擦 81 N 还是超过玩家的 60 N，**推不动**。
        /// Minimum = 谁的摩擦小听谁的：石头自己的倍率改多少，接触面就用多少。
        /// </remarks>
        private const string MaterialPath = "Assets/_Project/Data/Physics/StoneMaterial2D.asset";

        /// <summary>石头必须落这个层：玩家的 Slide 与接地探测只认它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "StoneBoulder";
        private const string ArtName = "Art";

        /// <summary>白盒尺寸（世界单位）。与 ⑫ 的巨石一样大：2.4 ≈ 5×5 格。</summary>
        private static readonly Vector2 Size = new Vector2(2.4f, 2.4f);

        /// <summary>
        /// 质量（kg）。**故意卡在「正常推不动、摩擦一减小就推得动」的区间里**：
        /// 玩家推力 60 N，静摩擦 = μ·m·g。30 kg 时 ——
        /// 正常（μ=0.5）要 147 N → 推不动；「摩擦 × 削弱」（μ=0.15）只要 44 N → 推得动；
        /// 「摩擦 × 失效」（μ=0）一推就滑。
        /// </summary>
        private const float Mass = 30f;

        private static readonly Color StoneColor = new Color(0.94f, 0.92f, 0.90f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑬ 巨石（纯重力石头，撞开关）", false, 63)]
        public static void CreatePrefab()
        {
            GameObject prefab = SavePrefab(PrefabPath);
            if (prefab == null) return;

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"[地图组件] 已生成预制体 {PrefabPath}：{Size.x} × {Size.y} / {Mass} kg，碰到 CityButton 会自动按下。\n" +
                      "· 用法：拖进场景，Play 后自己落到地面；「重力 × 反转」会把它举起来\n" +
                      "· 推不动？故意的：正常摩擦（μ0.4）要 118 N，玩家只有 60 N；「摩擦 × 削弱」（μ0.15）压到 44 N 就推得动了\n" +
                      "· 想让它按开关：把 CityButton 摆在它会碰到的地方即可（按钮是 Trigger，不用改任何设置）\n" +
                      "· 想要「会碎 + 露剑」的机关：用菜单 ⑫ 巨石断剑，不是这个");
        }

        /// <summary>石头预制体。</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/StoneBoulder.prefab";

        /// <summary>把石头装成预制体（旁边开个临时空场景装，不碰当前场景）。</summary>
        public static GameObject SavePrefab(string assetPath)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene temp = default;

            try
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temp);
            }
            catch (System.InvalidOperationException)
            {
                temp = default;   // 当前场景没保存过 —— 退回落地方案
            }

            GameObject root = null;
            try
            {
                root = new GameObject(RootName);
                if (temp.IsValid()) SceneManager.MoveGameObjectToScene(root, temp);

                if (!Build(root)) return null;

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                if (temp.IsValid()) EditorSceneManager.CloseScene(temp, true);
                else if (root != null) Object.DestroyImmediate(root);

                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        /// <summary>往 <paramref name="root"/> 上装一个石头。返回 false = 资源缺失（已打日志）。</summary>
        private static bool Build(GameObject root)
        {
            Sprite stone = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (stone == null)
            {
                Debug.LogError("[地图组件] 找不到石头外观：" + SpritePath);
                stone = AssetDatabase.LoadAssetAtPath<Sprite>(WhiteboxPath);
                if (stone == null) return false;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，石头放下去谁也碰不到。");
                return false;
            }

            root.layer = layer;

            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;

            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(MaterialPath);
            if (material == null)
            {
                Debug.LogWarning("[地图组件] 找不到 " + MaterialPath + "（摩擦 Combine=Minimum），" +
                                 "退回木箱材质 —— 「摩擦 × 削弱」可能推不动这块石头。");
            }
            if (material != null) box.sharedMaterial = material;   // 也是 RuleAffectedBody 的摩擦克隆来源

            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.mass = Mass;
            body.gravityScale = 1f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // 受规则影响：质量规则写进 mass（重力规则是全局的，不需要它）。
            root.AddComponent<RuleAffectedBody>();
            root.AddComponent<StoneBody>();

            BuildPart(root.transform, ArtName, stone, Size, StoneColor, -1);

            return true;
        }

        /// <summary>按**目标世界尺寸**摆一块精灵（石砖是 65px/128PPU ≈ 0.51 单位，不能当 1×1 用）。</summary>
        private static void BuildPart(Transform parent, string name, Sprite sprite, Vector2 size, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Vector2 native = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
            if (native.x <= 0.001f) native.x = 1f;
            if (native.y <= 0.001f) native.y = 1f;

            var scale = new Vector3(size.x / native.x, size.y / native.y, 1f);
            go.transform.localScale = scale;

            // 按 pivot 把几何中心摆到原点（pivot 不在中心时也不会偏）。
            if (sprite != null)
            {
                Vector2 center = sprite.bounds.center;
                go.transform.localPosition = new Vector3(-center.x * scale.x, -center.y * scale.y, 0f);
            }

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
        }
    }
}