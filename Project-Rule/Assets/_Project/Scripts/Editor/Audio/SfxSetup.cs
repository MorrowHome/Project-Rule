using System;
using System.IO;
using RuleGame.Gameplay.Player;
using RuleGame.Presentation.Audio;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    public static class SfxSetup
    {
        public const string BankPath = "Assets/_Project/Data/Audio/PlayerWheel_SfxBank.asset";
        private const string AudioFolder = "Assets/_Project/Audio/SFX/Kenney/";
        private const string RuntimePath = "Assets/_Project/Prefabs/LevelRuntime.prefab";

        [MenuItem("Tools/规则游戏/音频/④ 接入玩家与轮盘音效")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
            SfxBank bank = AssetDatabase.LoadAssetAtPath<SfxBank>(BankPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<SfxBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
                var serialized = new SerializedObject(bank);
                var entries = serialized.FindProperty("_entries");
                entries.arraySize = Enum.GetValues(typeof(SfxCue)).Length;
                SetEntry(entries, SfxCue.Jump, 0.48f, 1.08f, 1.18f, "Rpg/cloth1", "Rpg/cloth2");
                SetEntry(entries, SfxCue.AirJump, 0.46f, 1.18f, 1.28f, "Rpg/cloth1", "Rpg/cloth2");
                SetEntry(entries, SfxCue.WallJump, 0.52f, 1.04f, 1.14f, "Rpg/cloth2", "Rpg/cloth3");
                SetEntry(entries, SfxCue.Land, 0.33f, 0.96f, 1.04f, "Assets/_Project/Audio/SFX/PlayerFeedback/PlayerLand.wav");
                SetEntry(entries, SfxCue.Dash, 0.6f, 0.85f, 0.94f, "Rpg/cloth3");
                SetEntry(entries, SfxCue.SwordLight, 0.55f, 0.96f, 1.04f, WorldSfxSetup.LightA, WorldSfxSetup.LightB);
                SetEntry(entries, SfxCue.SwordHeavy, 0.65f, 0.88f, 0.96f, WorldSfxSetup.HeavyA, WorldSfxSetup.HeavyB);
                SetEntry(entries, SfxCue.Hurt, 0.60f, 0.96f, 1.04f, "Assets/_Project/Audio/SFX/PlayerFeedback/PlayerHurt.wav");
                SetEntry(entries, SfxCue.WheelOpen, 0.45f, 1f, 1f, "Interface/open_001");
                SetEntry(entries, SfxCue.WheelPreview, 0.26f, 1f, 1f, "Interface/select_001");
                SetEntry(entries, SfxCue.WheelConfirm, 0.55f, 1f, 1f, "Interface/confirmation_001");
                SetEntry(entries, SfxCue.WheelCancel, 0.4f, 1f, 1f, "Interface/close_001");
                SetEntry(entries, SfxCue.WheelBack, 0.32f, 0.92f, 0.92f, "Interface/click_001");
                SetEntry(entries, SfxCue.WheelError, 0.42f, 1f, 1f, "Interface/error_001");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            GameObject root = PrefabUtility.LoadPrefabContents(RuntimePath);
            try
            {
                var audio = root.GetComponent<SfxPlayer>();
                if (audio == null) audio = root.AddComponent<SfxPlayer>();
                SetReference(audio, "_bank", bank);
                var player = root.GetComponentInChildren<PlayerController>(true);
                var playerSfx = player.GetComponent<PlayerSfxPresenter>();
                if (playerSfx == null) playerSfx = player.gameObject.AddComponent<PlayerSfxPresenter>();
                SetReference(playerSfx, "_audio", audio);
                var wheel = root.GetComponentInChildren<RuleWheelPresenter>(true);
                var wheelSfx = wheel.GetComponent<RuleWheelSfxPresenter>();
                if (wheelSfx == null) wheelSfx = wheel.gameObject.AddComponent<RuleWheelSfxPresenter>();
                SetReference(wheelSfx, "_audio", audio);
                PrefabUtility.SaveAsPrefabAsset(root, RuntimePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/PlayerWheelSfx");
            File.WriteAllText("Logs/PlayerWheelSfx/Setup.txt", "Player and rule wheel sound effects installed in LevelRuntime.\n");
            Debug.Log("[SfxSetup] 玩家动作、实际受击与轮盘音效已接入 LevelRuntime；重复安装保留音效配置。");
        }

        private static void SetReference(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEntry(SerializedProperty array, SfxCue cue, float volume, float minPitch, float maxPitch,
            params string[] clips)
        {
            SerializedProperty entry = array.GetArrayElementAtIndex((int)cue);
            entry.FindPropertyRelative("Cue").enumValueIndex = (int)cue;
            entry.FindPropertyRelative("Volume").floatValue = volume;
            entry.FindPropertyRelative("Pitch").vector2Value = new Vector2(minPitch, maxPitch);
            entry.FindPropertyRelative("MinInterval").floatValue = cue == SfxCue.WheelError ? 0.18f : 0.06f;
            entry.FindPropertyRelative("MaxVoices").intValue = 2;
            entry.FindPropertyRelative("Priority").intValue = cue == SfxCue.Hurt ? 16 : 96;
            var references = entry.FindPropertyRelative("Clips");
            references.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clips[i].StartsWith("Assets/") ? clips[i] : AudioFolder + clips[i] + ".ogg");
                if (clip == null) throw new InvalidOperationException("缺少音效：" + clips[i]);
                references.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            }
        }
    }
}
