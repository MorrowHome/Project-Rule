using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;

namespace RuleGame.Editor.Animations
{
    /// <summary>可丢弃工程内，以真实输入/物理攻击检查范围、伤害、暂停、回溯及剑气渲染。</summary>
    public static class PlayerSwordWaveValidation
    {
        private const string Pending = "RuleGame.Player.SwordWaveValidation";
        private static PlayerController _player;
        private static PlayerAttackVfx _vfx;
        private static EntityStatus _status, _inside, _outside;
        private static Keyboard _keyboard;
        private static PlayerAttackCue _cue;
        private static int _strikes, _step, _passed;
        private static float _due, _progress;
        private static Vector3 _position;
        private static float _lightPower;
        private static double _deadline;
        private static readonly MethodInfo Hit = typeof(PlayerController).GetMethod("PerformAttackHit", BindingFlags.Instance | BindingFlags.NonPublic);
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/PlayerSwordWaveValidation.txt"));

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃工程副本运行。");
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
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            };
        }

        private static void Begin()
        {
            try
            {
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                _keyboard = InputSystem.AddDevice<Keyboard>(); _keyboard.MakeCurrent();
                TimeManager.Initialize(); WorldRegistry.Initialize();
                RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                SnapshotRoster.Initialize();
                SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);
                var combat = new GameObject("Combat").AddComponent<CombatSystem>();
                var floor = new GameObject("Floor") { layer = 8 };
                floor.transform.position = new Vector3(0, -.5f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(60, 1);
                var floorRenderer = floor.AddComponent<SpriteRenderer>();
                floorRenderer.sprite = Sprite.Create(Texture2D.whiteTexture,
                    new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                    Vector2.one * .5f, Texture2D.whiteTexture.width);
                floorRenderer.drawMode = SpriteDrawMode.Sliced;
                floorRenderer.size = new Vector2(60, 1);
                floorRenderer.color = new Color(.16f, .22f, .3f);
                floorRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/P4Whitebox.mat");
                var litProbe = new GameObject("LitSurfaceProbe").AddComponent<SpriteRenderer>();
                litProbe.sprite = floorRenderer.sprite;
                litProbe.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
                litProbe.color = new Color(.3f, .35f, .4f);
                litProbe.transform.position = new Vector3(1.7f, .25f);
                litProbe.transform.localScale = new Vector3(.35f, .35f, 1);
                var go = new GameObject("Player"); go.SetActive(false);
                go.transform.position = new Vector3(0, 1.02f);
                go.AddComponent<BoxCollider2D>().size = new Vector2(1, 2);
                _status = go.AddComponent<EntityStatus>();
                _player = go.AddComponent<PlayerController>();
                var inputs = ScriptableObject.CreateInstance<InputActionAsset>();
                var map = inputs.AddActionMap("Player");
                map.AddAction("Move", InputActionType.Value).AddCompositeBinding("2DVector")
                    .With("Left", "<Keyboard>/a").With("Down", "<Keyboard>/s");
                map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
                map.AddAction("Attack", InputActionType.Button, "<Keyboard>/j");
                map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/c");
                var data = new SerializedObject(_player);
                data.FindProperty("_inputActions").objectReferenceValue = inputs;
                data.FindProperty("_solidLayer").intValue = 1 << 8;
                data.FindProperty("_attackMask").intValue = 1 << 9;
                data.FindProperty("_combat").objectReferenceValue = combat;
                data.FindProperty("_attackBoxSize").vector2Value = new Vector2(3.2f, 1.8f);
                data.FindProperty("_attackOffset").floatValue = 1.7f;
                data.ApplyModifiedPropertiesWithoutUndo();
                var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false);
                visual.AddComponent<SpriteRenderer>().sharedMaterial = floorRenderer.sharedMaterial;
                visual.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Art/Animations/Hero/Hero.controller");
                go.AddComponent<PlayerAnimationDriver>();
                go.SetActive(true);
                _vfx = go.GetComponent<PlayerAttackVfx>();
                Check(_vfx != null, "既有动画驱动自动接入剑气，无需修改场景");
                _player.OnAttackStruck += cue => { _cue = cue; _strikes++; };
                _inside = Dummy("Inside", new Vector3(3.22f, 1.02f));
                _outside = Dummy("Outside", new Vector3(3.55f, 1.02f));
                Physics2D.SyncTransforms();
                _due = Time.time + .2f;
                _deadline = EditorApplication.timeSinceStartup + 35;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static EntityStatus Dummy(string name, Vector3 position)
        {
            var go = new GameObject(name) { layer = 9 }; go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = new Vector2(.12f, .3f);
            var status = go.AddComponent<EntityStatus>();
            go.AddComponent<EntityDamageable>();
            return status;
        }

        private static MeshRenderer[] Waves() => UnityEngine.Object.FindObjectsByType<MeshRenderer>()
            .Where(r => r.name == "PlayerSwordWave").ToArray();
        private static MeshRenderer Visible() => Waves().First(r => r.enabled);
        private static Light2D SwordLight() => UnityEngine.Object.FindObjectsByType<Light2D>()
            .Single(light => light.name == "PlayerSwordLight");
        private static MeshRenderer[] Glows() => UnityEngine.Object.FindObjectsByType<MeshRenderer>()
            .Where(renderer => renderer.name == "PlayerSwordGlow").ToArray();
        private static SpriteRenderer[] Reflections() => UnityEngine.Object.FindObjectsByType<SpriteRenderer>()
            .Where(renderer => renderer.name == "PlayerSwordReflection").ToArray();
        private static bool LightCleared() => !SwordLight().enabled && Glows().All(r => !r.enabled)
            && Reflections().All(r => !r.enabled);
        private static float Progress(MeshRenderer renderer)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            return block.GetFloat("_Progress");
        }
        private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("剑气验证超时，步骤 " + _step);
                if (Time.time < _due) return;
                switch (_step)
                {
                    case 0:
                        Keys(Key.J); _due = Time.time + .06f; _step++; break;
                    case 1:
                        Keys();
                        Check(_strikes == 0 && Waves().All(r => !r.enabled), "起手阶段没有提前播放剑气或结算");
                        _step++; break;
                    case 2:
                        if (_strikes == 0) return;
                        Check(_strikes == 1 && !_cue.Heavy, "真实轻击 FixedUpdate 仅发一次出手事件");
                        Check(_inside.Health == 90f && _outside.Health == 100f, "远端盒内目标命中，盒外目标不受伤");
                        Check(_cue.Size == new Vector2(3.2f, 1.8f), "实时配置攻击范围直接传给表现");
                        Check(Vector2.Distance(Visible().bounds.center, _cue.Center) < .001f
                            && Vector2.Distance(Visible().bounds.size, _cue.Size) < .001f, "剑气世界边界匹配实际判定盒");
                        Capture("PlayerSwordWave_Light.png");
                        _position = Visible().transform.position; _progress = Progress(Visible());
                        _lightPower = SwordLight().intensity;
                        Check(SwordLight().enabled && _lightPower > 0f && Glows().Any(r => r.enabled), "出手同时触发柔光与真实 2D 灯光");
                        Check(Reflections().Count(r => r.enabled) >= 2, "当前 Unlit 角色与地面接入反光叠层");
                        _status.SetPaused(true); _player.transform.position += Vector3.right * 4f;
                        _due = Time.time + .18f; _step++; break;
                    case 3:
                        Check(Visible().transform.position == _position, "出手后移动不会把剑气拖出命中位置");
                        Check(Mathf.Approximately(Progress(Visible()), _progress), "实体暂停冻结剑气寿命");
                        Check(Mathf.Approximately(SwordLight().intensity, _lightPower), "暂停时补光亮度冻结");
                        _status.SetPaused(false); _due = Time.time + .9f; _step++; break;
                    case 4:
                        Check(Waves().All(r => !r.enabled) && _strikes == 1 && _inside.Health == 90f, "消退并关闭渲染，尾迹不追加伤害");
                        Check(LightCleared(), "寿命结束关闭柔光、灯光和反光");
                        Keys(Key.A); _due = Time.time + .1f; _step++; break;
                    case 5:
                        Keys(Key.J); _due = Time.time + .05f; _step++; break;
                    case 6:
                        Keys(); _step++; break;
                    case 7:
                        if (_strikes < 2) return;
                        Check(_cue.Facing < 0f && Visible().transform.localScale.x < 0f, "真实左向攻击镜像剑气");
                        Check(_cue.Center.x < _player.transform.position.x, "左向剑气出现在角色左侧");
                        _due = Time.time + .9f; _step++; break;
                    case 8:
                        Keys(Key.S, Key.J); _due = Time.time + .05f; _step++; break;
                    case 9:
                        Keys(); _step++; break;
                    case 10:
                        if (_strikes < 3) return;
                        Check(_cue.Heavy && _cue.Size == new Vector2(1.4f, 1.2f), "真实下加攻击走独立重击范围");
                        var properties = new MaterialPropertyBlock(); Visible().GetPropertyBlock(properties);
                        Color tint = properties.GetColor("_Tint");
                        Check(properties.GetFloat("_Heavy") == 1f && tint.r > .9f && tint.b > .9f,
                            "重击使用更厚的亮白斩击");
                        Capture("PlayerSwordWave_Heavy.png");
                        SnapshotSystem.Instance.Restore(SnapshotSystem.Instance.Capture());
                        Check(Waves().All(r => !r.enabled), "世界回溯清掉旧时间线剑气");
                        Check(LightCleared(), "世界回溯同步清理光效");
                        for (int i = 0; i < 20; i++) Hit.Invoke(_player, new object[] { 0f, 0f, Vector2.one, .8f, false });
                        Check(Waves().Length == 3, "连续出手只复用三个渲染槽位");
                        Check(Glows().Length == 3 && Reflections().Length <= 12, "光效固定三个柔光槽位与最多十二个反光槽位");
                        _status.ApplyHealthDelta(-100f);
                        Check(Waves().All(r => !r.enabled), "死亡立即清理剑气");
                        Check(LightCleared(), "死亡同步关闭全部补光");
                        _status.InitializeVitals(100, 100);
                        Hit.Invoke(_player, new object[] { 0f, 0f, Vector2.one, .8f, false });
                        _vfx.enabled = false;
                        Check(Waves().All(r => !r.enabled), "停用组件清理剑气与事件订阅");
                        Check(LightCleared(), "停用时同步关闭全部补光");
                        _vfx.enabled = true;
                        Hit.Invoke(_player, new object[] { 0f, 0f, Vector2.one, .8f, false });
                        Check(Waves().Count(r => r.enabled) == 1, "重新启用正常复用且不重复订阅");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static void Capture(string name)
        {
            var go = new GameObject("SwordPreviewCamera");
            var camera = go.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 2f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .04f, .07f);
            camera.transform.position = new Vector3(_cue.Center.x - _cue.Facing * .6f, _cue.Center.y, -10f);
            var target = new RenderTexture(960, 540, 24);
            camera.targetTexture = target; camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Output), name), pixels.EncodeToPNG());
            // 排除原角色图自带的白色刀光，必须是新增 Mesh 确实贡献了像素。
            Color[] withWave = pixels.GetPixels();
            MeshRenderer[] visible = Waves().Where(wave => wave.enabled).ToArray();
            foreach (var wave in visible) wave.enabled = false;
            camera.Render(); pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
            Color[] withoutWave = pixels.GetPixels();
            Check(withWave.Where((c, i) => Vector3.Distance(new Vector3(c.r, c.g, c.b),
                new Vector3(withoutWave[i].r, withoutWave[i].g, withoutWave[i].b)) > .04f).Count() > 500,
                "剑气独立于原角色图实际贡献像素：" + name);
            Check(withWave.Where((c, i) => c.r > .85f && c.g > .85f && c.b > .85f
                && c.r - withoutWave[i].r > .15f).Count() > 250, "刀光具有大面积亮白实心轮廓：" + name);
            Vector3 corner = camera.WorldToScreenPoint((Vector3)_cue.Center + new Vector3(
                _cue.Size.x * _cue.Facing * .43f, _cue.Size.y * .42f));
            int cornerPixel = Mathf.Clamp((int)corner.y, 0, 539) * 960 + Mathf.Clamp((int)corner.x, 0, 959);
            Check(Mathf.Abs(withWave[cornerPixel].r - withoutWave[cornerPixel].r)
                + Mathf.Abs(withWave[cornerPixel].g - withoutWave[cornerPixel].g)
                + Mathf.Abs(withWave[cornerPixel].b - withoutWave[cornerPixel].b) < .01f,
                "月牙外透明，没有矩形底框：" + name);
            if (!_cue.Heavy)
            {
                Color floorLit = Sample(camera, pixels, new Vector3(1.7f, -.15f));
                Color probeLit = Sample(camera, pixels, new Vector3(1.7f, .25f));
                SpriteRenderer[] reflections = Reflections().Where(r => r.enabled).ToArray();
                foreach (var reflection in reflections) reflection.enabled = false;
                SwordLight().enabled = false;
                camera.Render(); pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                Color floorDark = Sample(camera, pixels, new Vector3(1.7f, -.15f));
                Color probeDark = Sample(camera, pixels, new Vector3(1.7f, .25f));
                Check(floorLit.b > floorDark.b + .005f, "实际像素确认 Unlit 地面接收到局部反光");
                Check(probeLit.b > probeDark.b + .005f, "实际像素确认 Lit 表面接收到 Light2D 补光");
                foreach (var reflection in reflections) reflection.enabled = true;
                SwordLight().enabled = true;
            }
            foreach (var wave in visible) wave.enabled = true;
            // 只改变渲染属性采集轮廓演变，保留玩法时钟和槽位年龄。
            var block = new MaterialPropertyBlock();
            var waveRenderer = visible[0]; waveRenderer.GetPropertyBlock(block);
            float progress = block.GetFloat("_Progress");
            MeshRenderer[] glows = Glows().Where(glow => glow.enabled).ToArray();
            var glowBlocks = glows.Select(glow => { var b = new MaterialPropertyBlock(); glow.GetPropertyBlock(b); return b; }).ToArray();
            SpriteRenderer[] reflectors = Reflections().Where(r => r.enabled).ToArray();
            var reflectionBlocks = reflectors.Select(r => { var b = new MaterialPropertyBlock(); r.GetPropertyBlock(b); return b; }).ToArray();
            float lightPower = SwordLight().intensity;
            float[] reflectedPower = reflectionBlocks.Select(b => b.GetFloat("_Intensity")).ToArray();
            int firstWhitePixels = 0, lastWhitePixels = 0;
            for (int frame = 0; frame < 10; frame++)
            {
                float t = frame / 9f;
                block.SetFloat("_Progress", t); waveRenderer.SetPropertyBlock(block);
                for (int i = 0; i < glows.Length; i++)
                {
                    glowBlocks[i].SetFloat("_Progress", t); glows[i].SetPropertyBlock(glowBlocks[i]);
                }
                for (int i = 0; i < reflectors.Length; i++)
                {
                    reflectionBlocks[i].SetFloat("_Intensity", reflectedPower[i] * (1 - t) * (1 - t));
                    reflectors[i].SetPropertyBlock(reflectionBlocks[i]);
                }
                SwordLight().intensity = lightPower * (1 - t) * (1 - t);
                camera.Render(); pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Output),
                    Path.GetFileNameWithoutExtension(name) + "_" + frame.ToString("D2") + ".png"), pixels.EncodeToPNG());
                int whitePixels = pixels.GetPixels().Where((c, i) => c.r > .85f && c.g > .85f
                    && c.b > .85f && c.r - withoutWave[i].r > .15f).Count();
                if (frame == 0) firstWhitePixels = whitePixels;
                if (frame == 9) lastWhitePixels = whitePixels;
            }
            Check(firstWhitePixels > 250 && lastWhitePixels == 0, "刀光从实心爆发扫除至完全消失：" + name);
            block.SetFloat("_Progress", progress); waveRenderer.SetPropertyBlock(block);
            for (int i = 0; i < glows.Length; i++)
            {
                glowBlocks[i].SetFloat("_Progress", progress); glows[i].SetPropertyBlock(glowBlocks[i]);
            }
            for (int i = 0; i < reflectors.Length; i++)
            {
                reflectionBlocks[i].SetFloat("_Intensity", reflectedPower[i]); reflectors[i].SetPropertyBlock(reflectionBlocks[i]);
            }
            SwordLight().intensity = lightPower;
            RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
        }

        private static Color Sample(Camera camera, Texture2D pixels, Vector3 world)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            return pixels.GetPixel(Mathf.Clamp((int)screen.x, 0, pixels.width - 1),
                Mathf.Clamp((int)screen.y, 0, pixels.height - 1));
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            _passed++; Debug.Log("PASS: " + name);
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, "Passed: " + _passed + "\n" + (error ?? "All checks passed."));
            if (error != null) Debug.LogError(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
