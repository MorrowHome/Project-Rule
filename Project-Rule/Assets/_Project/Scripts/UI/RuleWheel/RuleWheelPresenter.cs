using System;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Rules;
using RuleGame.Infrastructure.Interaction;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RuleGame.UI.RuleWheel
{
    /// <summary>
    /// 轮盘组合根。按住 Shift / 右扳机进入，鼠标或右摇杆选择，攻击键逐级确认。
    /// 输入与镜头以令牌接管；慢时间交给 P1 的 <c>TimeManager</c>（以 <c>TimeSource.UI</c> 来源合成），
    /// 关闭 / 禁用 / 失焦统一释放。
    /// </summary>
    [DefaultExecutionOrder(-80), RequireComponent(typeof(UIDocument))]
    public sealed class RuleWheelPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerController _player;
        [SerializeField] private RuleCatalog _catalog;
        [SerializeField] private MonoBehaviour _unlocks;
        [SerializeField] private MonoBehaviour _cameraFocus;
        [SerializeField] private Camera _worldCamera;
        [SerializeField, Range(0.01f, 1f)] private float _selectionScale = 0.1f;
        [SerializeField] private Vector2 _playerOffset = new Vector2(70f, 0f);
        private static readonly AttributeId[] Attributes = (AttributeId[])Enum.GetValues(typeof(AttributeId));
        private static readonly RelationId[] Relations = (RelationId[])Enum.GetValues(typeof(RelationId));
        private static readonly string[] AttributeWords = { "重力", "时间", "摩擦", "质量", "伤害/生命", "因果" };
        private static readonly string[] RelationWords = { "反转", "失效", "恒定", "增强", "削弱" };
        private VisualElement _document, _root;
        private RuleFan _fan;
        private Label _token, _feedback;
        private RuleSelectionSession _session;
        private ITimeManager _time;
        private bool _timeScaleApplied;
        private IDisposable _inputLease, _focusLease;
        private bool _waitForRelease;
        private float _nextAvailability;
        public bool IsOpen => Stage != RuleSelectionStage.Closed;
        public RuleSelectionStage Stage => _session?.Stage ?? RuleSelectionStage.Closed;
        public RuleFan Fan => _fan;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>().rootVisualElement;
            _root = _document.Q("wheelRoot");
            _fan = _document.Q<RuleFan>("ruleFan");
            _token = _document.Q<Label>("selectedToken");
            _feedback = _document.Q<Label>("feedback");
            _root?.AddToClassList("hidden");
        }

        private void Start()
        {
            EntityStatus status = _player != null ? _player.GetComponent<EntityStatus>() : null;
            _time = TimeManager.Instance;
            if (_root == null || _fan == null || _token == null || _feedback == null || status == null
                || _catalog == null || RuleSystem.Instance == null || !(_unlocks is IRuleUnlocks)
                || _time == null || !(_cameraFocus is IRuleSelectionFocus) || _worldCamera == null)
            {
                Debug.LogError("[RuleWheel] 请配置布局、P3 玩家、目录、解锁查询与镜头接口，"
                    + "并确保 P1 的时间管理器（RuleSystemDriver.Awake 里初始化）已就绪。", this);
                enabled = false;
                return;
            }
            _session = new RuleSelectionSession(RuleSystem.Instance, _catalog, status, (IRuleUnlocks)_unlocks);
        }

        public bool TryOpen()
        {
            if (!isActiveAndEnabled || _session == null || IsOpen || _player == null || !_player.isActiveAndEnabled
                || _player.GetComponent<EntityStatus>().IsPaused) return false;
            try
            {
                // 慢时间走 P1 的 TimeManager：选择模式只是一路独立来源（TimeSource.UI），
                // 与时间规则乘法合成；关闭时只移除这一路，轮盘不再自己保存 / 恢复全局倍率。
                _time.PushScale(TimeSource.UI, _selectionScale);
                _timeScaleApplied = true;
                _inputLease = ((IRuleSelectionInputGate)_player).BlockGameplayInput();
                _focusLease = ((IRuleSelectionFocus)_cameraFocus).AcquireSelectionFocus();
                if (_focusLease == null) { ReleaseEffects(); return false; }
                _session.Open();
                _root.RemoveFromClassList("hidden");
                RefreshChoices();
                PositionWheel();
                return true;
            }
            catch (Exception error) { Close(); Debug.LogException(error, this); return false; }
        }

        private void Update()
        {
            if (_player == null || !_player.isActiveAndEnabled || _player.GetComponent<EntityStatus>().IsPaused)
            { if (IsOpen) Close(); return; }
            bool held = Keyboard.current?.leftShiftKey.isPressed == true || Keyboard.current?.rightShiftKey.isPressed == true
                || Gamepad.current?.rightTrigger.isPressed == true;
            if (!held) { if (IsOpen) Close(); _waitForRelease = false; return; }
            if (!IsOpen) { if (!_waitForRelease) TryOpen(); return; }
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) { Close(); return; }
            if (Mouse.current?.rightButton.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true)
            { _session.Back(); RefreshChoices(); return; }
            PositionWheel();
            if (Time.unscaledTime >= _nextAvailability)
            {
                _nextAvailability = Time.unscaledTime + 0.1f;
                _fan.SetAvailable(Availability());
            }
            Vector2 stick = Gamepad.current?.rightStick.ReadValue() ?? Vector2.zero;
            if (stick.sqrMagnitude > 0.15f)
            {
                Vector2 center = new Vector2(20f, _fan.contentRect.height * 0.5f);
                _fan.Select(_fan.IndexAt(center + new Vector2(stick.x, -stick.y).normalized * ((_fan.InnerRadius + _fan.OuterRadius) * 0.5f)));
            }
            else if (Mouse.current != null)
            {
                Vector2 screen = Mouse.current.position.ReadValue();
                Vector2 panel = RuntimePanelUtils.ScreenToPanel(_document.panel, new Vector2(screen.x, Screen.height - screen.y));
                _fan.Select(_fan.IndexAt(_fan.WorldToLocal(panel)));
            }
            if (Mouse.current?.leftButton.wasPressedThisFrame == true || Gamepad.current?.buttonWest.wasPressedThisFrame == true)
                ConfirmSelection();
        }

        public void SelectIndex(int index) => _fan?.Select(index);
        public bool ConfirmSelection()
        {
            if (!IsOpen || _fan.SelectedIndex < 0) return false;
            int index = _fan.SelectedIndex;
            if (Stage == RuleSelectionStage.Attribute)
            {
                if (index >= Attributes.Length || !_session.SelectAttribute(Attributes[index])) { _feedback.text = "尚不可用"; return false; }
                RefreshChoices();
                return true;
            }
            if (index >= Relations.Length) return false;
            RuleReleaseResult result = _session.Confirm(Relations[index]);
            if (result.Succeeded) { Close(); return true; }
            _feedback.text = result.Status == RuleReleaseStatus.NotEnoughEnergy ? "能量不足"
                : result.Status == RuleReleaseStatus.OnCooldown ? "还在冷却" : "尚不可用";
            _fan.SetAvailable(Availability());
            return false;
        }

        private bool[] Availability()
        {
            if (Stage == RuleSelectionStage.Attribute)
            {
                var available = new bool[Attributes.Length];
                for (int i = 0; i < available.Length; i++) available[i] = _session.IsAttributeAvailable(Attributes[i]);
                return available;
            }
            var relations = new bool[Relations.Length];
            for (int i = 0; i < relations.Length; i++) relations[i] = _session.GetBlockReason(Relations[i]) == RuleReleaseStatus.Success;
            return relations;
        }

        private void RefreshChoices()
        {
            _token.text = Stage == RuleSelectionStage.Attribute ? "属性" : AttributeWords[(int)_session.Attribute];
            _feedback.text = "";
            _fan.SetOptions(Stage == RuleSelectionStage.Attribute ? AttributeWords : RelationWords, Availability());
        }

        private void PositionWheel()
        {
            Vector3 screen = _worldCamera.WorldToScreenPoint(_player.transform.position);
            Vector2 actor = RuntimePanelUtils.ScreenToPanel(_document.panel, new Vector2(screen.x, _worldCamera.pixelHeight - screen.y));
            float width = float.IsNaN(_root.resolvedStyle.width) ? 260f : _root.resolvedStyle.width;
            float height = float.IsNaN(_root.resolvedStyle.height) ? 410f : _root.resolvedStyle.height;
            float x = Mathf.Clamp(actor.x + _playerOffset.x, 20f, Mathf.Max(20f, _document.contentRect.width - width - 20f));
            float y = Mathf.Clamp(actor.y - height * 0.5f + _playerOffset.y, 20f, Mathf.Max(20f, _document.contentRect.height - height - 20f));
            _root.style.translate = new Translate(x, y);
        }

        public void Close()
        {
            _session?.Close();
            _root?.AddToClassList("hidden");
            ReleaseEffects();
            _waitForRelease = true;
        }

        private void ReleaseEffects()
        {
            _focusLease?.Dispose(); _focusLease = null;
            _inputLease?.Dispose(); _inputLease = null;
            if (_timeScaleApplied) { _time?.RemoveScale(TimeSource.UI); _timeScaleApplied = false; }
        }

        private void OnDisable() => Close();
        private void OnApplicationFocus(bool focused) { if (!focused) Close(); }
    }
}
