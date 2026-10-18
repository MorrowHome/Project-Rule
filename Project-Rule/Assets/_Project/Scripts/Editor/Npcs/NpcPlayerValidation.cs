using System;
using System.Collections.Generic;
using System.IO;
using RuleGame.Development.Npcs;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Gameplay.Npcs;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;
using RuleGame.UI.Dialogue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Npcs
{
    /// <summary>在完整副本通过 Input System 键盘事件验证实际玩家交互，不直接调用 NPC 试播。</summary>
    public static class NpcPlayerValidation
    {
        private const string Pending = "RuleGame.NpcPlayerValidation";
        private static readonly List<string> Results = new List<string>();
        private static Keyboard _keyboard;
        private static PlayerController _player;
        private static NpcPlayerInteraction _interaction;
        private static DialogueHost _host;
        private static DialogueToolkitPresenter _view;
        private static NpcLabControls _hud;
        private static RenderTexture _surface;
        private static IDisposable _otherLock;
        private static int _stage, _frame;
        private static double _until, _deadline;
        private static float _x;
        private static bool _error;
        private static string Output => "Logs/NpcPlayer";

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只允许在临时工程副本执行。");
            try
            {
                NpcSetup.ConnectPlayableLab();
                EditorSceneManager.OpenScene(NpcSetup.ScenePath);
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
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            _interaction = _player.GetComponent<NpcPlayerInteraction>();
            _host = UnityEngine.Object.FindAnyObjectByType<DialogueHost>();
            _view = _host.GetComponent<DialogueToolkitPresenter>();
            _hud = UnityEngine.Object.FindAnyObjectByType<NpcLabControls>();
            _keyboard = InputSystem.AddDevice<Keyboard>("NpcValidationKeyboard");
            _keyboard.MakeCurrent();
            _surface = new RenderTexture(1280, 720, 24);
            _surface.Create();
            UnityEngine.Object.FindAnyObjectByType<Camera>().targetTexture = _surface;
            _host.GetComponent<UIDocument>().panelSettings.targetTexture = _surface;
            _deadline = EditorApplication.timeSinceStartup + 40;
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
            Wait(0, 0.4f);
        }

        private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));

        private static void Wait(int stage, float duration = 0.1f)
        {
            _stage = stage;
            _until = EditorApplication.timeSinceStartup + duration;
            _frame = Time.frameCount + 2;
        }

        private static void Tick()
        {
            try
            {
                if (_error) throw new InvalidOperationException("出现运行时错误，查看日志。");
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("阶段 " + _stage);
                if (EditorApplication.timeSinceStartup < _until || Time.frameCount < _frame) return;
                switch (_stage)
                {
                    case 0:
                        Require(_host.IsReady && _hud != null, "玩家、共享 Host 与画面 HUD 接线完成");
                        Require(_interaction.NearbyNpc == null, "出生点在交互范围外");
                        _x = _player.transform.position.x;
                        Keys(Key.D); Wait(1, 0.23f); break;
                    case 1:
                        Require(_player.transform.position.x > _x + 0.5f, "真实 D 键驱动现有玩家控制器移动");
                        Keys(); Wait(2, 0.2f); break;
                    case 2:
                        Require(_interaction.NearbyNpc != null && _interaction.NearbyNpc.Definition.Id == "Merchant", "走近后选择最近商人");
                        Require(_hud.PromptText.Contains("商人"), "Game 画面显示商人交互提示");
                        Keys(Key.E); Wait(3); break;
                    case 3:
                        Require(_host.IsPlaying && _interaction.ActiveNpc.Definition.Id == "Merchant", "真实 E 键开始商人交谈");
                        Require(_player.IsGameplayInputSuppressed, "交谈占用玩家输入令牌");
                        _x = _player.transform.position.x;
                        Keys(Key.D, Key.Space); Wait(4, 0.25f); break;
                    case 4:
                        Require(Mathf.Abs(_player.transform.position.x - _x) < 0.05f, "交谈期间 D 和跳跃不驱动角色");
                        Require(_view.CurrentLineId == "line:npc_merchant_talk", "交互触发真实 Yarn 商人台词");
                        Keys(); Wait(5); break;
                    case 5:
                        Keys(Key.E); Wait(6); break;
                    case 6:
                        Keys(); Wait(7); break;
                    case 7:
                        Require(!_view.IsTyping, "E 第一次推进显示整句");
                        Capture();
                        Keys(Key.E); Wait(8, 0.2f); break;
                    case 8:
                        Require(_interaction.IsTrading && _hud.ShopVisible, "读完后在 Game 画面打开商品展示");
                        Require(_player.IsGameplayInputSuppressed, "商品展示期间继续占用输入");
                        Keys(); Wait(9); break;
                    case 9:
                        Keys(Key.E); Wait(10); break;
                    case 10:
                        Require(_host.IsPlaying && !_interaction.IsTrading && !_hud.ShopVisible, "E 关闭展示并进入道别");
                        Require(_view.CurrentLineId == "line:npc_merchant_bye", "实际播放商人道别");
                        Keys(); Wait(11); break;
                    case 11:
                        Keys(Key.E); Wait(12); break;
                    case 12:
                        Keys(); Wait(13); break;
                    case 13:
                        Keys(Key.E); Wait(14, 0.2f); break;
                    case 14:
                        Require(!_host.IsPlaying && !_player.IsGameplayInputSuppressed, "道别结束恢复玩家输入");
                        _x = _player.transform.position.x;
                        Keys(Key.D); Wait(15, 0.2f); break;
                    case 15:
                        Require(_player.transform.position.x > _x + 0.5f, "对话结束后 D 键恢复移动");
                        Keys();
                        _player.TeleportTo(new Vector2(-7.8f, 0.55f));
                        _hud.GetComponent<UIDocument>().rootVisualElement.Q<Toggle>("abilityReady").value = true;
                        Wait(16); break;
                    case 16:
                        Keys(Key.E); Wait(17); break;
                    case 17:
                        Require(_view.CurrentLineId == "line:npc_mayor_talk2", "画面勾选能力达标后 E 触发镇长对应分支");
                        Keys(Key.Escape); Wait(18, 0.2f); break;
                    case 18:
                        Require(!_host.IsPlaying && !_player.IsGameplayInputSuppressed, "Esc 取消并释放玩家输入");
                        Keys();
                        _otherLock = _player.BlockGameplayInput();
                        Wait(19); break;
                    case 19:
                        Keys(Key.E); Wait(20); break;
                    case 20:
                        Require(!_host.IsPlaying, "其他界面持有输入门禁时 E 不触发 NPC");
                        _otherLock.Dispose(); _otherLock = null;
                        Keys(); Wait(21); break;
                    case 21:
                        TimeManager.Instance.SetPaused(true);
                        Keys(Key.E); Wait(22); break;
                    case 22:
                        Require(!_host.IsPlaying, "暂停期间 E 不启动交谈");
                        TimeManager.Instance.SetPaused(false);
                        Keys(); Wait(23); break;
                    case 23:
                        _player.TeleportTo(new Vector2(7.8f, 0.55f));
                        Wait(24, 0.2f); break;
                    case 24:
                        Require(_interaction.ActiveNpc != null && _interaction.ActiveNpc.Definition.Id == "Guard" && _host.IsPlaying,
                            "走进守卫范围自动交谈，接入同一个玩家门禁");
                        _interaction.enabled = false;
                        Wait(25, 0.2f); break;
                    case 25:
                        Require(!_host.IsPlaying && !_player.IsGameplayInputSuppressed, "禁用交互组件取消阅读并释放门禁");
                        Finish(); break;
                }
            }
            catch (Exception error) { Debug.LogException(error); Finish(error); }
        }

        private static void Capture()
        {
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = _surface;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Output);
                File.WriteAllBytes(Path.Combine(Output, "NpcPlayerInteraction.png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Results.Add("PASS " + description);
        }

        private static void Log(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (message.Contains("Search") || stack.Contains("UnityEditor.Search")) return;
            _error = true;
        }

        private static void Finish(Exception error = null)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            _otherLock?.Dispose();
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Output);
            Results.Add(error == null ? "PASS ALL " + Results.Count : "FAIL " + error);
            File.WriteAllLines(Path.Combine(Output, "NpcPlayerValidation.txt"), Results);
            Debug.Log("[NPC Player Validation] " + (error == null ? "passed " + (Results.Count - 1) : "FAILED"));
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
