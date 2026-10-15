using System;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>隔离工程中验证兔子真实弹体不会因预判或弹道换算提前砸地。</summary>
    public static class RabbitProjectileValidation
    {
        private const string Pending = "RuleGame.Enemy.RabbitProjectileValidation";
        private static int _passed, _failed;

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) RunCases();
            };
        }

        private static void RunCases()
        {
            TimeManager.Initialize(); WorldRegistry.Initialize();
            RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
            Vector2 originalGravity = Physics2D.gravity;
            SimulationMode2D originalMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            try
            {
                Case("站地平射", 6f, 0f, false, 1f);
                Case("下落预判", 5.5f, -8f, false, 1f);
                Case("反向下落预判", -5.5f, -8f, false, 1f);
                Case("远处高抛", 8.5f, 0f, true, 1f);
                Case("三倍重力高抛", 8.5f, 0f, true, 3f);
                Case("近处高抛", 3.2f, 0f, true, 1f);
                Case("失重平射", 6f, 0f, false, 0f);
                Case("贴身时先重新站位", 1.4f, 0f, false, 1f, true);
                FlightCase(false);
                FlightCase(true);
            }
            finally
            {
                Physics2D.gravity = originalGravity;
                Physics2D.simulationMode = originalMode;
                SessionState.SetBool(Pending, false);
            }
            string output = Path.GetFullPath("Logs/RabbitProjectileValidation.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, $"Passed: {_passed}\nFailed: {_failed}\n");
            EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }

        private static void Case(string name, float distance, float fallingSpeed, bool lob, float gravityScale, bool reposition = false)
        {
            GameObject root = new GameObject(name);
            try
            {
                Physics2D.gravity = Vector2.down * (9.81f * gravityScale);
                var floor = new GameObject("Floor"); floor.transform.SetParent(root.transform);
                floor.layer = 8; floor.transform.position = Vector2.down * .5f;
                floor.AddComponent<BoxCollider2D>().size = new Vector2(40f, 1f);
                var target = new GameObject("Target"); target.transform.SetParent(root.transform);
                var targetBody = target.AddComponent<Rigidbody2D>(); targetBody.bodyType = RigidbodyType2D.Kinematic;
                target.AddComponent<BoxCollider2D>().size = new Vector2(.7f, 1.1f);
                target.transform.position = new Vector2(distance, lob ? 3f : .65f);
                var go = new GameObject("Rabbit"); go.SetActive(false); go.transform.SetParent(root.transform);
                go.layer = 9; go.transform.position = new Vector2(0f, 1.55f);
                var actor = go.AddComponent<EnemyActor>(); actor.enabled = false;
                var config = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<EnemyDefinition>(
                    "Assets/_Project/Data/Enemies/RabbitBoss_Definition.asset"));
                config.Energy = 0f; config.RabbitEscapeDistance = 1f;
                actor.Definition = config; actor.Target = target.transform;
                go.SetActive(true); actor.SendMessage("Start");
                actor.GetComponent<BoxCollider2D>().size = config.BodySize;
                actor.Brain.Tick(actor.AnimationDuration + .01f);
                if (actor.Brain.StateName != "Throw") throw new InvalidOperationException("没有进入投掷");
                target.transform.position = new Vector2(distance, .65f);
                targetBody.position = target.transform.position;
                targetBody.linearVelocity = Vector2.up * fallingSpeed;
                Physics2D.SyncTransforms();
                EnemyProjectile shot = null;
                actor.Feedback += cue =>
                {
                    if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject;
                };
                actor.Brain.Tick(.8f); actor.Brain.Tick(.71f);
                if (reposition)
                {
                    if (shot != null || actor.Brain.StateName != "Reposition")
                        throw new InvalidOperationException("贴身仍从身体中心向脚下丢出弹体");
                    _passed++; Debug.Log("PASS: " + name);
                    return;
                }
                if (shot == null) throw new InvalidOperationException("没有生成弹体");
                shot.enabled = false;
                if ((shot.transform.position.x - actor.transform.position.x) * Mathf.Sign(distance) < 1f
                    || shot.transform.position.y < actor.transform.position.y + .2f)
                    throw new InvalidOperationException("弹体没有从身体前方的抬高出手点生成");
                if (Vector2.Distance(shot.GetComponent<Rigidbody2D>().position, shot.transform.position) > .01f)
                    throw new InvalidOperationException("池化弹体刚体仍停在旧落地点");
                // 玩家落地；预判锁定后不再追踪。用真实弹体扫掠与物理步进检查是否能到达玩家。
                targetBody.linearVelocity = Vector2.zero;
                bool reached = false;
                actor.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.ProjectileImpact) return;
                    reached = Mathf.Abs(cue.Position.x - distance) < .65f && cue.Position.y > .05f;
                    Debug.Log($"{name}: impact={cue.Position}, reached={reached}");
                };
                Debug.Log($"{name}: initial velocity={shot.Velocity}, gravity={shot.Gravity}");
                for (int i = 0; i < 120 && shot.gameObject.activeSelf; i++)
                {
                    shot.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime);
                }
                if (!reached) throw new InvalidOperationException("到达玩家前已砸地或越过目标");
                _passed++; Debug.Log("PASS: " + name);
                UnityEngine.Object.DestroyImmediate(config);
            }
            catch (Exception error) { _failed++; Debug.LogError(name + ": " + error.Message); }
            finally
            {
                foreach (var shot in UnityEngine.Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
                    shot.gameObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(root);
                Physics2D.SyncTransforms();
            }
        }

        private static void FlightCase(bool simulate)
        {
            string name = simulate ? "倒立先实际升空，玩家钻到下方先换位" : "换位超时也不能贴地倒立投掷";
            var root = new GameObject(name);
            try
            {
                Physics2D.gravity = Vector2.down * 9.81f;
                var floor = new GameObject("Floor"); floor.transform.SetParent(root.transform);
                floor.layer = 8; floor.transform.position = Vector2.down * .5f;
                floor.AddComponent<BoxCollider2D>().size = new Vector2(40f, 1f);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/RabbitBoss.prefab");
                var actor = UnityEngine.Object.Instantiate(prefab, root.transform).GetComponent<EnemyActor>();
                actor.enabled = false;
                actor.Definition = UnityEngine.Object.Instantiate(actor.Definition); actor.Definition.Energy = 0f;
                actor.transform.position = new Vector2(0f, 1.55f);
                var body = actor.GetComponent<Rigidbody2D>(); body.position = actor.transform.position;
                var target = new GameObject("Target"); target.transform.SetParent(root.transform);
                target.transform.position = new Vector2(6f, .65f);
                target.AddComponent<BoxCollider2D>().size = new Vector2(.7f, 1.1f);
                actor.Target = target.transform; actor.SendMessage("Start");
                actor.Status.ApplyHealthDelta(40f - actor.Status.Health);
                int shots = 0; float firstShotHeight = 0f;
                actor.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.ProjectileSpawn) return;
                    if (shots == 0)
                    {
                        firstShotHeight = body.position.y;
                        Debug.Log($"倒立首轮实际位置：body={body.position}, origin={cue.Position}, target={target.transform.position}");
                    }
                    shots++;
                    ((EnemyProjectile)cue.Subject).gameObject.SetActive(false);
                };
                Physics2D.SyncTransforms();
                if (!simulate)
                {
                    actor.Brain.Tick(.02f); actor.Brain.Tick(actor.AnimationDuration + .01f);
                    actor.Brain.Tick(actor.Definition.RabbitAirMoveDuration + .01f); actor.Brain.Tick(.36f);
                    if (shots != 0 || actor.Brain.StateName != "AirReposition")
                        throw new InvalidOperationException("没有真正升空就进入倒立投掷");
                }
                else
                {
                    for (int i = 0; i < 25; i++)
                    {
                        actor.Brain.Tick(.02f); actor.Motor.Tick(.02f); Physics2D.Simulate(.02f);
                    }
                    if (body.position.y < 1.8f) throw new InvalidOperationException("翻转动画期间仍贴地");
                    for (int i = 0; i < 400 && shots == 0; i++)
                    {
                        actor.Brain.Tick(.02f); actor.Motor.Tick(.02f); Physics2D.Simulate(.02f);
                    }
                    if (shots != 3 || firstShotHeight < 2.7f)
                        throw new InvalidOperationException($"倒立出手未取得实际高度：shots={shots}, y={firstShotHeight}");
                    target.transform.position = new Vector2(body.position.x, .65f); Physics2D.SyncTransforms();
                    actor.Brain.Tick(.4f);
                    if (shots != 3 || actor.Brain.StateName != "AirReposition")
                        throw new InvalidOperationException("玩家钻到下方后仍朝脚下续丢");
                }
                _passed++; Debug.Log("PASS: " + name);
                UnityEngine.Object.DestroyImmediate(actor.Definition);
            }
            catch (Exception error) { _failed++; Debug.LogError(name + ": " + error.Message); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                Physics2D.SyncTransforms();
            }
        }
    }
}
