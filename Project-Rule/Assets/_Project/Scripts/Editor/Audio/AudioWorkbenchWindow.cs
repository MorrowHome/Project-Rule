using System;
using System.Collections.Generic;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Audio
{
    public sealed class AudioWorkbenchWindow : EditorWindow
    {
        private const string Folder = "Assets/_Project/Scripts/Editor/Audio/";
        private static readonly string[] CueNames =
        {
            "地面跳跃", "空中二段跳", "蹬墙跳", "落地", "冲刺", "挥剑 · 轻击", "挥剑 · 重击", "玩家受击",
            "轮盘 · 打开", "轮盘 · 选择预览", "轮盘 · 确认", "轮盘 · 取消", "轮盘 · 返回", "轮盘 · 操作失败",
            "敌人 · 近战攻击", "敌人 · 发射弹体", "敌人 · 光束蓄能", "敌人 · 木质受击", "敌人 · 金属受击", "敌人 · 毛绒受击",
            "敌人 · 木质死亡", "敌人 · 金属死亡", "敌人 · 毛绒死亡", "机关 · 打开", "机关 · 关闭",
            "碰撞 · 石头", "碰撞 · 金属", "碰撞 · 木头", "碎裂 · 石头", "碎裂 · 木头", "机关 · 爆炸"
        };
        [SerializeField] private SfxBank _bank;
        [SerializeField] private int _selected = (int)SfxCue.SwordLight;
        [SerializeField] private float _previewMaster = .85f;
        [SerializeField] private string _filter = "全部";
        [SerializeField] private string _search = "";
        [SerializeField] private bool _music;
        [SerializeField] private string _musicArea = "Bricks";
        private SerializedObject _serialized;
        private ScrollView _list, _detail;
        private Label _status;
        private readonly System.Random _random = new System.Random();

        [MenuItem("Tools/规则游戏/音频/⑧ 音频工作台（试听与配置）")]
        public static void Open()
        {
            var window = GetWindow<AudioWorkbenchWindow>("音频工作台");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        public static void Open(SfxBank bank)
        {
            Open();
            var window = GetWindow<AudioWorkbenchWindow>();
            window._bank = bank;
            window._music = false;
            window.CreateGUI();
        }

        private void OnEnable() => Undo.undoRedoPerformed += Refresh;
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            AudioAudition.Stop();
        }

        public void CreateGUI()
        {
            rootVisualElement.Unbind();
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("audio-workbench");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Folder + "AudioWorkbenchWindow.uss");
            if (sheet != null && !rootVisualElement.styleSheets.Contains(sheet)) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.Add(new Label("音频工作台") { name = "heading" });
            rootVisualElement.Q<Label>("heading").AddToClassList("workbench-heading");
            var toolbar = Row(rootVisualElement);
            toolbar.Add(new Button(() => { _music = false; Refresh(); }) { text = "音效" });
            toolbar.Add(new Button(() => { _music = true; Refresh(); }) { text = "区域音乐" });
            toolbar.Add(new Button(() => { AudioAudition.Stop(); Status("已停止试听。"); }) { text = "停止试听" });
            toolbar.Add(new Button(Save) { text = "保存配置" });
            var bank = new ObjectField("音效配置") { objectType = typeof(SfxBank), allowSceneObjects = false };
            if (_bank == null) _bank = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
            bank.SetValueWithoutNotify(_bank);
            bank.RegisterValueChangedCallback(evt => { AudioAudition.Stop(); _bank = evt.newValue as SfxBank; Refresh(); });
            rootVisualElement.Add(bank);
            var filters = Row(rootVisualElement);
            var category = new DropdownField("分类", new List<string> { "全部", "玩家", "轮盘", "敌人", "机关" }, 0);
            category.SetValueWithoutNotify(_filter);
            category.RegisterValueChangedCallback(evt => { _filter = evt.newValue; BuildList(); });
            filters.Add(category);
            var search = new ToolbarSearchField { value = _search };
            search.AddToClassList("workbench-grow");
            search.RegisterValueChangedCallback(evt => { _search = evt.newValue; BuildList(); });
            filters.Add(search);
            var body = new VisualElement(); body.AddToClassList("workbench-body"); rootVisualElement.Add(body);
            _list = new ScrollView(); _list.AddToClassList("workbench-list"); body.Add(_list);
            _detail = new ScrollView(); _detail.AddToClassList("workbench-detail"); body.Add(_detail);
            _status = new Label("选中事件后可直接试听，不需要进入 Play Mode。参数修改支持 Ctrl/Cmd+Z；点击保存配置写入资产。");
            _status.AddToClassList("workbench-status"); rootVisualElement.Add(_status);
            Refresh();
        }

        private void Refresh()
        {
            if (_list == null) return;
            BuildList();
            BuildDetail();
        }

        internal static string Name(SfxCue cue) => (int)cue >= 0 && (int)cue < CueNames.Length ? CueNames[(int)cue] : cue.ToString();
        private static string Category(SfxCue cue) => (int)cue <= 7 ? "玩家" : (int)cue <= 13 ? "轮盘" : (int)cue <= 22 ? "敌人" : "机关";
        private void BuildList()
        {
            _list.Clear();
            if (_music)
            {
                foreach (string area in new[] { "Bricks", "Clockwork", "Fluffy" })
                {
                    string name = area == "Bricks" ? "积木区" : area == "Clockwork" ? "时钟区" : "毛绒区";
                    _list.Add(new Button(() => Music(area, name)) { text = name });
                }
                return;
            }
            foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
            {
                if (_filter != "全部" && Category(cue) != _filter) continue;
                if (!string.IsNullOrWhiteSpace(_search) && !(Name(cue) + " " + cue).Contains(_search, StringComparison.OrdinalIgnoreCase)) continue;
                var button = new Button(() => { _selected = (int)cue; Refresh(); }) { text = Name(cue), tooltip = cue.ToString() };
                if (_selected == (int)cue) button.AddToClassList("workbench-selected");
                _list.Add(button);
            }
        }

        private void BuildDetail()
        {
            _detail.Unbind(); _detail.Clear();
            if (_music) { Music(_musicArea, _musicArea == "Bricks" ? "积木区" : _musicArea == "Clockwork" ? "时钟区" : "毛绒区"); return; }
            if (_bank == null) { Note("请指定 SfxBank 配置资产。"); return; }
            _serialized = new SerializedObject(_bank);
            SfxCue cue = (SfxCue)_selected;
            var entries = _serialized.FindProperty("_entries");
            SerializedProperty item = null;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Cue").intValue == _selected)
                { item = entries.GetArrayElementAtIndex(i); break; }
            Title(Name(cue));
            Note(cue + " · " + Category(cue) + "\n修改当前资产会作用于所有引用它的玩家、敌人和机关；换素材不需要改代码。");
            if (item == null)
            {
                Note("当前音效配置没有这个事件。可先运行音频接线菜单补齐默认事件。"); return;
            }
            Field(item, "Volume", "事件音量（0–1）");
            Field(item, "Pitch", "随机音高范围");
            var master = new Slider("试听主音量", 0f, 1f) { value = _previewMaster };
            master.tooltip = "仅控制本窗口试听；默认与 LevelRuntime 的 SfxPlayer.Volume 0.85 一致，不会改游戏配置。";
            master.RegisterValueChangedCallback(evt => _previewMaster = evt.newValue);
            _detail.Add(master);
            var controls = Row(_detail);
            controls.Add(new Button(() => Preview(cue, -1)) { text = "按配置随机试听" });
            controls.Add(new Button(() => { Selection.activeObject = _bank; EditorGUIUtility.PingObject(_bank); }) { text = "定位配置资产" });
            Note("参数试听包含事件音量、随机音高与试听主音量，不含距离衰减或游戏内并发。每次点击会停止上一段试听。");
            var advanced = new Foldout { text = "播放限制（高级）", value = false };
            foreach (var field in new[] { ("MinInterval", "最短间隔（秒）"), ("MaxVoices", "同类最多并发"), ("Priority", "优先级（越小越优先）") })
                advanced.Add(new PropertyField(item.FindPropertyRelative(field.Item1), field.Item2));
            _detail.Add(advanced);
            var clips = new PropertyField(item.FindPropertyRelative("Clips"), "素材变体（拖入 AudioClip，可增删）");
            _detail.Add(clips);
            var variants = new VisualElement(); _detail.Add(variants);
            void Variants()
            {
                variants.Clear();
                SfxBank.Entry entry = _bank.Find(cue);
                for (int i = 0; i < (entry?.Clips?.Length ?? 0); i++)
                {
                    int index = i;
                    AudioClip clip = entry.Clips[i];
                    var row = new VisualElement(); row.AddToClassList("workbench-variant"); variants.Add(row);
                    row.Add(new Label("变体 " + (i + 1) + " · " + (clip != null ? clip.name + " · " + clip.length.ToString("0.000") + " 秒" : "未指定素材")));
                    var buttons = Row(row);
                    buttons.Add(new Button(() => Preview(cue, index)) { text = "参数试听" });
                    buttons.Add(new Button(() => Try(() => AudioAudition.PlayRaw(clip), "原声试听 · 不含音量与音高")) { text = "原声试听" });
                    buttons.Add(new Button(() => { if (clip != null) { Selection.activeObject = clip; EditorGUIUtility.PingObject(clip); } }) { text = "定位素材" });
                }
            }
            clips.RegisterValueChangeCallback(_ => Variants());
            _detail.Bind(_serialized);
            Variants();
        }

        private void Preview(SfxCue cue, int variant)
        {
            SfxBank.Entry entry = _bank?.Find(cue);
            if (entry?.Clips == null || entry.Clips.Length == 0) { Status("请先为这个事件添加素材。"); return; }
            int index = variant < 0 ? _random.Next(entry.Clips.Length) : variant;
            if (index >= entry.Clips.Length) { Status("素材列表已变化，请重新选择。"); return; }
            float min = Mathf.Clamp(entry.Pitch.x, .1f, 3f), max = Mathf.Clamp(Mathf.Max(entry.Pitch.x, entry.Pitch.y), .1f, 3f);
            float pitch = Mathf.Lerp(min, max, (float)_random.NextDouble());
            Try(() => AudioAudition.PlayProcessed(entry.Clips[index], entry.Volume * _previewMaster, pitch),
                Name(cue) + " · 变体 " + (index + 1) + " · 音高 " + pitch.ToString("0.00") + " · 音量 " + (entry.Volume * _previewMaster).ToString("0.00"));
        }

        private void Music(string area, string name)
        {
            _musicArea = area;
            _detail.Unbind(); _detail.Clear();
            MusicProfile profile = AssetDatabase.LoadAssetAtPath<MusicProfile>(MusicSetup.DataFolder + "/" + area + "_MusicProfile.asset");
            Title(name + " · 背景音乐");
            if (profile == null) { Note("缺少区域音乐配置，请运行音频接线菜单①。"); return; }
            var serialized = new SerializedObject(profile);
            foreach (var field in new[] { ("_intro", "首次进入的前奏"), ("_loop", "循环段"), ("_volume", "游戏内音量"), ("_crossfadeSeconds", "跨区淡化（秒）") })
                _detail.Add(new PropertyField(serialized.FindProperty(field.Item1), field.Item2));
            _detail.Bind(serialized);
            var buttons = Row(_detail);
            buttons.Add(new Button(() => Try(() => AudioAudition.PlayRaw(profile.Intro), "正在试听前奏原声")) { text = "前奏原声" });
            buttons.Add(new Button(() => Try(() => AudioAudition.PlayRaw(profile.Loop, 0, true), "循环原声试听；点击停止结束")) { text = "循环原声" });
            buttons.Add(new Button(() => Try(() => AudioAudition.PlayRaw(profile.Loop, Mathf.Max(0, profile.Loop.samples - profile.Loop.frequency * 3), true), "从循环末尾 3 秒开始，试听尾→头接点")) { text = "循环接点原声" });
            Note("音乐原声试听不应用游戏内音量、跨区淡化或前奏调度。完整效果请打开已有音乐试听场景，进入 Play Mode 后在三个区域间移动。场景中暂停也可检查音乐压低。");
            var actions = Row(_detail);
            actions.Add(new Button(() => { Selection.activeObject = profile; EditorGUIUtility.PingObject(profile); }) { text = "定位区域配置" });
            actions.Add(new Button(() => { AudioAudition.Stop(); if (!EditorApplication.isPlaying && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(MusicSetup.LabPath); }) { text = "打开音乐试听场景" });
        }

        private void Save()
        {
            if (EditorApplication.isPlaying) { Status("请退出 Play Mode 后保存配置。"); return; }
            if (_bank != null) AssetDatabase.SaveAssetIfDirty(_bank);
            foreach (string area in new[] { "Bricks", "Clockwork", "Fluffy" })
            {
                var profile = AssetDatabase.LoadAssetAtPath<MusicProfile>(MusicSetup.DataFolder + "/" + area + "_MusicProfile.asset");
                if (profile != null) AssetDatabase.SaveAssetIfDirty(profile);
            }
            Status("音效和三个区域音乐配置已保存。可提交资产改动到 Git。");
        }
        private void Try(Action action, string message)
        {
            try { action(); Status(message); }
            catch (Exception error) { Status(error.GetBaseException().Message); }
        }
        private void Status(string message) { if (_status != null) _status.text = message; }
        private void Title(string text) { var label = new Label(text); label.AddToClassList("workbench-title"); _detail.Add(label); }
        private void Note(string text) { var label = new Label(text); label.AddToClassList("workbench-note"); _detail.Add(label); }
        private void Field(SerializedProperty item, string property, string label) => _detail.Add(new PropertyField(item.FindPropertyRelative(property), label));
        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement(); row.AddToClassList("workbench-toolbar"); parent.Add(row); return row;
        }
    }

    [CustomEditor(typeof(SfxBank))]
    public sealed class SfxBankEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new Button(() => AudioWorkbenchWindow.Open((SfxBank)target)) { text = "打开音频工作台（中文配置与试听）" });
            root.Add(new HelpBox("优先使用音频工作台逐项调音。修改支持撤销；SfxPlayer 主音量和听距在 LevelRuntime 预制体上。", HelpBoxMessageType.Info));
            root.Add(new PropertyField(serializedObject.FindProperty("_entries"), "全部原始配置"));
            return root;
        }
    }
}
