using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using RuleGame.Core.Rules;
using RuleGame.UI.RuleWheel;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Gameplay.Scene.DemonCastle;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Development.DemonCastle
{
    /// <summary>魔王城独立测试场的导航、说明与内存重置，不改变正式关卡输入或存档。</summary>
    public sealed class DemonCastleLabControls : MonoBehaviour
    {
        public PlayerController Player;
        public CityInteractor Interactor;
        public PanelSettings PanelSettings;
        public FontAsset ChineseFont;
        public static readonly Vector2[] Stations =
        {
            new Vector2(-3f, 1f), new Vector2(12f, 1f), new Vector2(24f, 1f), new Vector2(35f, 1f),
            new Vector2(49f, 1f), new Vector2(62f, 1f), new Vector2(77f, 1f), new Vector2(91f, 1f),
            new Vector2(101f, 1f), new Vector2(115f, 1f), new Vector2(142f, 1f), new Vector2(161f, 1f)
        };
        public static readonly string[] StationNames =
        { "烟花箱与按钮", "跳跃弹簧", "规则铁块", "落石通道", "双面平台", "发条拼图房", "升降木马", "钢琴门锁", "往返滑板", "地形试跳区", "立柱与残缺组件", "溜溜球美术展示" };
        private static readonly string[] Hints =
        {
            "踩左侧按钮点火，1.2 秒后爆炸。站远一点观察，再用 F5 重置。",
            "踩上圆盘自动压缩、回弹。向右可落在高台。",
            "正常状态较难推动。打开规则轮盘，试用质量削弱或摩擦削弱后再推。",
            "走入钟乳石正下方，预警后会落下并造成伤害。F5 可重新悬挂落石。",
            "从上表面起跳翻转 180°。红色粗糙面，蓝色冰面；比较两面的滑动距离。",
            "在 H / V 发条上起跳，空格横 / 纵移动一格；到边界反向。还原 1—8 后开门。",
            "站上木马平台，观察上下往返和承载。起跳后可离开平台。",
            "踩出 白、黑、白、白、黑 开门。每次离开琴键后再踩；5 秒无新音会清空进度。",
            "站上滑板会随行，两端自动返回。可跳离，也可在运动中标记 / 恢复快照。",
            "短、中、长平台和残缺地块可站立、跳跃。方块平台仍为占位美术。",
            "六种立柱使用交付美术并带实体碰撞，可比较各段尺寸和支撑高度。",
            "溜溜球仅展示已交付的双帧循环，没有碰撞、伤害或额外机关行为。"
        };
        private RuleWheelPresenter _wheel;
        private EntityStatus _status;
        private VisualElement _guide, _bottom;
        private Label _area, _hint, _vitals, _state, _snapshot;
        private DropdownField _chooser;
        private Snapshot _initial, _saved;
        private DemonSlidingPuzzle _puzzle;
        private DemonMelodyLock _melody;
        private int _station;
        private float _nextRefresh;
        public bool Ready => _initial != null;
        public int CurrentStation => _station;
        public int StationCount => Stations.Length;

        private IEnumerator Start()
        {
            _wheel = FindAnyObjectByType<RuleWheelPresenter>();
            _status = Player != null ? Player.GetComponent<EntityStatus>() : null;
            _puzzle = FindAnyObjectByType<DemonSlidingPuzzle>(); _melody = FindAnyObjectByType<DemonMelodyLock>();
            BuildHud();
            Player?.SetRespawn("demon-castle-lab", Stations[0]);
            yield return null; // 等主角与所有机关 Start 完成再保存可重置的初始世界。
            _initial = SnapshotSystem.Instance?.Capture();
            RefreshHud();
        }
        private void BuildHud()
        {
            var document = GetComponent<UIDocument>() ?? gameObject.AddComponent<UIDocument>();
            document.panelSettings = PanelSettings; document.sortingOrder = 10;
            var root = document.rootVisualElement; root.Clear(); root.pickingMode = PickingMode.Ignore;
            if (ChineseFont != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(ChineseFont);
            _guide = new VisualElement { name = "demonGuide", pickingMode = PickingMode.Ignore };
            _guide.style.position = Position.Absolute; _guide.style.left = 20f; _guide.style.top = 18f;
            _guide.style.width = 490f; _guide.style.maxWidth = new Length(52f, LengthUnit.Percent);
            _guide.style.backgroundColor = new Color(.07f, .04f, .12f, .9f);
            _guide.style.paddingLeft = 14f; _guide.style.paddingRight = 14f; _guide.style.paddingTop = 10f; _guide.style.paddingBottom = 12f;
            _guide.style.color = new Color(.95f, .9f, 1f); root.Add(_guide);
            _area = new Label { name = "demonArea" }; _area.style.fontSize = 25f; _area.style.unityFontStyleAndWeight = FontStyle.Bold; _guide.Add(_area);
            _chooser = new DropdownField("跳转试验区", new List<string>(StationNames), 0);
            _chooser.style.marginTop = 8f; _chooser.style.fontSize = 18f;
            var choiceText = _chooser.Q<UnityEngine.UIElements.TextElement>(className: "unity-base-popup-field__text");
            if (choiceText != null) choiceText.style.color = new Color(.12f, .1f, .18f);
            _chooser.RegisterValueChangedCallback(_ => SelectStation(_chooser.index)); _guide.Add(_chooser);
            _hint = new Label { name = "demonHint" }; _hint.style.fontSize = 21f; _hint.style.whiteSpace = WhiteSpace.Normal;
            _hint.style.marginTop = 8f; _guide.Add(_hint);
            _state = new Label { name = "demonState" }; _state.style.fontSize = 18f; _state.style.whiteSpace = WhiteSpace.Normal; _state.style.marginTop = 8f; _guide.Add(_state);
            _vitals = new Label { name = "demonVitals" }; _vitals.style.position = Position.Absolute; _vitals.style.right = 24f; _vitals.style.top = 22f;
            _vitals.style.fontSize = 23f; _vitals.style.color = new Color(.95f, .9f, 1f); root.Add(_vitals);
            _bottom = new VisualElement { pickingMode = PickingMode.Ignore }; _bottom.style.position = Position.Absolute;
            _bottom.style.left = 24f; _bottom.style.bottom = 18f; _bottom.style.right = 24f; _bottom.style.color = new Color(.9f, .85f, .98f); root.Add(_bottom);
            _snapshot = new Label { name = "demonSnapshot" }; _snapshot.style.fontSize = 18f; _bottom.Add(_snapshot);
            var shortcuts = new Label("F1 下一处 / F2 上一处   ·   F5 全场重置   ·   F6 标记 / F7 恢复   ·   F9 补满能量   ·   E 交互");
            shortcuts.style.fontSize = 18f; shortcuts.style.whiteSpace = WhiteSpace.Normal; _bottom.Add(shortcuts);
        }
        private void Update()
        {
            bool selecting = _wheel != null && _wheel.IsOpen;
            if (_guide != null) _guide.style.display = selecting ? DisplayStyle.None : DisplayStyle.Flex;
            if (_bottom != null) _bottom.style.display = selecting ? DisplayStyle.None : DisplayStyle.Flex;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .1f;
                if (Player != null) _station = NearestStation(Player.transform.position.x);
                RefreshHud();
            }
            var keys = Keyboard.current;
            if (keys == null || CityMechanism.Paused || selecting) return;
            if (keys.f1Key.wasPressedThisFrame) SelectStation((_station + 1) % Stations.Length);
            if (keys.f2Key.wasPressedThisFrame) SelectStation((_station + Stations.Length - 1) % Stations.Length);
            if (keys.f5Key.wasPressedThisFrame) ResetScene();
            if (keys.f6Key.wasPressedThisFrame) MarkSnapshot();
            if (keys.f7Key.wasPressedThisFrame) RestoreMarkedSnapshot();
            if (keys.f9Key.wasPressedThisFrame && _status != null) _status.RestoreEnergy(_status.EnergyMax);
            if (keys.eKey.wasPressedThisFrame && Interactor != null) Interactor.TryInteract();
        }
        private static int NearestStation(float x)
        {
            int nearest = 0; float distance = float.PositiveInfinity;
            for (int i = 0; i < Stations.Length; i++)
            { float next = Mathf.Abs(x - Stations[i].x); if (next < distance) { distance = next; nearest = i; } }
            return nearest;
        }
        private void RefreshHud()
        {
            if (_area == null) return;
            _area.text = "魔王城测试  " + (_station + 1).ToString("00") + " / " + Stations.Length + "  " + StationNames[_station];
            _chooser.SetValueWithoutNotify(StationNames[_station]); _hint.text = Hints[_station];
            _vitals.text = _status != null ? "生命 " + Mathf.CeilToInt(_status.Health) + " / " + Mathf.CeilToInt(_status.HealthMax) +
                "   能量 " + Mathf.CeilToInt(_status.Energy) + " / " + Mathf.CeilToInt(_status.EnergyMax) : "";
            _state.text = _station == 5 ? "拼图：" + (_puzzle != null && _puzzle.Solved ? "已还原，门已开启" : "未还原") :
                _station == 7 ? "曲调进度：" + (_melody != null ? (_melody.Unlocked ? "已开门" : _melody.Progress + " / " + _melody.Sequence.Length) : "未配置") : "";
            _snapshot.text = !Ready ? "正在初始化测试场……" : _saved == null ? "F6 可标记世界快照；F5 可恢复初始机关状态。" : "已标记快照 · F7 恢复标记 · F5 恢复初始状态";
        }
        public void SelectStation(int index)
        {
            if (Player == null || CityMechanism.Paused) return;
            _station = Mathf.Clamp(index, 0, Stations.Length - 1);
            Player.TeleportTo(Stations[_station]); Player.SetRespawn("demon-castle-lab-" + _station, Stations[_station]);
            RefreshHud();
        }
        public void MarkSnapshot() { _saved = SnapshotSystem.Instance?.Capture(); RefreshHud(); }
        public void RestoreMarkedSnapshot()
        {
            if (_saved == null) return;
            SnapshotSystem.Instance?.Restore(_saved);
            if (Player != null) _station = NearestStation(Player.transform.position.x);
            RefreshHud();
        }
        public void ResetScene()
        {
            if (_initial == null) return;
            RuleSystem.Instance?.ClearAll(); SnapshotSystem.Instance?.Restore(_initial);
            _saved = null; _station = 0;
            Player?.TeleportTo(Stations[0]); Player?.SetRespawn("demon-castle-lab", Stations[0]);
            Player?.GetComponent<EntityDamageable>()?.Revive();
            _status?.RestoreEnergy(_status.EnergyMax); RefreshHud();
        }
    }
}
