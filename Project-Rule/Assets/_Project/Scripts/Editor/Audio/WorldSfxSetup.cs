using System;
using System.IO;
using RuleGame.Gameplay.Enemy;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    public static class WorldSfxSetup
    {
        private const string SoundFolder = "Assets/_Project/Audio/SFX/";
        private const string PrefabFolder = "Assets/_Project/Prefabs";
        public const string LightA = SoundFolder + "Swishes/SwordLight_A.wav";
        public const string LightB = SoundFolder + "Swishes/SwordLight_B.wav";
        public const string HeavyA = SoundFolder + "Swishes/SwordHeavy_A.wav";
        public const string HeavyB = SoundFolder + "Swishes/SwordHeavy_B.wav";

        [MenuItem("Tools/规则游戏/音频/⑥ 更换挥剑声并接入敌人与机关")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SoundFolder.TrimEnd('/') }))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
            SfxBank bank = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
            if (bank == null) throw new InvalidOperationException("请先运行音频菜单④。");
            var serialized = new SerializedObject(bank);
            var entries = serialized.FindProperty("_entries");
            Set(entries, SfxCue.SwordLight, .55f, 64, .96f, 1.04f, true, LightA, LightB);
            Set(entries, SfxCue.SwordHeavy, .65f, 64, .88f, .96f, true, HeavyA, HeavyB);
            Set(entries, SfxCue.EnemyMelee, .42f, 160, .9f, 1.05f, false, LightA, LightB);
            Set(entries, SfxCue.EnemyShot, .4f, 160, .95f, 1.05f, false, SoundFolder + "Kenney/Interface/click_001.ogg");
            Set(entries, SfxCue.EnemyBeam, .42f, 144, .94f, 1.04f, false, SoundFolder + "Rpg80/spell_01.ogg");
            Set(entries, SfxCue.EnemyHitWood, .52f, 112, .95f, 1.05f, false, SoundFolder + "Kenney/Impact/impactWood_medium_000.ogg");
            Set(entries, SfxCue.EnemyHitMetal, .48f, 112, .95f, 1.05f, false, SoundFolder + "Kenney/Impact/impactMetal_medium_000.ogg");
            Set(entries, SfxCue.EnemyHitSoft, .55f, 112, .95f, 1.05f, false, SoundFolder + "Kenney/Impact/impactSoft_medium_000.ogg");
            Set(entries, SfxCue.EnemyDeathWood, .63f, 40, .88f, 1f, false, SoundFolder + "Rpg80/wood_01.ogg", SoundFolder + "Rpg80/wood_02.ogg");
            Set(entries, SfxCue.EnemyDeathMetal, .58f, 40, .85f, 1f, false, SoundFolder + "Kenney/Impact/impactMetal_heavy_000.ogg", SoundFolder + "Kenney/Impact/impactMetal_heavy_001.ogg");
            Set(entries, SfxCue.EnemyDeathSoft, .64f, 40, .9f, 1f, false, SoundFolder + "Kenney/Impact/impactSoft_heavy_000.ogg", SoundFolder + "Kenney/Impact/impactSoft_heavy_001.ogg");
            Set(entries, SfxCue.SwitchOn, .48f, 112, 1f, 1.04f, false, SoundFolder + "Rpg80/lock_01.ogg");
            Set(entries, SfxCue.SwitchOff, .4f, 112, .85f, .9f, false, SoundFolder + "Rpg80/lock_01.ogg");
            Set(entries, SfxCue.ImpactStone, .52f, 144, .92f, 1.05f, false, SoundFolder + "Rpg80/stones_01.ogg", SoundFolder + "Rpg80/stones_02.ogg");
            Set(entries, SfxCue.ImpactMetal, .48f, 144, .92f, 1.05f, false, SoundFolder + "Kenney/Impact/impactMetal_medium_000.ogg");
            Set(entries, SfxCue.ImpactWood, .48f, 144, .92f, 1.05f, false, SoundFolder + "Kenney/Impact/impactWood_heavy_000.ogg");
            Set(entries, SfxCue.BreakStone, .7f, 32, .8f, .9f, false, SoundFolder + "Rpg80/stones_02.ogg");
            Set(entries, SfxCue.BreakWood, .66f, 48, .8f, .95f, false, SoundFolder + "Rpg80/wood_02.ogg");
            Set(entries, SfxCue.Explosion, .65f, 32, .8f, .92f, false, SoundFolder + "Rpg80/spell_fire_02.ogg");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bank);

            int saved = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    foreach (EnemyActor actor in root.GetComponentsInChildren<EnemyActor>(true))
                        if (actor.GetComponent<EnemySfxPresenter>() == null)
                        { actor.gameObject.AddComponent<EnemySfxPresenter>(); changed = true; }
                    foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (component != null && MechanismSfxPresenter.Supports(component)
                            && component.GetComponent<MechanismSfxPresenter>() == null)
                        { component.gameObject.AddComponent<MechanismSfxPresenter>(); changed = true; }
                    if (path == PrefabFolder + "/LevelRuntime.prefab" || path == PrefabFolder + "/Enemies/EnemyLabPlayer.prefab")
                    {
                        SfxPlayer audio = root.GetComponent<SfxPlayer>();
                        if (audio == null)
                        {
                            audio = root.AddComponent<SfxPlayer>();
                            var sound = new SerializedObject(audio);
                            sound.FindProperty("_bank").objectReferenceValue = bank;
                            sound.ApplyModifiedPropertiesWithoutUndo(); changed = true;
                        }
                        if (root.GetComponent<WorldSfxBinder>() == null) { root.AddComponent<WorldSfxBinder>(); changed = true; }
                    }
                    if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); saved++; }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/WorldSfx");
            File.WriteAllText("Logs/WorldSfx/Setup.txt", "Updated sound bank and " + saved + " prefabs.\n");
            Debug.Log("[WorldSfxSetup] 新挥剑素材与敌人/机关已接线；修改预制体 " + saved + " 份。");
        }

        private static void Set(SerializedProperty entries, SfxCue cue, float volume, int priority, float pitchMin,
            float pitchMax, bool replace, params string[] clips)
        {
            SerializedProperty item = null;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Cue").intValue == (int)cue)
                { item = entries.GetArrayElementAtIndex(i); break; }
            if (item != null && !replace) return;
            if (item == null) { entries.arraySize++; item = entries.GetArrayElementAtIndex(entries.arraySize - 1); }
            item.FindPropertyRelative("Cue").intValue = (int)cue;
            item.FindPropertyRelative("Volume").floatValue = volume;
            item.FindPropertyRelative("Pitch").vector2Value = new Vector2(pitchMin, pitchMax);
            item.FindPropertyRelative("Priority").intValue = priority;
            item.FindPropertyRelative("MinInterval").floatValue = .12f;
            item.FindPropertyRelative("MaxVoices").intValue = 4;
            var references = item.FindPropertyRelative("Clips");
            references.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clips[i]);
                if (clip == null) throw new InvalidOperationException("缺少音频：" + clips[i]);
                references.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            }
        }
    }
}
