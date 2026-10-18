using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一套**巨石断剑机关**：巨石 + 碎块模板 + 藏着的剑。
    /// ⚠️ **按钮不打包进来** —— 关卡里单独拖一个 <c>Prefabs/BrickCity/CityButton.prefab</c>。
    /// </summary>
    /// <remarks>
    /// <para><b>这套机关怎么玩</b>（策划 2026-10-18 提出）：
    /// ①「重力 × 反转」把巨石举起来 → ② 巨石撞响天花板按钮、自己被震裂 →
    /// ③ 规则结束、重力恢复，巨石砸回地面碎成一圈碎块 → ④ 碎块里露出插着的剑 →
    /// ⑤「摩擦 × 反转」之后走近即可拔剑。四个组件各管一段，见 <see cref="GravityBoulder"/> /
    /// <see cref="SwordInStone"/> / <see cref="BoulderFragment"/> 与 <see cref="ImpactButton"/> 的类注释。</para>
    ///
    /// <para><b>生成出来的层级</b>（根 scale 恒为 1，所有尺寸都写在世界单位上）：
    /// <code>
    /// BoulderSwordMechanism          ← 根：摆在「巨石中心静止时」的位置
    /// ├─ Boulder                     ← Ground 层、Dynamic、40 kg
    /// │   ├─ Art / Label
    /// │   └─ FragmentTemplate        ← **禁用中**，碎开时按它克隆一圈
    /// └─ Sword                       ← **开局禁用**，碎开时移到巨石底部正中
    ///     ├─ Art（Blade / Guard / Hilt）
    ///     └─ Label
    /// </code>
    /// ⚠️ <b>按钮不在预制体里</b>：关卡里单独拖一个 <c>Prefabs/BrickCity/CityButton.prefab</c>
    /// 到巨石上方 <see cref="LiftHeight"/> 处。巨石升上去碰到它就按下 + 开裂
    /// （CityButton 的碰撞体是 <b>Trigger</b>，所以巨石会<b>穿过</b>它继续上升 —— 天花板要自己摆）。</para>
    ///
    /// <para>⚠️ <b>摆位</b>：根节点要放在**地面上方 <c>BoulderSize.y / 2</c> = 1.2 处**
    /// （巨石底面贴地、中心落在根上）。摆歪了也不会坏 —— 巨石是动态刚体，
    /// Play 之后自己会落到地面；只是上面那个 CityButton 的高度会跟着一起偏。</para>
    ///
    /// <para>⚠️ <b>质量 40 kg</b>：玩家的推力上限约 12 kg（见 <c>PushableCrateBuilder</c>），
    /// 而且巨石**没有挂 <c>PushableBody</c>**，所以推是推不动的 —— 它只能被规则举起来。</para>
    ///
    /// <para><b>它是预制体，不是散件</b>：菜单 <c>Tools/规则游戏/地图组件/⑫ 巨石断剑（生成预制体）</c>
    /// 生成 <c>Assets/_Project/Prefabs/BoulderSwordMechanism.prefab</c>，关卡里**直接拖这个预制体** ——
    /// 整关只用一两次的机关都走预制体（见 <c>Doc/关卡组件一览.md</c> §3）。
    /// 另外两个菜单：⑫b 放进当前场景（不想用预制体时）、⑫c 生成试验场景。</para>
    /// </remarks>
    public static class BoulderSwordBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";

        /// <summary>
        /// 巨石的**外观**：一块**方的**石头（320px 贴图，带倒角 / 描边 / 裂纹），不等美术。
        /// 碎块也用同一张 —— 碎开来就是一堆石渣。
        /// </summary>
        private const string BoulderSpritePath = "Assets/_Project/Art/Sprites/Stone/StoneSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";
        private const string MaterialPath = "Assets/_Project/Data/Physics/CrateMaterial2D.asset";

        /// <summary>巨石与按钮共用的层：实心、可站、能被动量探测到。</summary>
        private const string SolidLayerName = "Ground";

        private const string RigName = "BoulderSwordMechanism";
        private const string BoulderName = "Boulder";
        private const string FragmentName = "FragmentTemplate";
        private const string SwordName = "Sword";

        private const string ArtName = "Art";
        private const string LabelName = "Label";
        private const string BladeName = "Blade";
        private const string GuardName = "Guard";
        private const string HiltName = "Hilt";

        /// <summary>巨石白盒尺寸（世界单位）。1 格 = 0.5 单位，所以 2.4 ≈ 5×5 格。</summary>
        public static readonly Vector2 BoulderSize = new Vector2(2.4f, 2.4f);

        /// <summary>巨石质量（kg）。远超玩家 12 kg 的推力上限 —— 推不动，只能举。</summary>
        public const float BoulderMass = 40f;

        /// <summary>天花板按钮相对巨石中心的高度。落体约 4.3 米 ≈ 落地速度 9.2，够砸碎（阈值 6）。</summary>
        public const float LiftHeight = 6f;

        /// <summary>巨石静止时中心离地多高 —— 摆位用（根节点就该放这么高）。</summary>
        public static float RestHeight => BoulderSize.y * 0.5f;

        private static readonly Vector2 FragmentSize = new Vector2(0.5f, 0.5f);

        // 贴图本身已经调好色，本体不再压暗（震裂 / 锁死时才由 GravityBoulder 换色）。
        private static readonly Color BoulderColor = Color.white;
        private static readonly Color FragmentColor = new Color(0.86f, 0.84f, 0.82f, 1f);


        private static readonly Color BladeColor = new Color(0.86f, 0.88f, 0.92f, 1f);
        private static readonly Color GuardColor = new Color(0.72f, 0.60f, 0.30f, 1f);
        private static readonly Color HiltColor = new Color(0.42f, 0.30f, 0.22f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        /// <summary>机关预制体。**关卡里拖它**（见类注释）。</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/BoulderSwordMechanism.prefab";

        /// <summary>天花板按钮：用积木城既有的 CityButton，**不打包进机关预制体**。</summary>
        public const string CityButtonPath = "Assets/_Project/Prefabs/BrickCity/CityButton.prefab";

        // ==================================================================
        //  菜单
        // ==================================================================

        [MenuItem("Tools/规则游戏/地图组件/⑫ 巨石断剑（生成预制体）", false, 60)]
        public static void CreatePrefab()
        {
            GameObject prefab = SavePrefab(PrefabPath);
            if (prefab == null) return;

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"[地图组件] 已生成预制体 {PrefabPath}：巨石 {BoulderSize.x} × {BoulderSize.y} / {BoulderMass} kg。\n" +
                      $"· 用法：拖进场景，**让根节点离地 {RestHeight:0.#}**（巨石底面贴地）\n" +
                      $"· 还要摆**两个**按钮：拖 {CityButtonPath} 到巨石上方 {LiftHeight} 处（震裂用），\n" +
                      "  再拖一个到地面（**解锁用**）—— 把**巨石**拖到这个按钮的 FollowMotor 上（与配 Gate 一样），\n" +
                      "  并且勾上那个按钮的 PressByPlayerProximity（玩家走进去就按下）\n" +
                      "· 玩法：先踩解锁按钮 → 重力反转举起来碰到天花板按钮 → 震裂 → 落下来碎开 → 摩擦反转拔剑\n" +
                      "· ⚠️ 不绑解锁按钮的话石头会**一直锁着**（Console 会警告）");
        }

        [MenuItem("Tools/规则游戏/地图组件/⑫b 巨石断剑（放进当前场景）", false, 61)]
        public static void CreateRig()
        {
            var root = CreateRoot(SceneViewPivot());

            if (BuildInto(root) == null)
            {
                Undo.DestroyObjectImmediate(root);
                return;
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已放进场景 {RigName}（{BoulderSize.x} × {BoulderSize.y} / {BoulderMass} kg）。\n" +
                      $"· 摆位：让根节点离地 {RestHeight:0.#}（巨石底面贴地）\n" +
                      $"· 还要摆一个按钮：拖 {CityButtonPath} 到巨石上方 {LiftHeight} 处\n" +
                      "· 关卡里一般不用这个菜单 —— 拖预制体就行（菜单 ⑫）");
        }

        /// <summary>建一个空的机关根（给试验场景与其它工具复用）。</summary>
        /// <param name="registerUndo">存预制体时传 false —— 临时物体不该进别人的撤销栈。</param>
        public static GameObject CreateRoot(Vector3 position, bool registerUndo = true)
        {
            var root = new GameObject(RigName);
            root.transform.position = position;
            if (registerUndo) Undo.RegisterCreatedObjectUndo(root, "创建巨石断剑机关");
            return root;
        }

        // ==================================================================
        //  预制体
        // ==================================================================

        /// <summary>
        /// 把整套机关装成一个**预制体资产**，返回预制体根；失败返回 <c>null</c>（已打日志）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么在临时场景里装</b>：预制体必须从场景里的对象存出去，而当前场景是别人的工作现场 ——
        /// 往里塞临时物体会把场景标脏、还会进撤销栈。所以先在旁边开一个空场景装好，存完就关掉。</para>
        /// <para>当前场景是**未命名的未保存场景**时，Unity 不允许加开场景（会报
        /// "Cannot create a new scene additively with an untitled scene unsaved"），
        /// 这时退回「就在当前场景里装、存完立刻删掉」—— 代价是当前场景被标脏。</para>
        /// </remarks>
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
                temp = default;   // 当前场景没保存过，退回落地方案
            }

            try
            {
                GameObject root = new GameObject(RigName);
                if (temp.IsValid()) SceneManager.MoveGameObjectToScene(root, temp);

                if (BuildInto(root, registerUndo: false) == null)
                {
                    Object.DestroyImmediate(root);
                    return null;
                }

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                if (temp.IsValid()) EditorSceneManager.CloseScene(temp, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        // ==================================================================
        //  装配
        // ==================================================================

        /// <summary>
        /// 把整套机关装进 <paramref name="root"/>。返回巨石组件；<c>null</c> 表示装不起来
        /// （白盒方块或 Ground 层缺失，已打日志）。
        /// </summary>
        public static GravityBoulder BuildInto(GameObject root, bool registerUndo = true)
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，巨石既站不住也撞不到按钮。");
                return null;
            }

            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，白盒标签会显示不出来。");
            }

            // 石头的外观：借出生点的石砖；找不到就退回白盒方块（不至于没图）。
            Sprite stone = AssetDatabase.LoadAssetAtPath<Sprite>(BoulderSpritePath);
            if (stone == null)
            {
                Debug.LogWarning("[地图组件] 找不到石头外观 " + BoulderSpritePath + "，退回白盒方块。");
                stone = sprite;
            }

            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(MaterialPath);
            if (material == null)
            {
                Debug.LogWarning("[地图组件] 找不到 " + MaterialPath +
                                 "，巨石会用引擎默认摩擦，且 RuleAffectedBody 可能找不到摩擦克隆来源。");
            }

            // ---- 巨石（含碎块模板） ---------------------------------------
            Transform boulder = NewChild(root.transform, BoulderName, Vector3.zero, layer, registerUndo);
            Transform fragmentTemplate = BuildBoulder(boulder, stone, font, material, layer, registerUndo);

            // ---- 藏着的剑 -------------------------------------------------
            GameObject sword = BuildSword(root.transform, sprite, font, registerUndo);
            sword.SetActive(false);   // 藏在石头里：碎开之前谁也看不见

            // ---- 接线 ----------------------------------------------------
            var art = boulder.Find(ArtName).GetComponent<SpriteRenderer>();
            var label = boulder.Find(LabelName).GetComponent<TextMesh>();
            var script = boulder.GetComponent<GravityBoulder>();

            script.SetWhitebox(art, label);
            script.SetLinks(sword, fragmentTemplate);
            EditorUtility.SetDirty(script);

            return script;
        }

        /// <summary>巨石本体：碰撞箱 + 动态刚体 + 受规则影响 + 白盒 + 碎块模板。</summary>
        private static Transform BuildBoulder(Transform boulder, Sprite sprite, Font font,
                                              PhysicsMaterial2D material, int layer, bool registerUndo)
        {
            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = boulder.gameObject.AddComponent<BoxCollider2D>();
            box.size = BoulderSize;
            box.offset = Vector2.zero;
            if (material != null) box.sharedMaterial = material;   // 也是 RuleAffectedBody 的摩擦克隆来源

            // GravityBoulder 在运行期会把这几项再配一遍；这里先写好是为了 Inspector 里看得见。
            var body = boulder.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.mass = BoulderMass;
            body.gravityScale = 1f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // 受规则影响：质量规则直接写进 Rigidbody2D.mass（重力规则是全局的，不需要它）。
            boulder.gameObject.AddComponent<RuleAffectedBody>();
            boulder.gameObject.AddComponent<GravityBoulder>();

            // 贴图自己带了描边，不用再垫黑影。
            BuildSprite(boulder, ArtName, sprite, BoulderSize, Vector2.zero, BoulderColor, -1, registerUndo);

            BuildLabel(boulder, font, GravityBoulder.LabelText(GravityBoulder.State.Intact),
                       new Vector3(0f, BoulderSize.y * 0.5f + 0.45f, 0f), registerUndo);

            return BuildFragmentTemplate(boulder, sprite, material, layer, registerUndo);
        }

        /// <summary>
        /// 碎块模板：一个**禁用中**的小石渣。碎开时 <c>GravityBoulder</c> 按它克隆一圈。
        /// </summary>
        /// <remarks>⚠️ <b>故意没有碰撞体、也没有刚体</b>：瓦砾堆在剑周围，实心的话会把玩家卡住
        /// （实测过）。<c>BoulderFragment</c> 自己积分 + 射线落地，纯表现。
        /// 所以这里只挂一个 <c>BoulderFragment</c> 和一张精灵。</remarks>
        private static Transform BuildFragmentTemplate(Transform boulder, Sprite sprite,
                                                       PhysicsMaterial2D material, int layer, bool registerUndo)
        {
            Transform template = NewChild(boulder, FragmentName, Vector3.zero, layer, registerUndo);

            template.gameObject.AddComponent<BoulderFragment>();

            BuildSprite(template, ArtName, sprite, FragmentSize, Vector2.zero, FragmentColor, -1, registerUndo);

            template.gameObject.SetActive(false);   // 模板躺在巨石里只当图纸用
            return template;
        }

        /// <summary>藏在石头里的剑：原点 = 剑尖（插进地面的那一点），美术全在 <c>Art</c> 下面。</summary>
        private static GameObject BuildSword(Transform root, Sprite sprite, Font font, bool registerUndo)
        {
            Transform sword = NewChild(root, SwordName, new Vector3(0f, -RestHeight, 0f), -1, registerUndo);

            // Art 是「会动的那一块」：拔出动作让它绕剑尖升起 + 旋转。
            Transform art = NewChild(sword, ArtName, Vector3.zero, -1, registerUndo);

            BuildSprite(art, BladeName, sprite, new Vector2(0.22f, 1.5f), new Vector2(0f, 0.75f), BladeColor, 1, registerUndo);
            BuildSprite(art, GuardName, sprite, new Vector2(0.62f, 0.14f), new Vector2(0f, 1.52f), GuardColor, 2, registerUndo);
            BuildSprite(art, HiltName, sprite, new Vector2(0.14f, 0.42f), new Vector2(0f, 1.80f), HiltColor, 2, registerUndo);

            TextMesh label = BuildLabel(sword, font, SwordInStone.LabelText(false), new Vector3(0f, 2.35f, 0f), registerUndo);

            var script = sword.gameObject.AddComponent<SwordInStone>();
            script.SetWhitebox(art, label);
            EditorUtility.SetDirty(script);

            return sword.gameObject;
        }

        // ==================================================================
        //  白盒零件
        // ==================================================================

        /// <summary>一块白盒方块。方块精灵是 1×1 单位，缩放多少就是世界尺寸多少。</summary>
        private static Transform BuildSprite(Transform parent, string name, Sprite sprite,
                                             Vector2 size, Vector2 localPosition, Color color, int order,
                                             bool registerUndo = true)
        {
            Transform part = NewChild(parent, name, localPosition, -1, registerUndo);

            // ⚠️ 不能假设精灵是 1×1 单位：石砖是 65px / 128 PPU ≈ 0.51 单位。
            //    缩放要拿 sprite.bounds.size 换算，并且按 pivot 把**几何中心**摆到 localPosition 上。
            Vector2 native = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
            if (native.x <= 0.001f) native.x = 1f;
            if (native.y <= 0.001f) native.y = 1f;

            var scale = new Vector3(size.x / native.x, size.y / native.y, 1f);
            part.localScale = scale;

            if (sprite != null)
            {
                Vector2 center = sprite.bounds.center;
                part.localPosition = localPosition - new Vector2(center.x * scale.x, center.y * scale.y);
            }

            var renderer = part.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;

            return part;
        }

        private static TextMesh BuildLabel(Transform parent, Font font, string text, Vector3 localPosition,
                                           bool registerUndo = true)
        {
            Transform holder = NewChild(parent, LabelName, localPosition, -1, registerUndo);

            var mesh = holder.gameObject.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = 0.05f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = LabelColor;

            var renderer = holder.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (font != null && font.material != null) renderer.sharedMaterial = font.material;
                // 标签在白盒阶段要永远看得见，压在所有东西上面。
                renderer.sortingOrder = 100;
            }

            return mesh;
        }

        private static Transform NewChild(Transform parent, string name, Vector3 localPosition, int layer,
                                          bool registerUndo = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            if (layer >= 0) go.layer = layer;
            if (registerUndo) Undo.RegisterCreatedObjectUndo(go, "创建巨石断剑机关");
            return go.transform;
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
