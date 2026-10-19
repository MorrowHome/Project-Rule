using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Presentation.SceneComponents;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 生成 **楼层电梯 + 随车拉杆** 预制体（<c>Prefabs/BrickCity/CityFloorLift.prefab</c>）。
    /// </summary>
    /// <remarks>
    /// <para><b>刻意与 <c>CityPathMotor</c> 那套分开</b>：场景里已有的两站电梯不动一格，
    /// 这里是新组件 <see cref="CityMultiStopLift"/> + <see cref="CityFloorLever"/> 的独立预制体。</para>
    ///
    /// <para><b>层级</b>：容器（无刚体）→ <c>Car</c>（Ground 层，Kinematic + 实心箱 + 3 站）
    /// 与 <c>Lever</c>（Default 层，Kinematic + Trigger + 3 档）。拉杆是**兄弟**，由
    /// <c>CityMultiStopLift._carriedBodies</c> 带着走 —— 挂成子物体的话会晚一个物理步。</para>
    ///
    /// <para><b>美术</b>：复用功能组件交付图（<c>Art/Sprites/Components</c>，由 <c>CityArtSetup</c> 导入）：
    /// 车厢 = 飞机两帧（<c>CityLift_Plane_01/02</c>，<c>SpriteFlipbook</c> 循环螺旋桨），
    /// 拉杆 = 摇杆五帧（<c>CityLever_01..05</c>，<c>CityFloorLeverSpriteView</c> 按档位前后播）。
    /// 对齐方式与积木城其它组件一致：飞机按**顶边**对齐碰撞箱顶面（人站在机顶），拉杆按**底边**落在机顶。</para>
    ///
    /// <para>生成过程在一张**临时的 additive 空场景**里做，不弄脏当前打开的场景。</para>
    /// </remarks>
    public static class CityFloorLiftPrefabBuilder
    {
        // 生成器版本标记：改内容能让 Unity 按内容哈希重新编译（它不看 mtime）。
        private const int BuilderRevision = 2;

        private const string PrefabPath = "Assets/_Project/Prefabs/BrickCity/CityFloorLift.prefab";
        private const string ArtFolder = "Assets/_Project/Art/Sprites/Components";
        private const string SolidLayerName = "Ground";

        private const string RootName = "CityFloorLift";
        private const string CarName = "Car";
        private const string LeverName = "Lever";
        private const string ArtName = "Visual";

        /// <summary>
        /// 车厢碰撞箱：**1.3 × 0.5** —— 关卡竖井都是 3 格宽（Grid 格 0.5 米 = 1.5 米），
        /// 每边留 0.1 米余量。飞机精灵按这个宽度等比缩放，**不要靠缩放实例**（那会把站点位移一起缩放）。
        /// </summary>
        private static readonly Vector2 CarSize = new Vector2(1.3f, 0.5f);

        /// <summary>拉杆的交互盒（Trigger）：罩住底座，够攻击盒/交互圈判定。</summary>
        private static readonly Vector2 LeverSize = new Vector2(0.7f, 0.75f);

        /// <summary>
        /// 选档半宽：车厢只有 1.3 米，玩家能站的范围约 ±0.4 —— 取 0.45，三档各 0.3 米，左右两档都够得着。
        /// </summary>
        private const float LeverSelectHalfWidth = 0.45f;

        /// <summary>
        /// 飞机精灵里**机舱顶**的局部 y（相对精灵轴心，单位：世界单位）。
        /// </summary>
        /// <remarks>0.75 是从交付图量出来的：机舱顶在轴心上方 0.75，而尾翼顶到 0.894。
        /// 要让「人站在机顶」，对齐机舱顶而不是包围盒顶；换美术后重新量一次。</remarks>
        private const float PlaneRoofLocalY = 0.75f;

        /// <summary>拉杆在车厢上的位置：机舱顶正中（车顶 0.25 + 交互盒半高 0.375）。</summary>
        private static readonly Vector3 LeverLocal = new Vector3(0f, 0.625f, 0f);

        /// <summary>默认三站：地面 / 4 米 / 8 米（相对预制体摆放位置）。</summary>
        private static readonly Vector2[] DefaultStops =
        {
            Vector2.zero,
            new Vector2(0f, 4f),
            new Vector2(0f, 8f),
        };

        /// <summary>默认三档角度（接旋转把手时用；接交付美术后把手由 <c>CityFloorLeverSpriteView</c> 换帧）。</summary>
        private static readonly float[] DefaultAngles = { 25f, -35f, -95f };

        private static readonly string[] PlaneSprites = { "CityLift_Plane_01", "CityLift_Plane_02" };

        /// <summary>美工交付的 5 帧：1 = 最右、3 = 竖直、5 = 最左。</summary>
        private static readonly string[] LeverSprites =
        {
            "CityLever_01", "CityLever_02", "CityLever_03", "CityLever_04", "CityLever_05",
        };

        [MenuItem("Tools/规则游戏/积木城/⑮ 楼层电梯（拉杆随车走）", false, 61)]
        public static void CreatePrefab()
        {
            Sprite planeIdle = Load(PlaneSprites[0]);
            Sprite leverRest = Load(LeverSprites[0]);
            if (planeIdle == null || leverRest == null)
            {
                Debug.LogError("[楼层电梯] 找不到交付美术（需要 " + ArtFolder + " 里的 CityLift_Plane_* / CityLever_*）。");
                return;
            }

            int solidLayer = LayerMask.NameToLayer(SolidLayerName);
            if (solidLayer < 0)
            {
                Debug.LogError("[楼层电梯] 工程里没有 " + SolidLayerName + " 层，车厢放下去玩家踩不到。");
                return;
            }

            // 临时场景里搭，搭完只留预制体，不碰当前打开的场景。
            Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            try
            {
                var root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, temp);

                // ---------------- 车厢 ----------------
                var car = new GameObject(CarName);
                car.transform.SetParent(root.transform, false);
                car.layer = solidLayer;

                var carBody = car.AddComponent<Rigidbody2D>();
                carBody.bodyType = RigidbodyType2D.Kinematic;
                carBody.gravityScale = 0f;
                carBody.interpolation = RigidbodyInterpolation2D.Interpolate;

                var carShape = car.AddComponent<BoxCollider2D>();
                carShape.size = CarSize;

                car.AddComponent<RuleAffectedBody>();

                var lift = car.AddComponent<CityMultiStopLift>();
                lift.SetStops(DefaultStops);

                // 飞机：**机舱顶**对齐碰撞箱顶面 —— 人站在机顶，机身垂在平台下方。
                // 精灵等比缩到车宽（机翼不再超出 3 格竖井），机舱顶的局部高度也跟着缩。
                float planeScale = CarSize.x / planeIdle.bounds.size.x;
                var carArt = BuildVisual(car.transform, planeIdle, 2);
                carArt.localScale = new Vector3(planeScale, planeScale, 1f);
                carArt.localPosition = new Vector3(0f, CarSize.y * 0.5f - PlaneRoofLocalY * planeScale, 0f);

                var propeller = carArt.gameObject.AddComponent<SpriteFlipbook>();
                propeller.Target = carArt.GetComponent<SpriteRenderer>();
                propeller.Frames = LoadAll(PlaneSprites);
                propeller.FramesPerSecond = 14f;

                // ---------------- 拉杆（兄弟，不是子物体） ----------------
                var lever = new GameObject(LeverName);
                lever.transform.SetParent(root.transform, false);
                lever.transform.localPosition = LeverLocal;
                lever.layer = 0;

                var leverBody = lever.AddComponent<Rigidbody2D>();
                leverBody.bodyType = RigidbodyType2D.Kinematic;
                leverBody.gravityScale = 0f;
                leverBody.interpolation = RigidbodyInterpolation2D.Interpolate;

                var leverShape = lever.AddComponent<BoxCollider2D>();
                leverShape.size = LeverSize;
                leverShape.isTrigger = true;

                lever.AddComponent<RuleAffectedBody>();

                var floorLever = lever.AddComponent<CityFloorLever>();
                floorLever.Prompt = "拨动拉杆";

                // 摇杆交付图：轴心在底边，落到碰撞箱底（= 车厢顶面）。
                var leverArt = BuildVisual(lever.transform, leverRest, 2);
                leverArt.localPosition = new Vector3(0f, -LeverSize.y * 0.5f, 0f);

                var view = leverArt.gameObject.AddComponent<CityFloorLeverSpriteView>();
                view.Lever = floorLever;
                view.Target = leverArt.GetComponent<SpriteRenderer>();
                view.Frames = LoadAll(LeverSprites);
                view.FramesPerSecond = 22f;

                // 交付美术是**换帧**表现的，所以不走旋转把手。
                floorLever.SetAngles(DefaultAngles);
                floorLever.SetSelectionWidth(LeverSelectHalfWidth);
                floorLever.SetWiring(null, lift);

                // 车带着拉杆走：同一个位移，不靠父子关系。
                lift.SetCarriedBodies(new[] { leverBody });

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null)
                {
                    Debug.LogError("[楼层电梯] 预制体保存失败：" + PrefabPath);
                    return;
                }

                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);

                Debug.Log("[楼层电梯] 已生成 " + PrefabPath +
                          "\n· 车箱 1.3 × 0.5（3 格 = 1.5 米的竖井留 0.1 余量）；飞机精灵按车宽等比缩放" +
                          "\n· 三站默认 (0,0) / (0,4) / (0,8)；站点是**相对摆放位置**的偏移" +
                          "\n· 车厢用 CityLift_Plane_01/02（螺旋桨 14fps），拉杆用 CityLever_01..05（按档换帧 22fps）" +
                          "\n· 拉杆是车箱的兄弟节点，由 CityMultiStopLift 的随车刚体数组带着走（别改成子物体）" +
                          "\n· 车厢在 Ground 层、拉杆在 Default 层；拉杆选择半宽默认 1.2 米（左中右各 0.8 米）");
            }
            finally
            {
                EditorSceneManager.CloseScene(temp, true);
            }
        }

        /// <summary>建一个不带碰撞的 Visual 子物体（精灵按自身 PPU 尺寸，不做缩放）。</summary>
        private static Transform BuildVisual(Transform parent, Sprite sprite, int sortingOrder)
        {
            var art = new GameObject(ArtName);
            art.transform.SetParent(parent, false);
            art.transform.localScale = Vector3.one;

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.sortingOrder = sortingOrder;
            return art.transform;
        }

        private static Sprite Load(string name)
        {
            string path = ArtFolder + "/" + name + ".png";
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
                if (asset is Sprite sprite && sprite.name == name) return sprite;

            // 兜底：有些导入模式下主资产就是 Sprite。
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite[] LoadAll(string[] names)
        {
            var frames = new Sprite[names.Length];
            for (int i = 0; i < names.Length; i++) frames[i] = Load(names[i]);
            return frames;
        }
    }
}
