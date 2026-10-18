using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Gameplay.Scene.DemonCastle;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Development.DemonCastle;

namespace RuleGame.Editor.Level
{
    /// <summary>仅在独立批处理工程验证资产、编辑器撤销以及真实 Play Mode 物理/机关链路。</summary>
    public static class DemonCastleValidation
    {
        private const string Pending = "RuleGame.DemonCastleValidation";
        private static readonly List<string> Results = new List<string>();
        private static int _passed, _failed;
        private static double _started;
        private static PlayerController _player;
        private static Snapshot _baseline;
        private static SimulationMode2D _previousMode;
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在独立工程批处理运行。");
            try
            {
                var menuItems = new[] { typeof(DemonCastleBuilder), typeof(DemonCastleMenu) }
                    .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false)).Cast<MenuItem>().ToArray();
                Check(menuItems.Length > 0 && menuItems.All(item => item.menuItem.StartsWith("Tools/规则游戏/地图组件/魔王城/", StringComparison.Ordinal)),
                    "all DemonCastle menu entries use requested nested path");
                Check(menuItems.Select(item => item.menuItem).Distinct().Count() == menuItems.Length, "DemonCastle menu entries have unique paths");
                DemonCastleBuilder.BuildDemo();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Check(Directory.GetFiles(DemonCastleBuilder.Folder, "*.prefab").Length == DemonCastleBuilder.Names.Length, "all catalog prefab assets exist");
                foreach (string name in DemonCastleBuilder.Names)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DemonCastleBuilder.Folder + "/" + name + ".prefab");
                    Check(asset != null && asset.GetComponentsInChildren<MonoBehaviour>(true).All(x => x != null), name + " loads without missing scripts");
                }
                DemonCastleArtValidation.CheckAssets(Check);
                var hashes = DemonCastleBuilder.Names.ToDictionary(x => x, x => File.ReadAllText(DemonCastleBuilder.Folder + "/" + x + ".prefab"));
                DemonCastleBuilder.BuildPrefabs();
                Check(hashes.All(x => x.Value == File.ReadAllText(DemonCastleBuilder.Folder + "/" + x.Key + ".prefab")), "rebuild preserves authored prefabs");
                var parent = new GameObject("Parent"); parent.transform.position = new Vector3(100f, 30f);
                var placed = DemonCastleMenu.PlaceAt("DemonPuzzleRoom", new Vector3(107f, 35f), parent.transform);
                Check(placed.transform.position == new Vector3(107f, 35f) && placed.transform.parent == parent.transform && PrefabUtility.IsPartOfPrefabInstance(placed), "placement keeps world position, parent and prefab connection");
                var instanceId = placed.GetEntityId(); Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(placed == null, "placement Undo removes entire assembly"); Undo.PerformRedo();
                var redone = EditorUtility.EntityIdToObject(instanceId) as GameObject;
                Check(redone != null && PrefabUtility.IsPartOfPrefabInstance(redone), "placement Redo restores linked assembly");
                Results.Clear(); SessionState.SetInt(Pending + ".Edit", _passed);
                EditorSceneManager.OpenScene(DemonCastleBuilder.ScenePath);
                SessionState.SetInt(Pending, 1); Subscribe(); EditorApplication.isPlaying = true;
            }
            catch (Exception error) { FinishError(error); }
        }
        [InitializeOnLoadMethod]
        private static void Resume() { if (Application.isBatchMode && SessionState.GetInt(Pending, 0) != 0) Subscribe(); }
        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= OnPlay;
            EditorApplication.playModeStateChanged += OnPlay;
        }
        private static void OnPlay(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetInt(Pending, 0) == 1)
            { _started = EditorApplication.timeSinceStartup; EditorApplication.update += WaitForReady; }
        }
        private static void WaitForReady()
        {
            if (EditorApplication.timeSinceStartup - _started < 1f) return;
            if (RuleSystem.Instance != null && SnapshotSystem.Instance != null && UnityEngine.Object.FindAnyObjectByType<PlayerController>() != null)
            {
                EditorApplication.update -= WaitForReady; SessionState.SetInt(Pending, 2);
                try { Run(); } catch (Exception error) { FinishError(error); }
            }
            else if (EditorApplication.timeSinceStartup - _started > 25f)
            { EditorApplication.update -= WaitForReady; FinishError(new Exception("Runtime bootstrap timed out")); }
        }
        private static void Check(bool value, string name)
        { if (!value) throw new Exception(name); _passed++; Results.Add("PASS " + name); Debug.Log("[DemonCastleValidation] PASS " + name); }
        private static T Find<T>(Predicate<T> predicate = null) where T : Component
        {
            foreach (var item in UnityEngine.Object.FindObjectsByType<T>()) if (predicate == null || predicate(item)) return item;
            throw new Exception("Missing " + typeof(T).Name);
        }
        private static void Case(string name, Action test)
        {
            try
            {
                TimeManager.Instance.SetPaused(false); RuleSystem.Instance.ClearAll();
                SnapshotSystem.Instance.Restore(_baseline); Physics2D.simulationMode = SimulationMode2D.Script;
                _player.TeleportTo(new Vector2(-5f, 1f)); Physics2D.SyncTransforms(); test();
            }
            catch (Exception error) { _failed++; Results.Add("FAIL " + name + ": " + error); Debug.LogError("[DemonCastleValidation] FAIL " + name + ": " + error); }
        }
        private static void Tick(Component component, int count, bool simulate = false)
        {
            for (int i = 0; i < count; i++)
            {
                component.SendMessage("FixedUpdate");
                if (simulate) { Physics2D.Simulate(Time.fixedDeltaTime); Physics2D.SyncTransforms(); }
            }
        }
        private static void OnTop(Collider2D surface)
        {
            var p = _player.GetComponent<Collider2D>();
            _player.TeleportTo(new Vector2(surface.bounds.center.x, surface.bounds.max.y + p.bounds.extents.y + .01f));
            Physics2D.SyncTransforms();
        }
        private static void Jump()
        {
            var evt = typeof(PlayerController).GetField("OnJumped", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_player) as Action;
            evt?.Invoke();
        }
        private static void Run()
        {
            _passed = SessionState.GetInt(Pending + ".Edit", 0);
            _player = Find<PlayerController>(); Find<DemonCastleLabControls>().enabled = false;
            var receiver = _player.GetComponent<EntityDamageable>(); receiver.SetInvulnerable(false);
            var serialized = new SerializedObject(receiver); serialized.FindProperty("_invincibleSeconds").floatValue = 0f; serialized.ApplyModifiedPropertiesWithoutUndo();
            _previousMode = Physics2D.simulationMode; Physics2D.simulationMode = SimulationMode2D.Script;
            _baseline = SnapshotSystem.Instance.Capture();
            DemonCastleArtValidation.CheckRuntime(Check);
            SnapshotSystem.Instance.Restore(_baseline);
            try
            {
                Case("firework damage", () =>
                {
                    var bomb = Find<DemonFirework>(); _player.TeleportTo(bomb.Body.position + Vector2.right);
                    Physics2D.SyncTransforms(); var health = _player.GetComponent<EntityStatus>(); float before = health.Health;
                    var extra = new GameObject("DuplicateHurtbox"); extra.SetActive(false); extra.layer = Hurtbox.Layer; extra.transform.SetParent(_player.transform, false);
                    extra.AddComponent<BoxCollider2D>().size = Vector2.one * .5f; extra.AddComponent<Hurtbox>(); extra.SetActive(true); Physics2D.SyncTransforms();
                    var checkpoint = SnapshotSystem.Instance.Capture(); bomb.SetOn(true); bomb.SetOn(true); Tick(bomb, 100);
                    Check(bomb.Spent && Mathf.Abs(before - health.Health - bomb.Damage) < .01f, "firework uses trigger Hurtboxes and damages a multi-box owner once");
                    Tick(bomb, 100); Check(Mathf.Abs(before - health.Health - bomb.Damage) < .01f, "spent firework cannot explode twice");
                    SnapshotSystem.Instance.Restore(checkpoint); Check(!bomb.Spent && !bomb.Armed && bomb.GetComponent<Collider2D>().enabled, "world snapshot restores firework and collision");
                    UnityEngine.Object.DestroyImmediate(extra);
                });
                Case("firework pause and fuse", () =>
                {
                    var bomb = Find<DemonFirework>(); bomb.SetOn(true); Tick(bomb, 8); var saved = bomb.CaptureSnapshotState(); float remaining = bomb.Remaining;
                    TimeManager.Instance.SetPaused(true); Tick(bomb, 100); Check(bomb.Remaining == remaining && !bomb.Spent, "pause freezes fuse");
                    TimeManager.Instance.SetPaused(false); Tick(bomb, 4); bomb.RestoreSnapshotState(saved);
                    Check(bomb.Armed && bomb.Remaining == remaining, "module snapshot restores armed fuse");
                });
                Case("firework switch", () =>
                {
                    var button = Find<CitySwitch>(); var bomb = Find<DemonFirework>();
                    Check(button.FollowMotor == bomb, "firework assembly target is linked");
                    button.SetOn(true); Check(bomb.Armed, "switch target interface ignites firework");
                });
                Case("physical firework button", () =>
                {
                    var button = Find<CitySwitch>(); var shape = button.GetComponent<Collider2D>();
                    var playerShape = _player.GetComponent<Collider2D>();
                    _player.TeleportTo(new Vector2(shape.bounds.center.x, shape.bounds.max.y + playerShape.bounds.extents.y - .04f));
                    Physics2D.SyncTransforms();
                    Check(!Find<DemonFirework>().Armed, "firework starts idle before physical contact");
                    for (int i = 0; i < 15; i++)
                    {
                        _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); Physics2D.SyncTransforms(); button.SendMessage("FixedUpdate");
                    }
                    Check(button.IsOn && Find<DemonFirework>().Armed, "actual player contact presses button and ignites linked firework");
                });
                Case("stalactite actual damage", () =>
                {
                    var rock = Find<DemonStalactite>(x => x.name == "DemonStalactiteMedium");
                    _player.TeleportTo(new Vector2(rock.Body.position.x, .7f)); Physics2D.SyncTransforms();
                    var status = _player.GetComponent<EntityStatus>(); float health = status.Health;
                    Tick(rock, 220, true);
                    Check(Mathf.Abs(health - status.Health - rock.Damage) < .01f, "falling stalactite hits real player through Hurtbox exactly once");
                });
                Case("moving platforms carry real player", () =>
                {
                    foreach (string name in new[] { "DemonCarousel", "DemonSkateboard" })
                    {
                        var motor = Find<CityPathMotor>(x => x.name == name); OnTop(motor.GetComponent<Collider2D>());
                        for (int i = 0; i < 12; i++) { _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                        Physics2D.SyncTransforms(); motor.SetOn(true);
                        Vector2 start = _player.GetComponent<Rigidbody2D>().position, origin = motor.Body.position;
                        for (int i = 0; i < 35; i++)
                        { motor.SendMessage("FixedUpdate"); _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); Physics2D.SyncTransforms(); }
                        Vector2 delta = motor.Body.position - origin, carried = _player.GetComponent<Rigidbody2D>().position - start;
                        Check(delta.magnitude > .3f && (delta - carried).magnitude < .18f, name + " carries player with actual collision movement");
                        motor.SetOn(false); origin = motor.Body.position; start = _player.GetComponent<Rigidbody2D>().position;
                        for (int i = 0; i < 35; i++)
                        { motor.SendMessage("FixedUpdate"); _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); Physics2D.SyncTransforms(); }
                        delta = motor.Body.position - origin; carried = _player.GetComponent<Rigidbody2D>().position - start;
                        Check(delta.magnitude > .3f && (delta - carried).magnitude < .18f, name + " carries player on return path");
                    }
                });
                Case("rust block real pushing", () =>
                {
                    var pushable = Find<RuleGame.Gameplay.Scene.PushableBody>(); var body = pushable.GetComponent<Rigidbody2D>();
                    for (int i = 0; i < 60; i++) Physics2D.Simulate(Time.fixedDeltaTime);
                    Physics2D.SyncTransforms(); var shape = pushable.GetComponent<Collider2D>(); var playerShape = _player.GetComponent<Collider2D>();
                    _player.TeleportTo(new Vector2(shape.bounds.min.x - playerShape.bounds.extents.x - .03f, body.position.y)); Physics2D.SyncTransforms();
                    var sensor = new PushSensor(_player.GetComponent<Rigidbody2D>());
                    Check(sensor.Push(new MotionTuning { PushStrength = 60f, PushProbeDistance = .15f, SolidLayer = 1 << 8 }, Vector2.right, 1f),
                        "player push sensor recognizes rust block through existing interface");
                    Vector2 origin = body.position;
                    for (int i = 0; i < 90; i++) { pushable.ReceivePush(Vector2.right * 60f); Physics2D.Simulate(Time.fixedDeltaTime); }
                    float heavy = body.position.x - origin.x;
                    SnapshotSystem.Instance.Restore(_baseline); Physics2D.simulationMode = SimulationMode2D.Script;
                    RuleSystem.Instance.Request(AttributeId.Mass, RelationId.Weaken, RuleSource.Player);
                    _player.TeleportTo(new Vector2(-5f, 1f));
                    for (int i = 0; i < 60; i++) Physics2D.Simulate(Time.fixedDeltaTime);
                    origin = body.position;
                    for (int i = 0; i < 90; i++) { pushable.ReceivePush(Vector2.right * 60f); Physics2D.Simulate(Time.fixedDeltaTime); }
                    float light = body.position.x - origin.x;
                    Check(light > heavy + .3f, "mass weakening makes physical rust block easier to push; heavy=" + heavy + " light=" + light);
                });
                Case("flip jump and material", () =>
                {
                    var flip = Find<DemonFlipPlatform>();
                    _player.TeleportTo(flip.Body.position + Vector2.right * 2f); Physics2D.SyncTransforms(); Jump();
                    Check(!flip.Turning, "side jump does not flip platform");
                    OnTop(flip.GetComponent<Collider2D>()); Jump(); Check(flip.Turning, "support jump starts flip");
                    Tick(flip, 30); Check(flip.Icy && !flip.Turning && flip.GetComponent<Collider2D>().sharedMaterial.friction < .03f, "flip completes at ice material");
                    var saved = flip.CaptureSnapshotState(); Tick(flip, 30); Check(flip.TryActivate(), "second flip can start after cooldown"); Tick(flip, 30);
                    Check(!flip.Icy && flip.GetComponent<Collider2D>().sharedMaterial.friction > 1f, "second flip restores rough material");
                    flip.RestoreSnapshotState(saved); Check(flip.Icy && Mathf.Abs(flip.Face.localEulerAngles.z - 180f) < .1f, "snapshot restores side and visual rotation");
                });
                Case("puzzle legality and winding", () =>
                {
                    var puzzle = Find<DemonSlidingPuzzle>();
                    Check(!puzzle.Solved && puzzle.Cells.OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 9)), "shuffle produces eight tiles and one blank");
                    var before = puzzle.Cells; Check(!puzzle.TryMove(new Vector2Int(1, 1)) && before.SequenceEqual(puzzle.Cells), "diagonal puzzle movement rejected");
                    var key = Find<DemonWindingKey>(x => !x.Vertical); int blank = puzzle.BlankIndex;
                    OnTop(key.GetComponent<Collider2D>()); Jump();
                    Check(puzzle.BlankIndex / 3 == blank / 3 && Mathf.Abs(puzzle.BlankIndex - blank) == 1, "jumping on horizontal key moves exactly one adjacent tile");
                    var saved = puzzle.CaptureSnapshotState(); var cells = puzzle.Cells;
                    var vertical = Find<DemonWindingKey>(x => x.Vertical); vertical.TryActivate(); puzzle.RestoreSnapshotState(saved);
                    Check(cells.SequenceEqual(puzzle.Cells), "puzzle snapshot restores all cells");
                });
                Case("puzzle solvability and gate", () =>
                {
                    var puzzle = Find<DemonSlidingPuzzle>();
                    var horizontal = Find<DemonWindingKey>(x => !x.Vertical);
                    var vertical = Find<DemonWindingKey>(x => x.Vertical);
                    var path = SolveViaKeys(puzzle.Cells, horizontal.Direction, vertical.Direction);
                    Check(path != null && path.Count > 0, "shuffled board is solvable using only the two winding keys");
                    foreach (bool isVertical in path)
                    {
                        var key = isVertical ? vertical : horizontal; Tick(key, 30);
                        Check(key.TryActivate(), "winding key solution step");
                    }
                    Check(puzzle.Solved, "puzzle completion detected");
                    var gate = puzzle.OnSolved.GetPersistentTarget(0) as CityPathMotor;
                    Check(gate != null && gate.TargetEnd, "solved puzzle opens its linked gate");
                    int[] final = puzzle.Cells; Check(!puzzle.TryMove(Vector2Int.left) && final.SequenceEqual(puzzle.Cells), "solved puzzle locks movement");
                });
                Case("piano sequence, mismatch and gate", () =>
                {
                    var melody = Find<DemonMelodyLock>(); melody.PlayNote(0); melody.PlayNote(1); melody.PlayNote(1);
                    Check(melody.Progress == 0 && !melody.Unlocked, "wrong piano sequence does not open gate");
                    melody.PlayNote(0); melody.PlayNote(1); var saved = melody.CaptureSnapshotState();
                    Tick(melody, 400); Check(melody.Progress == 0, "piano timeout resets partial melody");
                    melody.RestoreSnapshotState(saved); Check(melody.Progress == 2, "piano snapshot restores partial melody");
                    melody.PlayNote(0); melody.PlayNote(0); melody.PlayNote(1);
                    var gate = melody.OnUnlocked.GetPersistentTarget(0) as CityPathMotor;
                    Check(melody.Unlocked && gate != null && gate.TargetEnd, "configured melody opens linked piano gate");
                    var key = Find<DemonPianoKey>(); Check(key.Clip != null && key.Clip.samples > 1000 && key.Melody == melody, "piano key includes playable note and melody binding");
                });
                Case("piano physical step", () =>
                {
                    var key = Find<DemonPianoKey>(x => x.Note == 0); OnTop(key.GetComponent<Collider2D>());
                    for (int i = 0; i < 12; i++) { _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                    Physics2D.SyncTransforms(); Tick(key, 1); Check(key.Melody.Progress == 1, "standing on white key emits one note");
                    Tick(key, 20); Check(key.Melody.Progress == 1, "holding key does not repeat notes");
                });
                Case("stalactite falling", () =>
                {
                    var rock = Find<DemonStalactite>(x => x.name == "DemonStalactiteMedium");
                    _player.TeleportTo(new Vector2(rock.Body.position.x, .9f)); Physics2D.SyncTransforms(); Tick(rock, 1);
                    Check(rock.Phase == 1, "player beneath stalactite starts warning");
                    TimeManager.Instance.SetPaused(true); Tick(rock, 100); Check(rock.Phase == 1, "pause freezes stalactite warning");
                    TimeManager.Instance.SetPaused(false); Physics2D.simulationMode = SimulationMode2D.Script;
                    var saved = SnapshotSystem.Instance.Capture(); float height = rock.Body.position.y;
                    Tick(rock, 50, true); Check(rock.Body.position.y < height - .2f, "stalactite physically falls after warning");
                    Tick(rock, 200, true); Check(rock.Phase == 3 && !rock.GetComponent<Collider2D>().enabled, "stalactite stops and consumes itself on ground");
                    SnapshotSystem.Instance.Restore(saved); Check(rock.Phase == 1 && rock.GetComponent<Collider2D>().enabled && Mathf.Abs(rock.Body.position.y - height) < .05f, "world rewind restores stalactite position and warning");
                });
                Case("spring launch", () =>
                {
                    var spring = Find<DemonSpringPlatform>(); OnTop(spring.GetComponent<Collider2D>());
                    for (int i = 0; i < 12; i++) { _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                    float start = _player.transform.position.y, peak = start; bool launched = false;
                    for (int i = 0; i < 90; i++)
                    {
                        spring.SendMessage("FixedUpdate"); launched |= spring.LaunchVelocity.y > 1f;
                        _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); Physics2D.SyncTransforms(); peak = Mathf.Max(peak, _player.transform.position.y);
                    }
                    Check(launched, "spring reports launch via existing surface interface");
                    Check(peak > start + .7f, "real player is launched upward by spring");
                });
                Case("rust mass rules", () =>
                {
                    var body = Find<RuleGame.Gameplay.Scene.PushableBody>().GetComponent<Rigidbody2D>();
                    float original = body.mass;
                    RuleSystem.Instance.Request(AttributeId.Mass, RelationId.Weaken, RuleSource.Player);
                    Check(body.mass < original * .5f, "rust block responds to existing mass weakening rule");
                    RuleSystem.Instance.ClearAll();
                    Check(Mathf.Abs(body.mass - original) < .01f, "clearing mass rule restores rust block mass");
                });
                Case("carousel movement and rewind", () =>
                {
                    var motor = Find<CityPathMotor>(x => x.name == "DemonCarousel"); float y = motor.Body.position.y;
                    var saved = SnapshotSystem.Instance.Capture(); Tick(motor, 40, true);
                    Check(motor.Body.position.y > y + .4f, "carousel moves upward from placed origin");
                    SnapshotSystem.Instance.Restore(saved); Check(Mathf.Abs(motor.Body.position.y - y) < .05f, "world snapshot restores carousel position");
                    TimeManager.Instance.SetPaused(true); Tick(motor, 40); Check(Mathf.Abs(motor.Body.position.y - y) < .05f, "pause freezes carousel");
                });
                Case("test scene navigation and reset", () =>
                {
                    var controls = Find<DemonCastleLabControls>();
                    Check(controls.Ready && controls.StationCount == 12, "test scene initializes twelve test areas and reset snapshot");
                    foreach (int station in new[] { 9, 10, 11 })
                    {
                        controls.SelectStation(station);
                        Check(controls.CurrentStation == station && Vector2.Distance(_player.transform.position, DemonCastleLabControls.Stations[station]) < .01f,
                            "test scene navigation reaches area " + (station + 1));
                    }
                    Check(UnityEngine.Object.FindObjectsByType<RuleGame.Presentation.DemonCastle.DemonCastleSpriteView>().Any(x => x.name == "DemonYoyoVisual"), "test scene contains delivered yoyo visual");
                    for (int i = 1; i <= 6; i++) Check(UnityEngine.Object.FindObjectsByType<BoxCollider2D>().Any(x => x.name == "DemonPillar" + i.ToString("00")), "test scene includes pillar variant " + i);
                    var bomb = Find<DemonFirework>(); controls.MarkSnapshot(); bomb.SetOn(true); Tick(bomb, 100);
                    Check(bomb.Spent, "test scene allows firework to be consumed before reset");
                    controls.RestoreMarkedSnapshot(); Check(!bomb.Spent, "test scene F7 action restores marked mechanism state");
                    controls.ResetScene();
                    Check(!bomb.Spent && !bomb.Armed && controls.CurrentStation == 0, "test scene F5 action resets firework and returns to first area");
                    var doc = controls.GetComponent<UnityEngine.UIElements.UIDocument>();
                    Check(doc.rootVisualElement.Q<UnityEngine.UIElements.Label>("demonVitals").text.Contains("生命") &&
                        doc.rootVisualElement.Q<UnityEngine.UIElements.Label>("demonArea").text.Contains("12"), "test scene HUD displays vitals and area count");
                });
            }
            finally { TimeManager.Instance.SetPaused(false); Physics2D.simulationMode = _previousMode; }
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                CaptureViews();
                DemonCastleArtValidation.CaptureGallery(Check);
            }
            Finish();
        }
        private static void CaptureViews()
        {
            SnapshotSystem.Instance.Restore(_baseline);
            var original = Find<Camera>(); original.enabled = false;
            var go = new GameObject("DemonPreviewCamera");
            var camera = go.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 4.2f;
            camera.backgroundColor = new Color(.055f, .035f, .095f); camera.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(1280, 720, 24); var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var old = RenderTexture.active; camera.targetTexture = rt;
            Directory.CreateDirectory("Logs/DemonCastlePreview");
            var centers = new[] { new Vector2(65.5f, 2.6f), new Vector2(94.5f, 2.2f), new Vector2(40f, 3.2f) };
            var names = new[] { "PuzzleRoom", "PianoRoom", "Stalactites" };
            try
            {
                for (int i = 0; i < centers.Length; i++)
                {
                    camera.orthographicSize = i == 2 ? 4.7f : 4.2f;
                    camera.transform.position = new Vector3(centers[i].x, centers[i].y, -10f);
                    camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    File.WriteAllBytes("Logs/DemonCastlePreview/" + names[i] + ".png", image.EncodeToPNG());
                    Check(image.GetPixels().Count(x => x.r > .2f || x.g > .2f || x.b > .3f) > 5000, names[i] + " renders visible geometry with URP");
                }
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = old; rt.Release();
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(go); original.enabled = true;
            }
        }
        private static List<bool> SolveViaKeys(int[] initial, int horizontal, int vertical)
        {
            string start = string.Concat(initial.Select(x => (char)('0' + x))) + (horizontal < 0 ? "-" : "+") + (vertical < 0 ? "-" : "+");
            var queue = new Queue<string>(); queue.Enqueue(start);
            var parents = new Dictionary<string, (string Previous, bool Vertical)> { [start] = (null, false) };
            while (queue.Count > 0)
            {
                string state = queue.Dequeue();
                if (state.StartsWith("123456780", StringComparison.Ordinal))
                {
                    var path = new List<bool>();
                    while (parents[state].Previous != null) { path.Add(parents[state].Vertical); state = parents[state].Previous; }
                    path.Reverse(); return path;
                }
                int blank = state.IndexOf('0');
                for (int axis = 0; axis < 2; axis++)
                {
                    int direction = state[9 + axis] == '+' ? 1 : -1;
                    int x = blank % 3 + (axis == 0 ? direction : 0), y = blank / 3 + (axis == 1 ? direction : 0);
                    if (x < 0 || x > 2 || y < 0 || y > 2)
                    {
                        direction = -direction;
                        x = blank % 3 + (axis == 0 ? direction : 0); y = blank / 3 + (axis == 1 ? direction : 0);
                    }
                    int from = y * 3 + x; char[] cells = state.ToCharArray(); cells[blank] = cells[from]; cells[from] = '0';
                    cells[9 + axis] = direction > 0 ? '+' : '-'; string next = new string(cells);
                    if (parents.ContainsKey(next)) continue;
                    parents[next] = (state, axis == 1); queue.Enqueue(next);
                }
            }
            return null;
        }
        private static void FinishError(Exception error)
        { _failed++; Results.Add("FAIL fatal: " + error); Debug.LogException(error); Finish(); }
        private static void Finish()
        {
            SessionState.SetInt(Pending, 0); Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/DemonCastleValidation.txt", $"Passed: {_passed}\nFailed: {_failed}\n" + string.Join("\n", Results));
            Debug.Log($"[DemonCastleValidation] {_passed} passed, {_failed} failed"); EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
    }
}
