using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;

namespace RuleGame.Editor.Verification
{
    /// <summary>
    /// 纸飞机飞刀（<see cref="ThrownKnife"/>）的 Play 冒烟自检：**在隔离的空场景里跑**，
    /// 跑完把原来打开的场景重新打开。
    /// </summary>
    /// <remarks>
    /// <para>五条断言对着需求逐条验：① 正常受重力（抛物线落点对得上 ½gt²）；
    /// ② 落地消失；③ 撞场景道具消失；④ 撞接触式开关**按下它**再消失；
    /// ⑤ 投掷者自己的碰撞体不误判（从身体里出手不会立刻炸）。</para>
    /// <para>⚠️ 不在关卡 / 测试场景里跑：工程关掉了 Scene Reload，运行期改动会落到编辑场景上
    /// （见仓库根 AGENTS.md 的事故记录）。</para>
    /// </remarks>
    public static class PaperPlaneKnifeValidation
    {
        private const string Pending   = "RuleGame.PaperPlaneKnifeValidation.Pending";
        private const string PrefabPath = "Assets/_Project/Prefabs/PaperPlaneKnife.prefab";
        private const string ReturnScene = "RuleGame.PaperPlaneKnifeValidation.Return";
        private static readonly string OutputPath =
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/PaperPlaneKnifeValidation.txt"));

        private static readonly List<string> Lines = new List<string>();
        private static readonly List<ThrownKnife> Knives = new List<ThrownKnife>();

        private static TestSwitch _switch;
        private static GameObject _rig;
        private static int _step;
        private static int _passed;
        private static float _due;
        private static float _spawnTime;
        private static float _gravityStartY;
        private static GameObject _thrower;

        [MenuItem("规则游戏/验证/纸飞机飞刀（隔离场景 Play 冒烟）", false, 72)]
        public static void Run()
        {
            if (Application.isPlaying) { Debug.LogWarning("[飞刀验证] 先退出 Play 再跑。"); return; }

            // 先把当前场景存盘，再换一张空场景 —— 绝不把运行期改动写回原场景。
            Scene current = SceneManager.GetActiveScene();
            if (current.isDirty) EditorSceneManager.SaveScene(current);
            SessionState.SetString(ReturnScene, current.path);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        /// <remarks>无条件注册：进 Play 时域可能不重载，靠 <see cref="SessionState"/> 判断该不该跑。</remarks>
        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (SessionState.GetBool(Pending, false)) Begin();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Restore();
            }
        }

        // ==================================================================
        //  测试台
        // ==================================================================

        private static void Begin()
        {
            try
            {
                // 编辑器窗口失焦时 Play 循环会被节流 —— 不给这两条，Time.time 会一直停在 0.02，
                // 测试就永远等不到 _due（实测踩过）。
                Application.runInBackground = true;
                EditorApplication.QueuePlayerLoopUpdate();

                _rig = new GameObject("PaperPlaneRig");

                // 地面：y = 0 上面
                var ground = new GameObject("Ground") { layer = 8 };
                ground.transform.SetParent(_rig.transform);
                ground.transform.position = new Vector3(0f, -0.5f);
                ground.AddComponent<BoxCollider2D>().size = new Vector2(200f, 1f);

                // 墙：撞「场景道具」与撞「墙」用的是同一条扫掠分支
                var wall = new GameObject("Wall") { layer = 8 };
                wall.transform.SetParent(_rig.transform);
                wall.transform.position = new Vector3(6f, 2f);
                wall.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 4f);

                // 一块普通道具（非地面层、非受击箱）
                var prop = new GameObject("Prop") { layer = 0 };
                prop.transform.SetParent(_rig.transform);
                prop.transform.position = new Vector3(-8f, 1f);
                prop.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 1f);

                // 接触式开关
                var button = new GameObject("TestSwitch") { layer = 0 };
                button.transform.SetParent(_rig.transform);
                button.transform.position = new Vector3(0f, 0.2f);
                var shape = button.AddComponent<BoxCollider2D>();
                shape.size = new Vector2(0.9f, 0.35f);
                shape.isTrigger = true;
                _switch = button.AddComponent<TestSwitch>();
                _switch.Pressed = false;

                // 投掷者：自己身上有一坨碰撞体，刀从它中间出手
                _thrower = new GameObject("Thrower") { layer = 0 };
                _thrower.transform.SetParent(_rig.transform);
                _thrower.transform.position = new Vector3(10f, 1f);
                _thrower.AddComponent<BoxCollider2D>().size = new Vector2(1f, 2f);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null) throw new InvalidOperationException("找不到 " + PrefabPath);

                Check("预制体带 ThrownKnife 与 SpriteRenderer",
                      prefab.GetComponent<ThrownKnife>() != null && prefab.GetComponentInChildren<SpriteRenderer>() != null);
                SpriteRenderer prefabRenderer = prefab.GetComponentInChildren<SpriteRenderer>();
                Check("纸飞机精灵已挂上且尺寸约 0.5 米",
                      prefabRenderer.sprite != null
                      && Mathf.Abs(prefabRenderer.sprite.rect.width / prefabRenderer.sprite.pixelsPerUnit - 0.5f) < 0.01f,
                      prefabRenderer.sprite == null ? "sprite 为空" : prefabRenderer.sprite.bounds.size.ToString());

                // ① 自由飞行（量重力）
                _gravityStartY = 12f;
                Spawn(new Vector2(-20f, _gravityStartY), new Vector2(3f, 0f), 3f);

                // ② 落地消失
                Spawn(new Vector2(-2f, 1.5f), Vector2.down, 2f);

                // ③ 撞道具消失（14 米/秒：到道具那儿只落 0.34 米，才真的撞在道具上而不是从它下面钻过去）
                Spawn(new Vector2(-12f, 1.3f), Vector2.right, 14f);

                // ④ 撞开关 → 按下 + 消失
                Spawn(new Vector2(-4f, 0.9f), Vector2.right, 11f);

                // ⑤ 从投掷者身体里出手
                // 斜上抛：这条弹道在观测窗口内不会自己掉到地上，测的才是「从身体里出手」
                Spawn(new Vector2(10f, 1f), new Vector2(0.6f, 0.8f), 8f, _thrower);

                _spawnTime = Time.time;
                _due = Time.time + 0.9f;
                _step = 1;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static void Spawn(Vector2 origin, Vector2 direction, float speed, GameObject thrower = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ThrownKnife knife = UnityEngine.Object.Instantiate(prefab).GetComponent<ThrownKnife>();
            knife.transform.position = origin;
            knife.OnSpent += k => Lines.Add("[hit] " + (k.LastHit == null
                ? "lifetime"
                : k.LastHit.name + " layer=" + k.LastHit.gameObject.layer
                  + " trigger=" + k.LastHit.isTrigger + " root=" + k.LastHit.transform.root.name));
            knife.Launch(thrower != null ? thrower.transform : null, null, direction, speed, 4f, 4f);
            Knives.Add(knife);
        }

        private static void Tick()
        {
            try
            {
                // 每帧主动催一次 Play 循环：编辑器不聚焦时它不会自己走（与 PlayerSwordWaveValidation 同一条做法）。
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.time < _due) return;
                if (_step != 1) return;
                _step = 2;

                float t = Time.time - _spawnTime;

                Lines.Add("[diag] t=" + t.ToString("0.000") + " gravity=" + Physics2D.gravity
                          + " paused=" + (RuleGame.Infrastructure.Time.TimeManager.Instance?.IsPaused.ToString() ?? "no-time-manager"));
                for (int i = 0; i < Knives.Count; i++)
                {
                    ThrownKnife k = Knives[i];
                    Lines.Add("[diag] knife" + i + " " + (k == null
                        ? "DESTROYED"
                        : "pos=" + k.transform.position + " age=" + k.Age.ToString("0.00")
                          + " spent=" + k.Spent + " vel=" + k.Velocity));
                }

                ThrownKnife flyer = Knives[0];
                float expected = 0.5f * Mathf.Abs(Physics2D.gravity.y) * t * t;
                bool alive = flyer != null;
                float actual = alive ? _gravityStartY - flyer.transform.position.y : 0f;
                Check("正常受重力：下落 " + actual.ToString("0.00") + " 米，理论 " + Mor(expected) + " 米",
                      alive && Mathf.Abs(actual - expected) < 0.35f,
                      "t=" + t.ToString("0.00"));

                Check("落地消失", Knives[1] == null);
                Check("撞场景道具消失", Knives[2] == null);
                Check("撞开关消失", Knives[3] == null);
                Check("撞开关时真的按下了它", _switch != null && _switch.Pressed);
                Check("投掷者自己的身体不误判（刀飞出来了）",
                      Knives[4] != null && Knives[4].transform.position.x > 10.4f,
                      Knives[4] == null ? "刀在出手瞬间被自己撞掉" : "x=" + Knives[4].transform.position.x.ToString("0.00"));

                Finish(null);
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static string Mor(float value) => value.ToString("0.00");

        // ==================================================================
        //  收尾
        // ==================================================================

        private static void Check(string label, bool ok, string detail = "")
        {
            Lines.Add((ok ? "✓ " : "✗ ") + label + (ok || string.IsNullOrEmpty(detail) ? "" : " —— " + detail));
            if (ok) { _passed++; Debug.Log("[飞刀验证] PASS " + label); }
            else Debug.LogError("[飞刀验证] FAIL " + label + (string.IsNullOrEmpty(detail) ? "" : " —— " + detail));
        }

        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            if (_rig != null) UnityEngine.Object.Destroy(_rig);

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            int failed = Lines.FindAll(line => line.StartsWith("✗")).Count;
            Lines.Add(error != null ? "失败：" + error
                : failed == 0 ? "全部通过（" + _passed + " 项）" : "有 " + failed + " 项没过");
            File.WriteAllText(OutputPath, string.Join("\n", Lines));

            SessionState.SetBool(Pending, false);
            EditorApplication.isPlaying = false;
        }

        private static void Restore()
        {
            string path = SessionState.GetString(ReturnScene, string.Empty);
            if (string.IsNullOrEmpty(path)) return;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SessionState.EraseString(ReturnScene);
        }

        /// <summary>假开关：只记「有没有被按到」。真实按钮是 CitySwitch。</summary>
        private sealed class TestSwitch : MonoBehaviour, IContactSwitch
        {
            public bool Pressed;
            public void PressByContact() => Pressed = true;
            public void ReleaseByContact() { }
        }
    }
}
