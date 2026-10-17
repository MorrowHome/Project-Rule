using System;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>在工程副本中检查真实弹体贴图跨越闪光淡出周期、暂停和对象池复用时持续可见。</summary>
    public static class EnemyProjectileViewValidation
    {
        private const string Pending = "RuleGame.Enemy.ProjectileViewValidation";
        private static EnemyActor _source, _plainSource;
        private static EnemyProjectile _shot, _pooled;
        private static EnemyVfxProfile _profile;
        private static SpriteRenderer _visual;
        private static int _step, _passed, _samples, _lastFrame = -1;
        private static float _until;
        private static double _deadline;
        private static Vector3 _pausedPosition;
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/EnemyProjectileViewValidation.txt"));

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
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            };
        }
        private static void Begin()
        {
            try
            {
                TimeManager.Initialize(); WorldRegistry.Initialize();
                RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                // 使用生产配置中的真实弹体和闪光序列，材质换成独立测试工程可用的内置材质。
                var asset = AssetDatabase.LoadAssetAtPath<EnemyVfxProfile>("Assets/_Project/Data/Enemies/Vfx/BatBoss_Vfx.asset");
                Check(asset != null && asset.ProjectileSprite != null && asset.Shot != null, "加载真实蝙蝠弹体与发射闪光");
                _profile = UnityEngine.Object.Instantiate(asset);
                _profile.Material = _profile.BeamMaterial = new Material(Shader.Find("Sprites/Default"));
                _source = MakeSource("Boss"); _plainSource = MakeSource("PlainEnemy");
                _source.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.ProjectileSpawn) return;
                    _shot = (EnemyProjectile)cue.Subject;
                    var view = _shot.GetComponent<EnemyProjectileView>();
                    if (view == null) view = _shot.gameObject.AddComponent<EnemyProjectileView>();
                    view.Configure(_profile);
                    _visual = _shot.transform.Find("ProjectileVisual").GetComponent<SpriteRenderer>();
                };
                _source.Shoot(Vector2.right, .5f);
                _until = Time.time + _profile.Shot.Duration * 3.2f;
                _deadline = EditorApplication.timeSinceStartup + 20;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static EnemyActor MakeSource(string name)
        {
            var go = new GameObject(name); go.layer = 9; go.SetActive(false);
            var actor = go.AddComponent<EnemyActor>(); actor.enabled = false;
            actor.Definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/BatBoss_Definition.asset");
            actor.Target = new GameObject(name + "Target").transform;
            actor.ProjectileSprite = _profile.ProjectileSprite;
            go.SetActive(true);
            return actor;
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("弹体表现验证超时");
                switch (_step)
                {
                    case 0:
                        if (!_shot.gameObject.activeInHierarchy || _visual.sprite != _profile.ProjectileSprite || !_visual.enabled || _visual.color.a <= 0f)
                            throw new InvalidOperationException("飞行中的弹体被发射闪光帧替换，淡出帧会造成周期性消失");
                        _samples++;
                        if (Time.time < _until) return;
                        Check(_samples > 10, "跨越三个发射闪光周期持续保持可见弹芯");
                        Check(_shot.transform.position.x > _source.transform.position.x + 1f, "验证期间弹体实际持续飞行");
                        Check(!_shot.GetComponent<SpriteRenderer>().enabled, "只有独立弹体子节点绘制，避免重复叠加");
                        _pausedPosition = _shot.transform.position;
                        TimeManager.Instance.SetPaused(true); _until = Time.fixedTime + .2f; _step++; break;
                    case 1:
                        if (Time.fixedTime < _until) return;
                        Check(_visual.enabled && _visual.sprite == _profile.ProjectileSprite
                            && Vector3.Distance(_shot.transform.position, _pausedPosition) < .01f, "暂停保持弹体位置与外观");
                        TimeManager.Instance.SetPaused(false);
                        _shot.RestoreSnapshotState(_shot.CaptureSnapshotState()); _source.RestoreLifeFlag(false);
                        _until = Time.time + .1f; _step++; break;
                    case 2:
                        if (Time.time < _until) return;
                        Check(_visual.enabled && _visual.sprite == _profile.ProjectileSprite, "快照恢复后弹芯仍可见");
                        _pooled = _shot; _shot.gameObject.SetActive(false);
                        _profile.Accent = Color.cyan;
                        _source.Shoot(Vector2.up, .5f, 0f, .4f);
                        Check(_shot == _pooled && _visual.enabled && _visual.color == Color.cyan, "对象池复用重新应用弹体颜色");
                        _until = Time.time + .1f; _step++; break;
                    case 3:
                        if (Time.time < _until) return;
                        Check(_visual.sprite == _profile.ProjectileSprite && _visual.transform.localScale.y < _visual.transform.localScale.x,
                            "地面冲击波保持原有扁平外观");
                        _shot.gameObject.SetActive(false);
                        _plainSource.Shoot(Vector2.right, .5f);
                        _until = Time.time + .1f; _step++; break;
                    case 4:
                        if (Time.time < _until) return;
                        Check(_pooled.gameObject.activeSelf && _pooled.Source == _plainSource, "无特效杂兵复用同一弹体");
                        Check(_pooled.GetComponent<SpriteRenderer>().enabled && !_visual.enabled
                            && !_pooled.GetComponent<TrailRenderer>().emitting, "无特效复用恢复原始外观并关闭旧拖尾");
                        _pooled.gameObject.SetActive(false);
                        _source.Shoot(Vector2.right, .5f);
                        _until = Time.time + .1f; _step++; break;
                    case 5:
                        if (Time.time < _until) return;
                        Check(_shot == _pooled && _visual.enabled && _visual.sprite == _profile.ProjectileSprite
                            && _shot.GetComponent<TrailRenderer>().emitting, "再次复用为 Boss 弹体时恢复弹芯和拖尾");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            _passed++; Debug.Log("PASS: " + name);
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, $"Passed: {_passed}\nFailed: {(error == null ? 0 : 1)}\nSamples: {_samples}\n{error}\n");
            if (error != null) Debug.LogError(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
