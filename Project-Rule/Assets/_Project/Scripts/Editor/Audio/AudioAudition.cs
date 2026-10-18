using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    /// <summary>仅编辑器使用的短音效试听，不创建场景物体或修改导入设置。</summary>
    [InitializeOnLoad]
    internal static class AudioAudition
    {
        private static readonly Type AudioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        private static readonly MethodInfo PlayMethod = AudioUtil?.GetMethod("PlayPreviewClip", new[] { typeof(AudioClip), typeof(int), typeof(bool) });
        private static readonly MethodInfo StopMethod = AudioUtil?.GetMethod("StopAllPreviewClips");
        private static readonly MethodInfo PauseMethod = AudioUtil?.GetMethod("PausePreviewClip");
        private static readonly MethodInfo ResumeMethod = AudioUtil?.GetMethod("ResumePreviewClip");
        private static AudioClip _temporary;

        static AudioAudition()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += _ => Stop();
            EditorApplication.quitting += Stop;
        }

        public static void Stop()
        {
            StopMethod?.Invoke(null, null);
            if (_temporary != null) UnityEngine.Object.DestroyImmediate(_temporary);
            _temporary = null;
        }

        public static void PlayRaw(AudioClip clip, int startSample = 0, bool loop = false)
        {
            Stop();
            if (clip == null) throw new InvalidOperationException("请先指定声音素材。");
            if (PlayMethod == null) throw new InvalidOperationException("当前 Unity 没有可用的编辑器试听接口，请在素材 Inspector 中试听。");
            PlayMethod.Invoke(null, new object[] { clip, startSample, loop });
        }

        public static void PlayProcessed(AudioClip clip, float volume, float pitch)
        {
            Stop();
            if (clip == null) throw new InvalidOperationException("这个变体没有素材，请先拖入 AudioClip。");
            if (clip.length > 15f) throw new InvalidOperationException("参数试听用于 15 秒内的短音效。长音乐请使用区域音乐页。");
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
                throw new InvalidOperationException("参数试听需要 Decompress On Load；可先用原声试听，或在素材 Inspector 调整短音效的导入设置。");
            if (clip.samples <= 0 || clip.channels <= 0) throw new InvalidOperationException("素材没有可读的音频样本。");
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            var source = new float[clip.samples * clip.channels];
            if (!clip.GetData(source, 0)) throw new InvalidOperationException("无法读取音频样本，请检查素材导入状态。");
            pitch = Mathf.Clamp(pitch, .1f, 3f);
            int frames = Mathf.Max(1, Mathf.CeilToInt(clip.samples / pitch));
            if ((long)frames * clip.channels > 16000000)
                throw new InvalidOperationException("这份素材在当前音高下过长，请提高试听音高或使用原声试听。");
            var output = new float[frames * clip.channels];
            for (int frame = 0; frame < frames; frame++)
            {
                float offset = Mathf.Min(frame * pitch, clip.samples - 1);
                int left = (int)offset, right = Mathf.Min(left + 1, clip.samples - 1);
                for (int channel = 0; channel < clip.channels; channel++)
                    output[frame * clip.channels + channel] = Mathf.Lerp(source[left * clip.channels + channel],
                        source[right * clip.channels + channel], offset - left) * Mathf.Clamp01(volume);
            }
            _temporary = AudioClip.Create("AudioWorkbench_Preview", frames, clip.channels, clip.frequency, false);
            _temporary.hideFlags = HideFlags.HideAndDontSave;
            if (PlayMethod == null || PauseMethod == null || ResumeMethod == null)
            {
                Stop();
                throw new InvalidOperationException("当前 Unity 不支持编辑器参数试听。");
            }
            try
            {
                // Unity 6000.6 的预览入口会重新初始化动态 Clip 的 PCM，清掉此前的 SetData。
                // 先建立预览并暂停，再写样本，续播不会再次初始化音频缓冲区。
                PlayMethod.Invoke(null, new object[] { _temporary, 0, false });
                PauseMethod.Invoke(null, null);
                if (!_temporary.SetData(output, 0)) throw new InvalidOperationException("参数试听样本写入失败。");
                ResumeMethod.Invoke(null, null);
            }
            catch
            {
                Stop();
                throw;
            }
        }
    }
}
