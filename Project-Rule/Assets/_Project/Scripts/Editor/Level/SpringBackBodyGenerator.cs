using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键生成**回弹块**的预制体：被打就沿自己的轨道退开、然后按摩擦快慢慢慢回正。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是预制体</b>：这类块在关卡里要**摆很多个**（迷宫里当活动挡板 / 竖井里当踏板），
    /// 每个都要「实心碰撞体 + Kinematic 刚体 + 受击箱 + 轨道」齐全，手搭一遍错一处就是
    /// 「砍上去没反应」或者「人穿过去了」。生成一次、拖 N 次最省事。</para>
    ///
    /// <para><b>四件套缺一不可</b>：</para>
    /// <list type="number">
    /// <item><b>实心碰撞体 + <c>Ground</c> 层</b>（层 8）—— 玩家的 <c>Slide</c> / 接地探测只认这一层。</item>
    /// <item><b>Kinematic 刚体</b> —— 位移由 <see cref="SpringBackBody"/> 沿轨道自己解算。</item>
    /// <item><b>受击箱</b>（<c>Hurtbox</c> 层 10 上的 Trigger 子物体）—— 命中判定只查这一层。</item>
    /// <item><b>轨道</b>（默认水平 ±1.5 格）—— 块只在这条线上走，打不飞也推不歪。</item>
    /// </list>
    ///
    /// <para><b>轨道是相对锚点的偏移</b>，所以同一个预制体拖到关卡任何位置都自带正确轨道；
    /// 要改方向 / 长度就改 Inspector 里「轨道」那两栏（竖直轨道填 <c>(0, −1.5) / (0, 1.5)</c>），
    /// 白盒条会跟着重摆。默认给了 3 格水平行程。</para>
    ///
    /// <para><b>受击箱 = 身体尺寸（整块可打）</b>，<b>不</b>按 0.75 缩：这块会被关卡放大成宽板，
    /// 0.75 的边距会随缩放一起放大、超过玩家的攻击距离，表现是「贴着砍却打不到」。
    /// 要改手感改那个子物体的 size，别改身体碰撞体。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑭ 回弹块（轨道内受击击退 + 缓慢回正）</c> 生成
    /// <c>Prefabs/SpringBackBody.prefab</c>，关卡里直接拖。</para>
    /// </remarks>
    public static class SpringBackBodyGenerator
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";

        /// <summary>实体落这个层：玩家的 Slide 与接地探测只认它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "SpringBackBody";
        private const string ArtName = "Art";
        private const string TrackName = "Track";
        private const string HurtboxName = "Hurtbox";

        /// <summary>白盒尺寸（世界单位）：1 × 1 = 2 × 2 格。</summary>
        private static readonly Vector2 Size = new Vector2(1f, 1f);

        /// <summary>默认轨道：水平，左右各 1.5 格。</summary>
        private static readonly Vector2 DefaultTrackFrom = new Vector2(-1.5f, 0f);
        private static readonly Vector2 DefaultTrackTo = new Vector2(1.5f, 0f);

        private static readonly Color ArtColor   = new Color(0.52f, 0.76f, 0.98f, 1f);
        private static readonly Color TrackColor = new Color(0.22f, 0.24f, 0.32f, 0.9f);

        /// <summary>预制体路径。菜单只认这一个。</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/SpringBackBody.prefab";

        [MenuItem("Tools/规则游戏/地图组件/⑭ 回弹块（轨道内受击击退 + 缓慢回正）", false, 64)]
        public static void CreatePrefab()
        {
            GameObject prefab = SavePrefab(PrefabPath);
            if (prefab == null) return;

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"[地图组件] 已生成预制体 {PrefabPath}：{Size.x} × {Size.y} 单位，实心、Ground 层、带受击箱、轨道水平 ±1.5 格。\n" +
                      "· 用法：拖进场景，摆任意多个；被打沿轨道退开约 0.9 格（轻击）/ 1.9 格（重击），然后慢慢回正\n" +
                      "· **只能在轨道上走**：轨道方向 / 长度在 Inspector 的「轨道」两栏（竖直填 (0,-1.5)/(0,1.5)）\n" +
                      "· 回正速度默认 1.2 单位/秒，**摩擦规则直接改它**：×增强 → 慢到 1/3，×削弱 → 快 3 倍");
        }

        /// <summary>把回弹块装成预制体（旁边开个临时空场景装，不碰当前场景）。</summary>
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

        /// <summary>往 <paramref name="root"/> 上装一个回弹块。返回 false = 资源 / 层缺失（已打日志）。</summary>
        private static bool Build(GameObject root)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return false;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，回弹块放下去玩家会直接穿过去。");
                return false;
            }

            int hurtboxLayer = Hurtbox.Layer;
            if (hurtboxLayer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + Hurtbox.LayerName + " 层，回弹块打不到。" +
                               "先在 Project Settings > Tags and Layers 里补上这一层。");
                return false;
            }

            root.layer = layer;

            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;

            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // ⚠️ 组件要在受击箱**之前**加：受击箱的承受方（IDamageable）要显式指到它。
            var block = root.AddComponent<SpringBackBody>();
            block.SetTrack(DefaultTrackFrom, DefaultTrackTo);

            BuildArt(root.transform, sprite);
            Transform track = BuildTrack(root.transform, sprite);
            BuildHurtbox(root.transform, hurtboxLayer, block);

            block.SetWhitebox(track);
            EditorUtility.SetDirty(block);

            return true;
        }

        /// <summary>一块白盒方块。方块精灵是 1×1 单位，缩放多少就是世界尺寸多少。</summary>
        private static void BuildArt(Transform parent, Sprite sprite)
        {
            var go = new GameObject(ArtName);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(Size.x, Size.y, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 压在地面美术（order 1）和角色（order 0）后面：站上去时人是挡在块前面的。
            renderer.sortingOrder = -1;
        }

        /// <summary>
        /// 轨道白盒条：一根细长条，长度 / 角度 / 位置由 <see cref="SpringBackBody"/> 每帧按配置算。
        /// </summary>
        /// <remarks>初始缩放给个 1，Awake / OnValidate 会立刻按 3 格改写 —— 放场景里就能看见轨道。</remarks>
        private static Transform BuildTrack(Transform parent, Sprite sprite)
        {
            var go = new GameObject(TrackName);
            go.transform.SetParent(parent, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = TrackColor;
            // 压在块和地面美术后面：轨道是「地皮上的凹槽」，不该盖住任何东西。
            renderer.sortingOrder = -3;

            return go.transform;
        }

        /// <summary>
        /// 受击箱：独立子物体、Trigger、住在 <c>Hurtbox</c> 层（10），尺寸按身体缩一圈。
        /// </summary>
        /// <remarks>
        /// 承受方**显式指到根上的 <see cref="SpringBackBody"/>**：预制体资产里的子物体
        /// <c>activeInHierarchy</c> 是 false，<c>GetComponentInParent</c> 在那种状态下找不到父级
        /// （运行时能，但预制体预览 / 验证器里会解析成 null 并报错）。写死更稳。
        /// </remarks>
        private static void BuildHurtbox(Transform parent, int hurtboxLayer, MonoBehaviour owner)
        {
            var go = new GameObject(HurtboxName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.layer = hurtboxLayer;

            var area = go.AddComponent<BoxCollider2D>();

            // ⚠️ **和身体同尺寸**，不按 0.75 缩：这块是会被关卡**放大成宽板**的道具
            //    （实测 8 × 2）。0.75 缩出来的边距会跟着缩放一起放大（8 宽时边距 1 米），
            //    比玩家从身侧够得到的距离还远 —— 表现就是「贴着砍却打不到」，
            //    而且差半格就左右不对称（一边能打、一边打不到）。
            //    整块可打：从任意一侧砍在块面上都算命中。
            area.size = Size;
            area.offset = Vector2.zero;
            area.isTrigger = true;

            var hurt = go.AddComponent<Hurtbox>();

            // ⚠️ 显式写 _owner，不靠「父链上找 IDamageable」：
            //    预制体资产（LoadAssetAtPath / 预制体预览）里的子物体 **activeInHierarchy = false**，
            //    而 GetComponentInParent 默认不往 inactive 的父级找 —— 那里会解析成 null 并报错。
            var so = new SerializedObject(hurt);
            SerializedProperty ownerProperty = so.FindProperty("_owner");
            if (ownerProperty != null)
            {
                ownerProperty.objectReferenceValue = owner;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
