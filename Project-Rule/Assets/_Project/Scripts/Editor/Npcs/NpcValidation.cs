using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Gameplay.Npcs;
using RuleGame.Infrastructure.Time;
using RuleGame.UI.Dialogue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace RuleGame.Editor.Npcs
{
    /// <summary>只在可丢弃的工程副本执行；使用真实 Prefab、Yarn、UI 推进和物理步。</summary>
    public static class NpcValidation
    {
        private const string Pending = "RuleGame.NpcValidation";
        private const string CaptureOnly = "RuleGame.NpcValidation.Capture";
        private static readonly List<string> Results = new List<string>();
        private static Queue<Case> _cases;
        private static Case _current;
        private static NpcActor _actor;
        private static DialogueHost _host;
        private static DialogueToolkitPresenter _view;
        private static Dictionary<string, NpcActor> _actors;
        private static Dictionary<string, DialogueSceneFlag> _flags;
        private static int _stage, _trade, _qualification, _boss, _attack, _pass;
        private static double _deadline, _nextAdvance, _waitUntil;
        private static Vector2 _residentStart;
        private static object _snapshot;
        private static bool _error;
        private static string Output => Environment.GetEnvironmentVariable("NPC_VALIDATION_OUTPUT") ?? "Logs/Npcs";
        private sealed class Case
        {
            public string Id;
            public NpcState State;
            public int Flags;
            public bool Cancel;
            public Case(string id, NpcState state, int flags = 0, bool cancel = false)
            { Id = id; State = state; Flags = flags; Cancel = cancel; }
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只允许在临时工程副本以 batchmode 执行。");
            try
            {
                Directory.CreateDirectory(Output);
                NpcSetup.Validate();
                EditorSceneManager.OpenScene(NpcSetup.ScenePath);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void CaptureBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只允许在临时工程副本执行。");
            try
            {
                EditorSceneManager.OpenScene(NpcSetup.ScenePath);
                SessionState.SetBool(CaptureOnly, true);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            };
        }

        private static void Begin()
        {
            Application.runInBackground = true;
            _host = UnityEngine.Object.FindAnyObjectByType<DialogueHost>();
            _view = _host.GetComponent<DialogueToolkitPresenter>();
            _actors = UnityEngine.Object.FindObjectsByType<NpcActor>(FindObjectsSortMode.None).ToDictionary(a => a.Definition.Id);
            _flags = UnityEngine.Object.FindObjectsByType<DialogueSceneFlag>(FindObjectsSortMode.None).ToDictionary(f => f.name);
            if (SessionState.GetBool(CaptureOnly, false))
            {
                _waitUntil = EditorApplication.timeSinceStartup + 1f;
                EditorApplication.update += TickPreview;
                return;
            }
            foreach (NpcActor actor in _actors.Values)
            {
                actor.Definition.RepeatDelay = 0f;
                Event(actor, "_onTradeRequested").AddListener(() => _trade++);
                Event(actor, "_onQualificationGranted").AddListener(() => _qualification++);
                Event(actor, "_onBossRequested").AddListener(() => _boss++);
                Event(actor, "_onAttackRequested").AddListener(() => _attack++);
                Event(actor, "_onPassageGranted").AddListener(() => _pass++);
            }
            _cases = new Queue<Case>(new[] {
                new Case("Merchant", NpcState.Talk, cancel:true), new Case("Merchant", NpcState.Talk),
                new Case("Mayor", NpcState.Talk1), new Case("Mayor", NpcState.Talk2, 1, true),
                new Case("Mayor", NpcState.Talk2, 1), new Case("Mayor", NpcState.Talk2, 1),
                new Case("GearKeeper", NpcState.Talk1, cancel:true), new Case("GearKeeper", NpcState.Talk1),
                new Case("GearKeeper", NpcState.Talk2), new Case("GearKeeper", NpcState.Talk3, 2),
                new Case("GearKeeper", NpcState.Talk4, 4), new Case("GearKeeper", NpcState.Talk5, 6, true),
                new Case("GearKeeper", NpcState.Talk5, 6), new Case("GearKeeper", NpcState.Talk5, 6),
                new Case("Guard", NpcState.Talk1, cancel:true), new Case("Guard", NpcState.Talk1),
                new Case("Guard", NpcState.Talk2, 8, true), new Case("Guard", NpcState.Talk2, 8),
                new Case("Guard", NpcState.Talk2, 8),
                new Case("MysteryMerchant", NpcState.Talk1), new Case("MysteryMerchant", NpcState.Talk2, 16, true),
                new Case("MysteryMerchant", NpcState.Talk2, 16),
                new Case("ResidentA", NpcState.Talk), new Case("ResidentB", NpcState.Talk),
                new Case("ResidentC", NpcState.Talk), new Case("ResidentD", NpcState.Talk)
            });
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
            _deadline = EditorApplication.timeSinceStartup + 20;
        }

        private static UnityEvent Event(NpcActor actor, string field) => (UnityEvent)typeof(NpcActor)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);

        private static void TickPreview()
        {
            if (EditorApplication.timeSinceStartup < _waitUntil) return;
            EditorApplication.update -= TickPreview;
            SessionState.SetBool(CaptureOnly, false);
            SessionState.SetBool(Pending, false);
            try { CapturePreview(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Tick()
        {
            try
            {
                if (_error) throw new InvalidOperationException("运行时出现错误，查看日志。");
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("NPC 验证阶段 " + _stage + " / " + _current?.Id);
                if (_stage == 0)
                {
                    if (!_host.IsReady) return;
                    Require(_actors.Count == 9, "9 个真实预制体实例已加载");
                    _stage = 1;
                }
                if (_stage == 1)
                {
                    if (_cases.Count == 0) { BeginPhysics(); return; }
                    _current = _cases.Dequeue();
                    _actor = _actors[_current.Id];
                    string[] keys = { "abilityReady", "buttonPowered", "gearReleased", "hasPass", "townSoldOut" };
                    for (int i = 0; i < keys.Length; i++) _flags[keys[i]].SetValue((_current.Flags & (1 << i)) != 0);
                    Require(_actor.ResolveGreeting() == _current.State, _current.Id + " 条件选择 " + _current.State);
                    var far = new GameObject("OutsideInteractionRadius");
                    far.transform.position = _actor.transform.position + Vector3.right * 20f;
                    Require(!_actor.TryInteract(far.transform), "距离外拒绝 " + _current.Id);
                    UnityEngine.Object.Destroy(far);
                    Require(_actor.TryInteract(_actor.transform), "成功开播 " + _current.Id);
                    _deadline = EditorApplication.timeSinceStartup + 15;
                    _stage = 2;
                }
                if (_stage == 2)
                {
                    if (_view.CurrentLineId == null) return;
                    Require(_view.CurrentLineId == "line:npc_" + _current.Id.ToLowerInvariant() + "_" + _current.State.ToString().ToLowerInvariant(), "真实 Yarn 选句 " + _view.CurrentLineId);
                    if (_current.Cancel) _view.Close();
                    else _view.Advance();
                    _stage = 3;
                }
                if (_stage == 3 || _stage == 4)
                {
                    if (_host.IsPlaying)
                    {
                        if (EditorApplication.timeSinceStartup > _nextAdvance)
                        {
                            _view.Advance();
                            _nextAdvance = EditorApplication.timeSinceStartup + 0.08;
                        }
                        return;
                    }
                    if (_actor.State == NpcState.Sell)
                    {
                        Require(!_current.Cancel, "取消交谈不会开启交易");
                        _actor.CloseTrade();
                        Require(_actor.State == NpcState.Bye, "交易关闭后开播道别");
                        _stage = 4;
                        return;
                    }
                    if (_actor.State == NpcState.PassKey || _actor.State == NpcState.Attack) return;
                    Require(_actor.State == NpcState.Idle, "交互收尾回到 Idle");
                    if (_current.Cancel)
                    {
                        if (_current.Id == "Merchant") Require(_trade == 0, "取消商人不打开商店");
                        if (_current.Id == "Mayor") Require(_qualification == 0, "取消镇长不发放资格");
                        if (_current.Id == "GearKeeper") Require(_boss == 0, "取消齿轮人不触发 Boss");
                        if (_current.Id == "Guard" && _current.State == NpcState.Talk2) Require(_pass == 0, "取消守卫不放行");
                    }
                    _stage = 1;
                }
                if (_stage == 10)
                {
                    if (EditorApplication.timeSinceStartup < _waitUntil) return;
                    Require(Vector2.Distance(_actor.GetComponent<Rigidbody2D>().position, _residentStart) < 0.001f, "暂停期间居民位置冻结");
                    TimeManager.Instance.SetPaused(false);
                    _waitUntil = EditorApplication.timeSinceStartup + 4f;
                    _stage = 11;
                }
                if (_stage == 11)
                {
                    if (EditorApplication.timeSinceStartup < _waitUntil) return;
                    Require(Mathf.Abs(_actor.transform.position.x - _residentStart.x) > 0.1f,
                        "居民通过真实物理步踱步；起点=" + _residentStart + " 终点=" + _actor.transform.position + " 状态=" + _actor.State);
                    _actor.RestoreSnapshotState(_snapshot);
                    Require(_actor.State == NpcState.Idle, "恢复居民行为快照");
                    Require(_qualification == 1 && _boss == 1 && _attack == 1 && _pass == 1 && _trade == 2,
                        "资格/Boss/放行只发一次；攻击一次，两次交易；取消无副作用");
                    CapturePreview();
                    Finish();
                }
            }
            catch (Exception error) { Debug.LogException(error); Finish(error); }
        }

        private static void BeginPhysics()
        {
            _actor = _actors["ResidentA"];
            // 通过正常关闭交互取得 Idle，不直接写状态机私有字段。
            Require(_actor.TryInteract(_actor.transform), "居民暂停前交互");
            _view.Close();
            _snapshot = _actor.CaptureSnapshotState();
            _residentStart = _actor.GetComponent<Rigidbody2D>().position;
            TimeManager.Instance.SetPaused(true);
            _waitUntil = EditorApplication.timeSinceStartup + 0.3f;
            _deadline = EditorApplication.timeSinceStartup + 10;
            _stage = 10;
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Results.Add("PASS " + description);
        }

        private static void CapturePreview()
        {
            // 展示按原始摆位排列的真实预制体，避免居民试走后遮住其他角色。
            string[] order = { "Merchant", "Mayor", "ResidentA", "ResidentB", "ResidentC", "ResidentD", "GearKeeper", "Guard", "MysteryMerchant" };
            for (int i = 0; i < order.Length; i++)
            {
                NpcActor actor = _actors[order[i]];
                Vector2 position = new Vector2((i - 4) * 2.6f, 0f);
                actor.GetComponent<Rigidbody2D>().position = position;
                actor.transform.position = position;
            }
            Physics2D.SyncTransforms();
            Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            var texture = new RenderTexture(1920, 512, 24);
            var pixels = new Texture2D(1920, 512, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.orthographicSize = 3.5f;
                camera.backgroundColor = new Color(0.78f, 0.8f, 0.82f);
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1920, 512), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory(Output);
                File.WriteAllBytes(Path.Combine(Output, "NpcConfiguration.png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                // 工程已有的 Search 索引错误与本次 NPC 运行无关，仍保留在 Unity 日志。
                if (message.Contains("Search") || stack.Contains("UnityEditor.Search")) return;
                _error = true;
            }
        }

        private static void Finish(Exception error = null)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Output);
            File.WriteAllLines(Path.Combine(Output, "NpcValidation.txt"), Results.Concat(new[] { error == null ? "PASS ALL " + Results.Count : "FAIL " + error }));
            Debug.Log("[NPC Validation] " + Results.Count + " assertions; " + (error == null ? "passed" : "FAILED"));
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
