using System;
using System.IO;
using RuleGame.Development.P3;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Rules;
using RuleGame.Infrastructure.Time;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace RuleGame.Editor.RuleWheel
{
    /// <summary>副本内验证真实移动输入以及两级轮盘中途松开 Shift，不修改实际工程的输入设置。</summary>
    public static class RuleWheelCancellationValidation
    {
        private const string Pending = "RuleGame.Wheel.CancelValidation";
        private const string PreviewOnly = "RuleGame.Wheel.CancelPreview";
        private static PlayerController _player;
        private static RuleWheelPresenter _wheel;
        private static Keyboard _keyboard;
        private static RenderTexture _surface;
        private static int _step, _cycle, _frames;
        private static float _x, _energy;
        private static double _due;
        private static double _startupDeadline;
        private static bool _errors;
        private static string Output => Environment.GetEnvironmentVariable("RULE_WHEEL_OUTPUT") ?? "Temp/RuleWheelCancellation";

        public static void Run() => Start(false);
        public static void Preview() => Start(true);

        private static void Start(bool preview)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本运行。");
            try
            {
                Directory.CreateDirectory(Output);
                EditorSceneManager.OpenScene("Assets/_Project/Scenes/SampleScene.unity");
                RuleWheelLabBuilder.Build();
                EditorSceneManager.OpenScene(RuleWheelLabBuilder.ScenePath);
                SessionState.SetBool(Pending, true);
                SessionState.SetBool(PreviewOnly, preview);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    if (!stack.Contains("UnityEditor.Search.SearchDatabase")) _errors = true;
            };
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) Begin(); };
            if (EditorApplication.isPlaying) Begin();
        }

        private static void Begin()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            _wheel = UnityEngine.Object.FindAnyObjectByType<RuleWheelPresenter>();
            _keyboard = InputSystem.AddDevice<Keyboard>(); _keyboard.MakeCurrent();
            _energy = _player.GetComponent<EntityStatus>().Energy;
            _surface = new RenderTexture(1280, 720, 24); _surface.Create();
            Camera.main.targetTexture = _surface;
            _wheel.GetComponent<UIDocument>().panelSettings.targetTexture = _surface;
            _due = Time.unscaledTimeAsDouble + 1.0;
            _startupDeadline = EditorApplication.timeSinceStartup + 10.0;
            EditorApplication.update += Step;
        }

        private static void Step()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.unscaledTimeAsDouble < _due) return;
            // 新场景首次字体/渲染初始化会阻塞编辑器；开始操作前等真实物理接地，不按墙钟猜。
            if (_cycle == 0 && _step == 0 && !_player.IsGrounded && EditorApplication.timeSinceStartup < _startupDeadline) return;
            try
            {
                Key move = _cycle % 2 == 0 ? Key.D : Key.A;
                float direction = _cycle % 2 == 0 ? 1f : -1f;
                switch (_step++)
                {
                    case 0:
                        if (_cycle == 0) Check(_player.IsGrounded, "复制场景里的玩家正常落地");
                        if (SessionState.GetBool(PreviewOnly, false))
                        {
                            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.LeftShift));
                            _step = 2;
                            break;
                        }
                        _x = _player.transform.position.x;
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(move));
                        break;
                    case 1:
                        Check((_player.transform.position.x - _x) * direction > 0.1f, "正常移动输入未响应");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(move, Key.LeftShift));
                        break;
                    case 2:
                        Check(_wheel.Stage == RuleSelectionStage.Attribute && Mathf.Approximately(Time.timeScale, 0.1f), "Shift 未打开属性轮盘");
                        if (SessionState.GetBool(PreviewOnly, false))
                        {
                            Screenshot("attribute.png");
                            Finish(!_errors, "深色场景 HUD 对比度预览完成。");
                            return;
                        }
                        if (_cycle % 2 == 1)
                        {
                            _wheel.SelectIndex(0);
                            Check(_wheel.ConfirmSelection() && _wheel.Stage == RuleSelectionStage.Relation, "无法进入关系阶段");
                        }
                        if (_cycle == 0) Screenshot("attribute.png");
                        if (_cycle == 1) Screenshot("relation.png");
                        _x = _player.transform.position.x;
                        _frames = Time.frameCount;
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(move));
                        break;
                    case 3:
                        Check(!_wheel.IsOpen && Mathf.Approximately(Time.timeScale, 1f)
                            && Mathf.Approximately(TimeManager.Instance.TimeScale, 1f),
                            "松开 Shift 没恢复 UI / 时间倍率");
                        Check(Time.frameCount > _frames && (_player.transform.position.x - _x) * direction > 0.1f,
                            "取消选择后帧循环或移动仍被锁住");
                        Check(Mathf.Approximately(_player.GetComponent<EntityStatus>().Energy, _energy), "取消选择错误扣费");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        break;
                    case 4:
                        Check(Camera.main.GetComponent<P3FollowCamera>().enabled, "取消后未归还队友跟随摄像机");
                        if (++_cycle < 4) { _step = 0; break; }
                        Finish(!_errors, "四轮真实 A/D 移动、属性/关系阶段中途松开 Shift、恢复步长和镜头全部通过。");
                        return;
                }
                _due = Time.unscaledTimeAsDouble + 0.35;
            }
            catch (Exception error) { Finish(false, $"cycle={_cycle}, step={_step}, position={_player.transform.position}, timeScale={Time.timeScale}, managerScale={(TimeManager.Instance != null ? TimeManager.Instance.TimeScale : -1f)}\n{error}"); }
        }

        private static void Check(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
        }
        private static void Screenshot(string name)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _surface;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Output, name), pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
        }
        private static void Finish(bool success, string detail)
        {
            EditorApplication.update -= Step;
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "result.json"), JsonUtility.ToJson(new Report { Success = success, Detail = detail }, true));
            Debug.Log("[Wheel Cancel] " + detail);
            EditorApplication.Exit(success ? 0 : 1);
        }
        [Serializable] private sealed class Report { public bool Success; public string Detail; }
    }
}
