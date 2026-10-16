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
    /// <summary>只在工程副本运行；使用真实兔子 Prefab、资源与组件验证动画和弹体生命周期。</summary>
    public static class RabbitAnimationValidation
    {
        private const string Pending = "RuleGame.RabbitAnimationValidation";
        private static EnemyActor _actor;
        private static EnemySpriteAnimator _animator;
        private static EnemyAnimationSet _set;
        private static EnemyProjectile _shot;
        private static SpriteRenderer _body;
        private static int _passed, _stage, _frame;
        private static double _deadline;
        private static float _until;
        private static object _saved;
        private static Sprite _pausedSprite;

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
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/RabbitBoss.prefab");
                Check(prefab != null, "加载正式兔子 Prefab");
                _actor = UnityEngine.Object.Instantiate(prefab).GetComponent<EnemyActor>();
                _actor.Definition = UnityEngine.Object.Instantiate(_actor.Definition);
                _actor.Definition.Energy = 0f;
                _actor.Target = new GameObject("RabbitTarget").transform;
                _actor.Target.position = new Vector3(6f, 0f, 0f);
                _actor.Status.SetPaused(true);
                _animator = _actor.GetComponentInChildren<EnemySpriteAnimator>();
                _set = _animator.AnimationSet;
                _body = _animator.GetComponent<SpriteRenderer>();
                var material = new Material(Shader.Find("Sprites/Default"));
                _body.sharedMaterial = material;
                var presenter = _actor.GetComponent<EnemyVfxPresenter>();
                presenter.Profile = UnityEngine.Object.Instantiate(presenter.Profile);
                presenter.Profile.Material = presenter.Profile.ProjectileMaterial = material;
                presenter.Profile.BeamMaterial = material;
                _actor.Feedback += feedback =>
                {
                    if (feedback.Kind == EnemyFeedbackKind.ProjectileSpawn) _shot = (EnemyProjectile)feedback.Subject;
                };
                _deadline = EditorApplication.timeSinceStartup + 45;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static EnemySpriteSequence Sequence(string action)
            => Array.Find(_set.Sequences, sequence => sequence.Action == action);
        private static void Step(float delta) => _actor.Brain.Tick(delta);
        private static void RenderRestored()
        {
            _animator.SendMessage("RestoreTime");
            _animator.SendMessage("LateUpdate");
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("兔子动画验证超时");
                if (_actor.Brain == null) return;
                switch (_stage)
                {
                    case 0:
                        _actor.enabled = false;
                        foreach (EnemySpriteSequence sequence in _set.Sequences)
                        {
                            Check(sequence.Frames.Length > 0 && Array.TrueForAll(sequence.Frames, sprite => sprite != null), sequence.Action + " 引用完整");
                            Check(sequence.FrameRate == 8f && sequence.NativeFrameRate, sequence.Action + " 使用原生八帧节奏");
                        }
                        Check(_animator.transform.localScale == Vector3.one && Mathf.Approximately(_animator.transform.localPosition.y, -1.5f), "正式精灵按固定尺寸和脚底锚点显示");
                        Check(_body.color == Color.white, "角色保留原始颜色");
                        _actor.Status.SetPaused(false);
                        _until = Time.time + .28f; _stage++;
                        break;
                    case 1:
                        if (Time.time < _until) return;
                        Check(Array.IndexOf(Sequence("Pop").Frames, _body.sprite) >= 2, "实际 LateUpdate 播放冒出序列");
                        _actor.Status.SetPaused(true); _pausedSprite = _body.sprite;
                        _until = Time.time + .3f; _stage++;
                        break;
                    case 2:
                        if (Time.time < _until) return;
                        Check(_body.sprite == _pausedSprite, "单体暂停冻结精灵帧");
                        _actor.Status.SetPaused(false);
                        _actor.Definition.RabbitThrowInterval = 4f;
                        Step(_actor.AnimationDuration + .01f);
                        Check(_actor.Brain.StateName == "Throw" && _actor.AnimationDuration == 4f, "完整冒出后进入投掷，保留自定义投掷间隔");
                        Step(1.49f);
                        Check(_shot == null, "第十二帧之前不发射");
                        Step(.03f);
                        Check(_shot != null, "加长间隔仍在原画出手帧产生真实投掷物");
                        _shot.enabled = false;
                        _saved = _shot.CaptureSnapshotState();
                        var view = _shot.GetComponent<EnemyProjectileView>();
                        view.SendMessage("LateUpdate");
                        var visual = _shot.transform.Find("ProjectileVisual").GetComponent<SpriteRenderer>();
                        var profile = _actor.GetComponent<EnemyVfxPresenter>().Profile;
                        Check(visual.color == Color.white && Array.IndexOf(profile.ProjectileAnimations[_shot.VisualIndex].Frames, visual.sprite) >= 0, "三类投掷物使用原画与独立弹体节点");
                        int variant = _shot.VisualIndex;
                        float age = _shot.Age;
                        _shot.gameObject.SetActive(false);
                        _actor.Shoot(Vector2.right, 1f, .18f);
                        _shot.RestoreSnapshotState(_saved);
                        view.SendMessage("LateUpdate");
                        Check(_shot.VisualIndex == variant && _shot.Age == age, "对象池复用后快照恢复弹体种类与帧进度");
                        Check(_shot.transform.localScale == Vector3.one * _shot.Radius * 2f, "正式弹体不改变物理半径和根节点比例");
                        _shot.gameObject.SetActive(false);
                        Step(_actor.AnimationDuration + .01f);
                        Check(_actor.Brain.StateName == "Rest", "投掷完成后保留喘息窗口");
                        object rest = _actor.CaptureSnapshotState();
                        _actor.Target.position = _actor.transform.position + Vector3.right * 2f;
                        Step(_actor.Brain.RestRemaining + .01f); Step(.02f);
                        Check(_actor.Brain.StateName == "Hide", "近身时先播放缩回");
                        Step(_actor.AnimationDuration + .01f); Step(.02f);
                        _actor.Status.SetPaused(true); RenderRestored();
                        Check(_actor.Brain.StateName == "Burrow" && _body.color.a == 1f
                            && Array.IndexOf(Sequence("Burrow").Frames, _body.sprite) >= 0,
                            "藏身撤退使用空帽跳跃，移除半透明占位");
                        object hidden = _actor.CaptureSnapshotState();
                        _actor.Status.SetPaused(false); Step(_actor.Definition.RabbitBurrowDuration + .01f);
                        _actor.RestoreSnapshotState(hidden);
                        _actor.Status.SetPaused(true); RenderRestored();
                        Check(Array.IndexOf(Sequence("Burrow").Frames, _body.sprite) >= 0, "快照恢复藏身精灵进度");
                        _actor.RestoreSnapshotState(rest);
                        _actor.Target.position = _actor.transform.position + Vector3.right * 6f;
                        _actor.Status.SetPaused(false);
                        _actor.Status.ApplyHealthDelta(_actor.Definition.Health * .5f - _actor.Status.Health);
                        Step(.02f);
                        Check(_actor.Brain.StateName == "Flip" && _actor.AnimationDuration >= 3f, "翻转完整播放缩回和帽子反转");
                        _actor.Status.SetPaused(true);
                        RenderRestored();
                        Check(_body.sprite == Sequence("Flip").Frames[0], "翻转从缩回第一帧开始");
                        _actor.Status.SetPaused(false);
                        Step(_actor.AnimationDuration + .01f);
                        Check(_actor.Brain.Phase == 2 && _actor.Brain.StateName == "AirReposition" && _actor.IsFlying, "翻转后先进入第二阶段空中换位");
                        Step(_actor.Definition.RabbitAirMoveDuration + .01f);
                        _actor.Status.SetPaused(true); RenderRestored();
                        Check(Array.IndexOf(Sequence("Flight").Frames, _body.sprite) >= 0, "第二阶段保持倒置帽子外观");
                        _actor.Status.SetPaused(false);
                        Step(_actor.AnimationDuration + .01f);
                        _actor.Status.SetPaused(true); RenderRestored();
                        Check(_actor.Brain.StateName == "Rest" && Array.IndexOf(Sequence("Flight").Frames, _body.sprite) >= 0, "第二阶段休息不变回露头兔子");
                        _actor.Status.SetPaused(false);
                        _actor.Status.ApplyHealthDelta(19f - _actor.Status.Health);
                        Step(.02f);
                        _actor.Status.SetPaused(true); RenderRestored();
                        Check(_actor.Defeated && _body.sprite == Sequence("Dead").Frames[0], "谢幕播放正式死亡帧");
                        _actor.Status.SetPaused(false); Step(_actor.AnimationDuration + .01f);
                        Check(!_actor.gameObject.activeSelf, "死亡序列完成后停用，保留回溯能力");
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            _passed++; Debug.Log("PASS: " + message);
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Pending, false);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/RabbitAnimationValidation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, $"Passed: {_passed}\nFailed: {(error == null ? 0 : 1)}\n{error}");
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
