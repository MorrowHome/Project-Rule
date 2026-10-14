using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 按「暴露面」给白盒铺美术。白盒刷实心块，本工具逐格判断哪些面朝外，按朝向选构件 + 变换。
    ///
    ///   面朝上   = 地板
    ///   面朝下   = 天花板（上下翻转）
    ///   面朝左   = 面朝左的墙（旋转 +90°）
    ///   面朝右   = 面朝右的墙（旋转 −90°）
    ///
    /// 三条关键规则（都是为了不糊）：
    ///
    /// 1) 用哪套件由**整条 run** 决定，不是逐格。美术件是半透明的（主体 alpha 0.96，端帽羽化到 0.02），
    ///    一重叠 alpha 就相加变亮。逐格判断会把同一条连续地面切成「厚件 + 薄件」混用，接缝处一厚一薄 → 糊。
    ///    现在：一条 run 里过半格子是「1 格厚的板」→ 整条用平台套件；否则整条用地面套件。
    ///
    /// 2) 所有 run 端点都用**硬切**，不用端帽。Ground Cap 外沿羽化，会在 Scene/Game 里留下灰雾边；
    ///    端点统一用中段石片，避免羽化边朝外，T 接头也不会露出圆头。
    ///
    /// 3) 厚结构不铺背面。3 格以上的厚墙正反两面都铺会大面积重叠 → 只铺外侧空间更大的一面；
    ///    厚地板的下表面不铺。厚天花板的下表面用菜单 ③ 单独铺。
    ///
    /// 用法：Hierarchy 选中逻辑 Grid，菜单 Tools/规则游戏/铺装美术/*（Ctrl+Shift+G = 全部）。
    /// 铺完自动隐藏白盒层（Ctrl+Shift+L 可再显示）。
    /// </summary>
    public static class GroundArtDresser
    {
        private const string StoneFolder = "Assets/_Project/Art/Sprites/Tiles/Stone/";
        private const string ArtChildName = "Art";
        private const int ArtSortingOrder = 1;
        private const int SpaceProbe = 64;

        private enum Face { Up, Down, Left, Right }

        [MenuItem("Tools/规则游戏/铺装美术/① 全部（地板 + 墙 + 薄板） %#g", false, 10)]
        public static void DressAll() => Dress(null, false);

        [MenuItem("Tools/规则游戏/铺装美术/② 只铺地板（面朝上）", false, 11)]
        public static void DressUp() => Dress(new[] { Face.Up }, false);

        [MenuItem("Tools/规则游戏/铺装美术/③ 只铺天花板（含厚结构的下表面）", false, 12)]
        public static void DressDown() => Dress(new[] { Face.Down }, true);

        [MenuItem("Tools/规则游戏/铺装美术/④ 只铺面朝左的墙", false, 13)]
        public static void DressLeft() => Dress(new[] { Face.Left }, false);

        [MenuItem("Tools/规则游戏/铺装美术/⑤ 只铺面朝右的墙", false, 14)]
        public static void DressRight() => Dress(new[] { Face.Right }, false);

        [MenuItem("Tools/规则游戏/铺装美术/⑥ 清空美术 %#h", false, 30)]
        public static void Clear()
        {
            var root = Selection.activeGameObject;
            if (root == null) { Debug.LogError("[铺装] 请先选中逻辑 Grid。"); return; }
            var artRoot = root.transform.Find(ArtChildName);
            if (artRoot != null) ClearChildren(artRoot);
            Debug.Log("[铺装] 已清空美术层。");
        }

        [MenuItem("Tools/规则游戏/铺装美术/⑦ 显示 / 隐藏白盒 %#l", false, 32)]
        public static void ToggleLogicRenderer()
        {
            var root = Selection.activeGameObject;
            if (root == null) { Debug.LogError("[铺装] 请先选中逻辑 Grid。"); return; }
            var tilemap = FindLogicTilemap(root);
            if (tilemap == null) { Debug.LogError("[铺装] 找不到 Tilemap。"); return; }
            var renderer = tilemap.GetComponent<TilemapRenderer>();
            if (renderer == null) return;
            renderer.enabled = !renderer.enabled;
            Debug.Log("[铺装] 白盒显示 = " + renderer.enabled);
        }

        private static void Dress(Face[] only, bool includeThickDown)
        {
            var root = Selection.activeGameObject;
            if (root == null) { Debug.LogError("[铺装] 请先在 Hierarchy 里选中逻辑 Grid 物体。"); return; }

            var tilemap = FindLogicTilemap(root);
            if (tilemap == null) { Debug.LogError("[铺装] 选中的物体下面找不到 Tilemap。"); return; }

            var kits = LoadKits();
            if (kits == null) return;

            var artRoot = EnsureArtRoot(root);
            ClearChildren(artRoot);

            var grid = tilemap.layoutGrid;
            var cell = grid.cellSize;
            var bounds = tilemap.cellBounds;

            var solid = new HashSet<Vector3Int>();
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var p = new Vector3Int(x, y, 0);
                    if (tilemap.HasTile(p)) solid.Add(p);
                }
            if (solid.Count == 0) { Debug.Log("[铺装] 白盒是空的，没有可铺的面。"); return; }

            bool Has(int x, int y) => solid.Contains(new Vector3Int(x, y, 0));
            int Space(int x, int y, int dx, int dy)
            {
                int n = 0, cx = x + dx, cy = y + dy;
                while (n < SpaceProbe && !Has(cx, cy)) { n++; cx += dx; cy += dy; }
                return n;
            }

            var buckets = new Dictionary<Face, List<Vector3Int>>();
            foreach (Face f in System.Enum.GetValues(typeof(Face))) buckets[f] = new List<Vector3Int>();

            foreach (var p in solid)
            {
                int x = p.x, y = p.y;
                bool up = !Has(x, y + 1);
                bool down = !Has(x, y - 1);
                bool left = !Has(x - 1, y);
                bool right = !Has(x + 1, y);
                if (!up && !down && !left && !right) continue;

                bool plateH = up && down;       // 正好 1 格厚的横板
                bool plateV = left && right;    // 正好 1 格厚的竖板
                // T 接头中心虽被竖向白盒挡住 Up 面，但横向美术带需要经过此格才能不断开。
                bool upJoin = !up && down && Has(x, y + 1) && Has(x - 1, y) && Has(x + 1, y);
                int spL = Space(x, y, -1, 0), spR = Space(x, y, 1, 0);

                if (plateH)
                {
                    // 1 格厚的板只铺一面：平台套件 0.48 已经占满 0.5 格，
                    // 两面都铺只会让两张件的羽化端帽重叠 → 端点亮斑。
                    if (up && (only == null || only.Contains(Face.Up))) buckets[Face.Up].Add(p);
                }
                else if (plateV)
                {
                    // 同上，1 格厚的竖板只铺外侧空间更大的一面
                    if (spL >= spR)
                    {
                        if (left && (only == null || only.Contains(Face.Left))) buckets[Face.Left].Add(p);
                    }
                    else
                    {
                        if (right && (only == null || only.Contains(Face.Right))) buckets[Face.Right].Add(p);
                    }
                }
                else
                {
                    if ((up || upJoin) && (only == null || only.Contains(Face.Up))) buckets[Face.Up].Add(p);
                    // 厚结构的下表面默认不铺（会和上面大面积重叠）；要铺用菜单 ③
                    if (down && includeThickDown && (only == null || only.Contains(Face.Down))) buckets[Face.Down].Add(p);
                    if (left && spL >= spR && (only == null || only.Contains(Face.Left))) buckets[Face.Left].Add(p);
                    if (right && spR >= spL && (only == null || only.Contains(Face.Right))) buckets[Face.Right].Add(p);
                }
            }

            int runs = 0, pieces = 0;
            foreach (var kv in buckets)
            {
                if (kv.Value.Count == 0) continue;
                bool vertical = kv.Key == Face.Left || kv.Key == Face.Right;
                foreach (var run in GroupRuns(kv.Value, vertical))
                {
                    pieces += DressRun(artRoot, kv.Key, run, grid, cell, kits, solid);
                    runs++;
                }
            }

            var logicRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (logicRenderer != null && logicRenderer.enabled)
            {
                logicRenderer.enabled = false;
                EditorUtility.SetDirty(logicRenderer);
            }

            Debug.Log($"[铺装] 完成：{runs} 段，{pieces} 个构件。白盒层已隐藏（Ctrl+Shift+L 可再显示）。");
        }

        private static List<List<Vector3Int>> GroupRuns(List<Vector3Int> cells, bool vertical)
        {
            var result = new List<List<Vector3Int>>();
            foreach (var line in cells.GroupBy(c => vertical ? c.x : c.y))
            {
                var sorted = line.OrderBy(c => vertical ? c.y : c.x).ToList();
                var cur = new List<Vector3Int> { sorted[0] };
                for (int i = 1; i < sorted.Count; i++)
                {
                    int prev = vertical ? sorted[i - 1].y : sorted[i - 1].x;
                    int now = vertical ? sorted[i].y : sorted[i].x;
                    if (now == prev + 1) cur.Add(sorted[i]);
                    else { result.Add(cur); cur = new List<Vector3Int> { sorted[i] }; }
                }
                result.Add(cur);
            }
            return result;
        }

        private static int DressRun(Transform artRoot, Face face, List<Vector3Int> run, GridLayout grid,
                                    Vector3 cell, Kit kit, HashSet<Vector3Int> solid)
        {
            bool vertical = face == Face.Left || face == Face.Right;
            float span = (vertical ? cell.y : cell.x) * run.Count;

            var first = run[0];
            var last = run[run.Count - 1];
            Vector3 cellOrigin = grid.CellToWorld(first);
            Vector3 cursor;
            Quaternion rot = Quaternion.identity;
            switch (face)
            {
                case Face.Up:
                    // 把板面放进逻辑格，而不是贴在格子外侧。
                    cursor = new Vector3(cellOrigin.x, cellOrigin.y, 0f);
                    break;
                case Face.Down:
                    cursor = new Vector3(cellOrigin.x, cellOrigin.y, 0f);
                    break;
                case Face.Left:
                    // 旋转后的石片从锚点向左伸；锚在格子右边，覆盖当前格。
                    cursor = new Vector3(cellOrigin.x + cell.x, cellOrigin.y, 0f);
                    rot = Quaternion.Euler(0f, 0f, 90f);
                    break;
                default:
                    Vector3 top = grid.CellToWorld(last);
                    // 右墙从锚点向右伸；锚在格子左边，覆盖当前格。
                    cursor = new Vector3(cellOrigin.x, top.y + cell.y, 0f);
                    rot = Quaternion.Euler(0f, 0f, -90f);
                    break;
            }
            // The source sprite alpha fades farther at its top than at its bottom; orient its cleaner edge outward.
            bool flipY = face != Face.Down;

            int thinCount = 0;
            foreach (var c in run)
            {
                bool up = !solid.Contains(new Vector3Int(c.x, c.y + 1, 0));
                bool down = !solid.Contains(new Vector3Int(c.x, c.y - 1, 0));
                bool left = !solid.Contains(new Vector3Int(c.x - 1, c.y, 0));
                bool right = !solid.Contains(new Vector3Int(c.x + 1, c.y, 0));
                if ((up && down) || (left && right)) thinCount++;
            }
            bool thin = thinCount * 2 >= run.Count;

            var slabs = thin ? kit.SlabSlabs : kit.GroundSlabs;
            float avgSlab = slabs.Average(s => s.bounds.size.x);
            // 端帽带羽化外边，按需求一律改硬切：首尾也用中段石片，不再留下灰色雾边。
            float middle = span;
            if (middle < 1.5f)
            {
                var one = slabs.OrderBy(s => Mathf.Abs(s.bounds.size.x - span)).First();
                Place(artRoot, one, face, cursor, rot, flipY, false, span / one.bounds.size.x, cell);
                return 1;
            }

            int count = 0;
            int pairCount = Mathf.Max(1, Mathf.RoundToInt(middle / (avgSlab * 2f)));
            float half = middle * 0.5f;
            float baseW = half / pairCount;
            var pairW = new float[pairCount];
            float sum = 0f;
            var rng = new System.Random(20261014 + first.x * 31 + first.y * 17);
            for (int i = 0; i < pairCount; i++)
            {
                pairW[i] = Mathf.Max(avgSlab * 0.5f, baseW * (1f + (float)(rng.NextDouble() * 0.24 - 0.12)));
                sum += pairW[i];
            }
            float fix = half / sum;
            for (int i = 0; i < pairCount; i++) pairW[i] *= fix;

            for (int p = 0; p < pairCount; p++)
            {
                float w = pairW[p];
                var baseSlab = slabs.OrderBy(s => Mathf.Abs(s.bounds.size.x - w)).First();
                float scale = w / baseSlab.bounds.size.x;
                Place(artRoot, baseSlab, face, cursor, rot, flipY, false, scale, cell);
                cursor += rot * new Vector3(w, 0f, 0f);
                Place(artRoot, baseSlab, face, cursor, rot, flipY, true, scale, cell);
                cursor += rot * new Vector3(w, 0f, 0f);
                count += 2;
            }

            return count;
        }

        private static void Place(Transform parent, Sprite sprite, Face face, Vector3 anchor,
                                  Quaternion rot, bool flipY, bool mirror, float scale, Vector3 cellSize)
        {
            var go = new GameObject(face + "_" + sprite.name);
            go.transform.SetParent(parent, false);
            float w = sprite.bounds.size.x * scale;
            Vector3 pos = anchor + rot * new Vector3(mirror ? w : 0f, 0f, 0f);
            go.transform.position = pos;
            go.transform.rotation = rot;
            float faceThickness = face == Face.Left || face == Face.Right ? cellSize.x : cellSize.y;
            float thicknessScale = faceThickness / sprite.bounds.size.y;
            go.transform.localScale = new Vector3(scale, thicknessScale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = ArtSortingOrder;
            sr.flipX = mirror;
            sr.flipY = flipY;
            Undo.RegisterCreatedObjectUndo(go, "铺装美术");
        }

        private class Kit
        {
            public Sprite GroundCapLeft, GroundCapRight, GroundShort;
            public List<Sprite> GroundSlabs;
            public Sprite SlabCapLeft, SlabCapRight, SlabShort;
            public List<Sprite> SlabSlabs;
        }

        private static Kit LoadKits()
        {
            Sprite L(string n) => AssetDatabase.LoadAssetAtPath<Sprite>(StoneFolder + n + ".png");
            var kit = new Kit
            {
                GroundCapLeft = L("Ground_Stone_CapLeft"),
                GroundCapRight = L("Ground_Stone_CapRight"),
                GroundShort = L("Ground_Stone_Short_01"),
                GroundSlabs = new List<Sprite>(),
                SlabCapLeft = L("Platform_Stone_CapLeft"),
                SlabCapRight = L("Platform_Stone_CapRight"),
                SlabShort = L("Platform_Stone_Short_01"),
                SlabSlabs = new List<Sprite>(),
            };
            for (int i = 4; i <= 8; i++)
            {
                var s = L($"Ground_Stone_Slab_{i:00}");
                if (s != null) kit.GroundSlabs.Add(s);
            }
            var p3 = L("Platform_Stone_Slab_03");
            if (p3 != null) kit.SlabSlabs.Add(p3);
            if (kit.GroundCapLeft == null || kit.GroundCapRight == null || kit.GroundSlabs.Count == 0
                || kit.SlabCapLeft == null || kit.SlabCapRight == null || kit.SlabSlabs.Count == 0)
            {
                Debug.LogError("[铺装] 石块构件贴图没找全，检查 " + StoneFolder);
                return null;
            }
            return kit;
        }

        private static Transform EnsureArtRoot(GameObject root)
        {
            var t = root.transform.Find(ArtChildName);
            if (t != null) return t;
            var go = new GameObject(ArtChildName);
            go.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "创建 Art 子物体");
            return go.transform;
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);
        }

        private static Tilemap FindLogicTilemap(GameObject root)
        {
            var maps = root.GetComponentsInChildren<Tilemap>(true);
            if (maps.Length == 0) return null;
            var named = maps.FirstOrDefault(m => m.gameObject.name.ToLowerInvariant().Contains("logic"));
            return named != null ? named : maps[0];
        }
    }
}
