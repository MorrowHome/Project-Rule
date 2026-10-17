using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure;

namespace RuleGame.Editor.Combat
{
    /// <summary>
    /// 给「能被打」的实体补受击箱（<see cref="Hurtbox"/>）。
    /// </summary>
    /// <remarks>
    /// <para>命中判定<b>只认受击箱</b>，所以每个实现 <see cref="IDamageable"/> 的对象都要有一个：
    /// 形状抄身体碰撞体、尺寸按 <see cref="DefaultRatio"/> 缩（**小于身体** = 判定比视觉严格）、
    /// 搬到受击层、设成 Trigger、挂 <c>Hurtbox</c> 并接好承受方。</para>
    ///
    /// <para><b>已经存在的受击箱一律不动</b> —— 只补缺的。手调过的大小不会被重跑覆盖；
    /// 要重新按比例生成就先把那个子物体删掉再跑。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/受击箱/*</c>。P4 以后加新敌人时先跑一遍预制体那条。</para>
    /// </remarks>
    public static class HurtboxBuilder
    {
        /// <summary>受击箱相对身体碰撞体的线性比例。</summary>
        /// <remarks>0.75 的线性 = 面积的 0.56：判定明显小于视觉，属于手感参数，按场景调。</remarks>
        public const float DefaultRatio = 0.75f;

        private const string HurtboxName = "Hurtbox";
        private const string PrefabFolder = "Assets/_Project/Prefabs";

        [MenuItem("Tools/规则游戏/受击箱/① 给当前场景里的可受伤实体补", false, 40)]
        public static void WireActiveScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var scene = EditorSceneManager.GetActiveScene();
            var report = new StringBuilder();
            report.Append("[受击箱] 场景: ").Append(scene.path).Append('\n');

            int created = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                created += Wire(root, report, true);

            if (created > 0) EditorSceneManager.MarkSceneDirty(scene);
            report.Append(created > 0 ? "共新建 " + created + " 个受击箱（Ctrl+Z 可还原）" : "没有需要新建的实体");
            Debug.Log(report.ToString());
        }

        [MenuItem("Tools/规则游戏/受击箱/② 给预制体补（" + PrefabFolder + "）", false, 41)]
        public static void WirePrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var report = new StringBuilder();
            report.Append("[受击箱] 预制体:\n");

            int created = 0;
            int files = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var lines = new StringBuilder();
                    int made = Wire(root, lines, false);
                    if (made == 0) continue;

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    created += made;
                    files++;
                    report.Append("  ").Append(path).Append('\n').Append(lines);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            report.Append(files > 0
                ? "共 " + files + " 个预制体、新建 " + created + " 个受击箱"
                : "没有需要新建的受击箱");
            Debug.Log(report.ToString());
        }

        // ==================================================================
        //  核心
        // ==================================================================

        /// <summary>
        /// 给**指定路径的场景**补受击箱，并直接存盘。
        /// </summary>
        /// <remarks>
        /// 附加方式打开 → 改 → 存 → 关：不会碰当前正在编辑的场景，也不会有「保存/放弃」弹窗。
        /// 场景本来就已经打开（含当前场景）时就地改，不重复打开。
        /// </remarks>
        public static void WireSceneAtPath(string scenePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            var report = new StringBuilder();
            report.Append("[受击箱] 场景: ").Append(scenePath).Append('\n');

            int created = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                created += Wire(root, report, true);

            if (created > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            if (opened) EditorSceneManager.CloseScene(scene, true);

            report.Append(created > 0 ? "共新建 " + created + " 个受击箱（已存盘）" : "没有需要新建的实体");
            Debug.Log(report.ToString());
        }

        /// <summary>给一棵树里所有「能被打」的对象补受击箱；返回新建的个数，逐条说明写进 <paramref name="report"/>。</summary>
        public static int Wire(GameObject root, StringBuilder report, bool undo)
        {
            int created = 0;
            foreach (GameObject entity in DamageableObjects(root))
                if (EnsureHurtbox(entity, report, undo)) created++;
            return created;
        }

        /// <summary>树里所有挂了 <see cref="IDamageable"/> 的对象（含未激活的子物体，按对象去重）。</summary>
        private static IEnumerable<GameObject> DamageableObjects(GameObject root)
        {
            var seen = new HashSet<GameObject>();
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !(behaviour is IDamageable)) continue;
                if (seen.Add(behaviour.gameObject)) yield return behaviour.gameObject;
            }
        }

        /// <summary>确保一个实体有受击箱。返回是否**新建**了。</summary>
        private static bool EnsureHurtbox(GameObject entity, StringBuilder report, bool undo)
        {
            string path = PathOf(entity);

            Collider2D body = FindBodyCollider(entity);
            if (body == null)
            {
                report.Append("  !! ").Append(path).Append("：本物体上没有身体碰撞体，跳过\n");
                return false;
            }

            Transform existing = entity.transform.Find(HurtboxName);
            if (existing != null && existing.GetComponent<Hurtbox>() != null)
                return false;                             // 已有受击箱：手调过的值不动

            GameObject box = existing != null ? existing.gameObject : new GameObject(HurtboxName);
            if (existing == null)
            {
                if (undo) Undo.RegisterCreatedObjectUndo(box, "补受击箱");
                box.transform.SetParent(entity.transform, false);
            }

            box.transform.localPosition = Vector3.zero;
            box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = Vector3.one;

            Collider2D area = box.GetComponent<Collider2D>();
            if (area == null) area = CreateArea(body, box);
            area.isTrigger = true;

            int layer = Hurtbox.Layer;
            if (layer >= 0) box.layer = layer;

            if (box.GetComponent<Hurtbox>() == null) box.AddComponent<Hurtbox>();
            AssignOwner(box, entity);

            report.Append("  · ").Append(path).Append("：受击箱 ").Append(DescribeArea(area))
                  .Append("（身体 ").Append(DescribeArea(body)).Append(" × ").Append(DefaultRatio).Append("）\n");
            return true;
        }

        /// <summary>身体碰撞体 = 本物体上第一个非 Trigger 的碰撞体。</summary>
        private static Collider2D FindBodyCollider(GameObject entity)
        {
            foreach (Collider2D collider in entity.GetComponents<Collider2D>())
                if (!collider.isTrigger) return collider;
            return null;
        }

        /// <summary>形状与身体同型，尺寸按比例缩到中心（offset 不动，两者同心）。</summary>
        private static Collider2D CreateArea(Collider2D body, GameObject box)
        {
            if (body is CapsuleCollider2D capsule)
            {
                var area = box.AddComponent<CapsuleCollider2D>();
                area.direction = capsule.direction;
                area.size = capsule.size * DefaultRatio;
                area.offset = capsule.offset;
                return area;
            }

            if (body is BoxCollider2D rectangle)
            {
                var area = box.AddComponent<BoxCollider2D>();
                area.size = rectangle.size * DefaultRatio;
                area.offset = rectangle.offset;
                return area;
            }

            if (body is CircleCollider2D circle)
            {
                var area = box.AddComponent<CircleCollider2D>();
                area.radius = circle.radius * DefaultRatio;
                area.offset = circle.offset;
                return area;
            }

            // 少见形状（Polygon 等）：退回包围盒，按世界尺寸除回本地尺寸再缩。
            var fallback = box.AddComponent<BoxCollider2D>();
            Vector3 scale = body.transform.lossyScale;
            Vector3 world = body.bounds.size;
            fallback.size = new Vector2(
                SafeDivide(world.x, scale.x) * DefaultRatio,
                SafeDivide(world.y, scale.y) * DefaultRatio);
            fallback.offset = body.offset;
            return fallback;
        }

        /// <summary>把 _owner 接到这个实体自己的承受方上（同物体上的 IDamageable）。</summary>
        private static void AssignOwner(GameObject box, GameObject entity)
        {
            MonoBehaviour owner = null;
            foreach (MonoBehaviour behaviour in entity.GetComponents<MonoBehaviour>())
            {
                if (behaviour is IDamageable) { owner = behaviour; break; }
            }
            if (owner == null) return;

            var hurtbox = box.GetComponent<Hurtbox>();
            var so = new SerializedObject(hurtbox);
            so.FindProperty("_owner").objectReferenceValue = owner;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ==================================================================
        //  报告用的小工具
        // ==================================================================

        private static string PathOf(GameObject go)
        {
            string path = go.name;
            for (Transform parent = go.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private static string DescribeArea(Collider2D area)
        {
            switch (area)
            {
                case CapsuleCollider2D capsule:
                    return "胶囊 " + capsule.size.x.ToString("0.###") + "×" + capsule.size.y.ToString("0.###");
                case BoxCollider2D box:
                    return "盒 " + box.size.x.ToString("0.###") + "×" + box.size.y.ToString("0.###");
                case CircleCollider2D circle:
                    return "圆 r=" + circle.radius.ToString("0.###");
                default:
                    return area.GetType().Name;
            }
        }

        private static float SafeDivide(float value, float divisor)
            => Mathf.Approximately(divisor, 0f) ? value : value / divisor;
    }
}
