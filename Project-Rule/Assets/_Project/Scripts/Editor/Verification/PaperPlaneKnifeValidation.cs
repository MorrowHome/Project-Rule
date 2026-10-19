using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Verification
{
    /// <summary>
    /// 纸飞机飞刀（<see cref="ThrownKnife"/>）的 Play 冒烟自检：**在隔离的空场景里跑**，
    /// 跑完把原来打开的场景重新打开。
    /// </summary>
    /// <remarks>
    /// <para>伤害按策划 5.2 验两条：**速度档位 / 原始伤害公式**（纯函数）与**承受方的
    /// 四舍五入 + 平防 + 夹血**（走真实 CombatSystem → EntityDamageable）。</para>
    /// <para>对着需求逐条验：① 正常受重力（抛物线落点对得上 ½gt²）；
    /// ② **撞地面不消失**，停在落点等寿命；③ 撞**场景道具**炸成纸屑并消失；
    /// ④ 撞**敌人身体**同样炸掉；⑤ **穿 Trigger 区（风场）不消失**；
    /// ⑥ 撞接触式开关**按下它**且自己不消失；⑦ 投掷者自己的碰撞体不误判；
    /// ⑧ 什么都没撞到时，只有**寿命到了**才消失。</para>
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
        private static bool _shortLivedSpent;
        private static float _watchdog;
        private static int _burstsBefore;

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

        /// <summary>批处理入口（不需要人点菜单，跑完自己退出）。</summary>
        /// <remarks>
        /// <c>Unity.exe -batchmode -projectPath &lt;工程&gt; -executeMethod
        /// RuleGame.Editor.Verification.PaperPlaneKnifeValidation.RunBatch -logFile &lt;日志&gt;</c>
        /// </remarks>
        public static void RunBatch()
        {
            if (Application.isPlaying) return;

            Scene current = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(current.path)) SessionState.SetString(ReturnScene, current.path);
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

                // 地面：y = 0 上面（Ground 层 = 唯一挡得住飞刀的层）
                var ground = new GameObject("Ground") { layer = 8 };
                ground.transform.SetParent(_rig.transform);
                ground.transform.position = new Vector3(0f, -0.5f);
                ground.AddComponent<BoxCollider2D>().size = new Vector2(200f, 1f);

                // 一块普通道具（非地面层、非受击箱）：应该被穿过去
                var prop = new GameObject("Prop") { layer = 0 };
                prop.transform.SetParent(_rig.transform);
                prop.transform.position = new Vector3(-8f, 1f);
                prop.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 1f);

                // 敌人身体：非受击箱的非 Trigger 碰撞体（层 9），撞上去应该炸开
                var enemy = new GameObject("EnemyBody") { layer = 9 };
                enemy.transform.SetParent(_rig.transform);
                enemy.transform.position = new Vector3(-14f, 1f);
                enemy.AddComponent<BoxCollider2D>().size = new Vector2(0.8f, 1.6f);

                // 风场那种 Trigger 区：什么都没有，只有一个 Trigger —— 以前它会「撞掉」飞刀
                var wind = new GameObject("WindZone") { layer = 0 };
                wind.transform.SetParent(_rig.transform);
                wind.transform.position = new Vector3(-6f, 2.6f);
                var windShape = wind.AddComponent<BoxCollider2D>();
                windShape.size = new Vector2(1.2f, 1.6f);
                windShape.isTrigger = true;

                // 真风场：Wind 层 + IWindField。飞刀要**被吹起来**（不是被撞掉）
                var fieldGo = new GameObject("WindField") { layer = 11 };
                fieldGo.transform.SetParent(_rig.transform);
                fieldGo.transform.position = new Vector3(40f, 12f);
                var fieldShape = fieldGo.AddComponent<BoxCollider2D>();
                fieldShape.size = new Vector2(6f, 24f);
                fieldShape.isTrigger = true;
                var zone = fieldGo.AddComponent<RuleGame.Gameplay.Scene.WindZone>();
                var zoneSo = new SerializedObject(zone);
                // ⚠️ 字段名跟 WindZone 走：现在是「底力 + 梯度」，不再是 _forceAtTop / _forceAtBottom 两端。
                //    梯度 0 = 整条风道均匀 25 N（与旧断言同一条件）。
                zoneSo.FindProperty("_forceAtBottom").vector2Value = new Vector2(0f, 25f);
                zoneSo.FindProperty("_forceGradient").floatValue = 0f;
                zoneSo.FindProperty("_drag").floatValue = 0f;
                zoneSo.FindProperty("_verticalDamping").floatValue = 0f;
                zoneSo.ApplyModifiedPropertiesWithoutUndo();

                // 接触式开关
                var button = new GameObject("TestSwitch") { layer = 0 };
                button.transform.SetParent(_rig.transform);
                button.transform.position = new Vector3(0f, 0.2f);
                var shape = button.AddComponent<BoxCollider2D>();
                shape.size = new Vector2(0.9f, 0.35f);
                shape.isTrigger = true;
                _switch = button.AddComponent<TestSwitch>();
                _switch.Pressed = false;
                _switch.Impacts = 0;

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

                // ---- 5.2 投掷物扣血公式：速度档位 + 原始伤害 ----
                Check("5.2 速度档位：(0~8] → 0",
                      ThrownKnife.SpeedScaleFor(0f) == 0 && ThrownKnife.SpeedScaleFor(8f) == 0);
                Check("5.2 速度档位：(8~12] → 1、(12~16] → 2",
                      ThrownKnife.SpeedScaleFor(9f) == 1 && ThrownKnife.SpeedScaleFor(12f) == 1
                      && ThrownKnife.SpeedScaleFor(13f) == 2 && ThrownKnife.SpeedScaleFor(16f) == 2);
                Check("5.2 速度档位：>16 → 3（再快也封顶）",
                      ThrownKnife.SpeedScaleFor(17f) == 3 && ThrownKnife.SpeedScaleFor(60f) == 3);
                Check("5.2 原始伤害：质量 1 + 速度 16 → 10（飞刀出手速度就是 16）",
                      Mathf.Abs(ThrownKnife.RawDamageFor(1f, 16f) - 10f) < 0.001f,
                      ThrownKnife.RawDamageFor(1f, 16f).ToString("0.###"));
                Check("5.2 原始伤害与质量成正比：2 kg → 20",
                      Mathf.Abs(ThrownKnife.RawDamageFor(2f, 16f) - 20f) < 0.001f);

                // ---- 承受方：四舍五入 → 减平防 → 夹到当前生命（走真实 CombatSystem）----
                if (TimeManager.Instance == null) TimeManager.Initialize();
                if (WorldRegistry.Instance == null) WorldRegistry.Initialize();
                if (RuleSystem.Instance == null) RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                if (SnapshotRoster.Instance == null) SnapshotRoster.Initialize();
                if (SnapshotSystem.Instance == null) SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);

                var combat = new GameObject("Combat").AddComponent<CombatSystem>();
                var attacker = new GameObject("Attacker");
                attacker.transform.position = new Vector3(4f, 6f);

                var dummy = new GameObject("DamageDummy") { layer = 9 };
                dummy.transform.SetParent(_rig.transform);
                dummy.transform.position = new Vector3(6f, 6f);
                dummy.AddComponent<BoxCollider2D>().size = new Vector2(1f, 2f);
                var status = dummy.AddComponent<EntityStatus>();
                var damageable = dummy.AddComponent<EntityDamageable>();
                var dso = new SerializedObject(damageable);
                dso.FindProperty("_invincibleSeconds").floatValue = 0f;
                dso.ApplyModifiedPropertiesWithoutUndo();

                int hits = combat.ApplyDamage(attacker, damageable, 10.4f, 0f);
                Check("承受方：10.4 四舍五入扣 10（生命 100 → 90）",
                      hits == 1 && Mathf.Approximately(status.Health, 90f),
                      "hits=" + hits + " hp=" + status.Health);

                dso.FindProperty("_flatDefense").floatValue = 3f;
                dso.ApplyModifiedPropertiesWithoutUndo();
                hits = combat.ApplyDamage(attacker, damageable, 10.4f, 0f);
                Check("承受方：减 3 点平防后扣 7（90 → 83）",
                      hits == 1 && Mathf.Approximately(status.Health, 83f),
                      "hits=" + hits + " hp=" + status.Health);

                // ① 自由飞行（量重力）
                _gravityStartY = 12f;
                Spawn(new Vector2(-20f, _gravityStartY), new Vector2(3f, 0f), 3f);

                // ② 落地：不再消失，应该停在落点等寿命
                Spawn(new Vector2(-2f, 1.5f), Vector2.down, 2f);

                // ③ 撞场景道具（14 米/秒：到道具那儿只落 0.34 米，真的撞在道具上而不是从下面钻过去）
                Spawn(new Vector2(-12f, 1.3f), Vector2.right, 14f);

                // ④ 撞敌人身体
                Spawn(new Vector2(-18f, 1f), Vector2.right, 12f);

                // ⑤ 穿风场 Trigger（斜上抛，正好从 Trigger 区中间穿过）
                Spawn(new Vector2(-9f, 1.5f), new Vector2(1f, 0.5f), 10f);

                // ⑥ 撞开关：按下它，自己继续飞
                Spawn(new Vector2(-4f, 0.9f), Vector2.right, 11f);

                // ⑦ 从投掷者身体里出手（斜上抛：观测窗口内不会自己落地）
                Spawn(new Vector2(10f, 1f), new Vector2(0.6f, 0.8f), 8f, _thrower);

                // ⑨ 风场里：会被往上吹（不受风的话 0.9 秒应该落到 4 米附近）
                Spawn(new Vector2(40f, 8f), Vector2.up, 0f);

                // ⑧ 只有寿命到了才消失：把这一把的寿命调成 0.3 秒
                ThrownKnife shortLived = Spawn(new Vector2(-30f, 20f), Vector2.up, 0f);
                var so = new SerializedObject(shortLived);
                so.FindProperty("_lifetime").floatValue = 0.3f;
                so.ApplyModifiedPropertiesWithoutUndo();
                shortLived.OnSpent += _ => _shortLivedSpent = true;

                _burstsBefore = PaperPlaneBurst.SpawnCount;
                _spawnTime = Time.time;
                _due = Time.time + 0.9f;
                _watchdog = Time.realtimeSinceStartup + 40f;   // 卡住也别把批处理挂死
                _step = 1;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static ThrownKnife Spawn(Vector2 origin, Vector2 direction, float speed, GameObject thrower = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ThrownKnife knife = UnityEngine.Object.Instantiate(prefab).GetComponent<ThrownKnife>();
            knife.transform.position = origin;
            knife.Launch(thrower != null ? thrower.transform : null, null, direction, speed, 4f);
            Knives.Add(knife);
            return knife;
        }

        private static void Tick()
        {
            try
            {
                // 每帧主动催一次 Play 循环：编辑器不聚焦时它不会自己走（与 PlayerSwordWaveValidation 同一条做法）。
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.realtimeSinceStartup > _watchdog) { Finish("等待超时（Play 循环没推进）"); return; }
                if (Time.time < _due) return;
                if (_step != 1) return;
                _step = 2;

                float t = Time.time - _spawnTime;

                Lines.Add("[diag] t=" + t.ToString("0.000") + " gravity=" + Physics2D.gravity);
                for (int i = 0; i < Knives.Count; i++)
                {
                    ThrownKnife k = Knives[i];
                    Lines.Add("[diag] knife" + i + " " + (k == null
                        ? "DESTROYED"
                        : "pos=" + k.transform.position + " age=" + k.Age.ToString("0.00")
                          + " stuck=" + k.Stuck + " vel=" + k.Velocity));
                }

                ThrownKnife flyer = Knives[0];
                float expected = 0.5f * Mathf.Abs(Physics2D.gravity.y) * t * t;
                bool alive = flyer != null;
                float actual = alive ? _gravityStartY - flyer.transform.position.y : 0f;
                Check("正常受重力：下落 " + actual.ToString("0.00") + " 米，理论 " + Mor(expected) + " 米",
                      alive && Mathf.Abs(actual - expected) < 0.35f,
                      "t=" + t.ToString("0.00"));

                ThrownKnife landed = Knives[1];
                Check("撞地面**不消失**，只是停在落点",
                      landed != null && landed.Stuck && landed.transform.position.y > 0.02f
                      && landed.transform.position.y < 0.35f,
                      landed == null ? "刀被地面撞没了" : "stuck=" + landed.Stuck + " y=" + landed.transform.position.y.ToString("0.00"));

                Check("撞场景道具：炸开并消失", Knives[2] == null,
                      Knives[2] == null ? "" : "x=" + Knives[2].transform.position.x.ToString("0.00"));

                Check("撞敌人身体：炸开并消失", Knives[3] == null,
                      Knives[3] == null ? "" : "x=" + Knives[3].transform.position.x.ToString("0.00"));

                int bursts = PaperPlaneBurst.SpawnCount - _burstsBefore;
                Check("两处撞击都真的炸出了纸屑（" + bursts + " 次）", bursts >= 2, "bursts=" + bursts);

                ThrownKnife throughWind = Knives[4];
                Check("**穿过**风场 Trigger 不消失",
                      throughWind != null && throughWind.transform.position.x > -6.2f,
                      throughWind == null ? "刀被 Trigger 区撞没了" : "x=" + throughWind.transform.position.x.ToString("0.00"));

                ThrownKnife overSwitch = Knives[5];
                Check("撞开关时真的按下了它", _switch != null && _switch.Pressed);
                Check("同一发只撞一次（按开关去重）", _switch != null && _switch.Impacts == 1,
                      _switch == null ? "没有开关" : "Impacts=" + _switch.Impacts);
                Check("按下开关之后刀还在飞", overSwitch != null,
                      overSwitch == null ? "刀按完按钮就消失了" : "ok");

                Check("投掷者自己的身体不误判（刀飞出来了）",
                      Knives[6] != null && Knives[6].transform.position.x > 10.4f,
                      Knives[6] == null ? "刀在出手瞬间被自己撞掉" : "x=" + Knives[6].transform.position.x.ToString("0.00"));

                ThrownKnife inWind = Knives[7];
                Check("风场里会被吹起来（不是被撞掉）",
                      inWind != null && inWind.InWind && inWind.WindAcceleration.y > 10f
                      && inWind.transform.position.y > 12f,
                      inWind == null ? "刀没了" : "y=" + inWind.transform.position.y.ToString("0.00")
                        + " 风加速度=" + inWind.WindAcceleration.ToString("0.0") + " inWind=" + inWind.InWind);

                Check("什么都没撞到时，寿命到了才消失", Knives[8] == null && _shortLivedSpent,
                      Knives[8] == null ? "没等到消失事件" : "0.9 秒还活着");

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

            // 批处理：写完日志直接退出，别把进程挂在 Play 模式里。
            if (Application.isBatchMode) { EditorApplication.Exit(failed == 0 ? 0 : 1); return; }

            EditorApplication.isPlaying = false;
        }

        private static void Restore()
        {
            string path = SessionState.GetString(ReturnScene, string.Empty);
            if (string.IsNullOrEmpty(path)) return;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SessionState.EraseString(ReturnScene);
        }

        /// <summary>假开关：记录「有没有被按到」与「被撞了几次」。真实按钮是 CitySwitch。</summary>
        private sealed class TestSwitch : MonoBehaviour, IContactSwitch
        {
            public bool Pressed;

            /// <summary>被纸飞机撞了几次 —— 用来抓「同一发翻两次」的去重回归。</summary>
            public int Impacts;

            public void PressByContact() => Pressed = true;
            public void ReleaseByContact() { }

            public void ImpactByContact(Vector2 sourcePoint)
            {
                Pressed = true;
                Impacts++;
            }
        }
    }
}
