using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.UI.Launch
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayerVitalsPresenter : MonoBehaviour
    {
        [SerializeField] private EntityStatus _status;

        [Tooltip("玩家身上的解锁账本；留空则自动找。解锁时在屏幕上方弹一条提示。")]
        [SerializeField] private PlayerProgress _progress;

        private VisualElement _healthClip, _energyClip;
        private Label _value;
        private Label _toast;
        private PlayerProgress _boundProgress;
        private float _toastUntil;
        private float _nextBindCheck;
        private bool _toastVisible;

        // 与 RuleWheelPresenter 的扇面文字保持一致（枚举顺序即下标）。
        private static readonly string[] AttributeWords = { "重力", "时间", "摩擦", "质量", "伤害/生命", "因果" };
        private static readonly string[] RelationWords = { "反转", "失效", "恒定", "增强", "削弱" };

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _healthClip = root.Q("healthClip");
            _energyClip = root.Q("energyClip");
            _value = root.Q<Label>("vitalsValue");
            _toast = root.Q<Label>("unlockToast");
            if (_status == null || _healthClip == null || _energyClip == null || _value == null)
            {
                Debug.LogError("[PlayerVitals] 请配置玩家 EntityStatus 和 PlayerVitals.uxml。", this);
                enabled = false;
                return;
            }
            _status.OnHealthChanged += HealthChanged;
            _status.OnEnergyChanged += EnergyChanged;
            HealthChanged(_status.Health, _status.HealthMax);
            EnergyChanged(_status.Energy, _status.EnergyMax);
            BindProgress();
        }

        private void Start() => BindProgress();

        private void OnDisable()
        {
            if (_status != null)
            {
                _status.OnHealthChanged -= HealthChanged;
                _status.OnEnergyChanged -= EnergyChanged;
            }
            UnbindProgress();
        }

        private void Update()
        {
            // 玩家可能比 HUD 晚生成：每秒重试一次，别每帧 Find。
            if (_boundProgress == null && Time.unscaledTime >= _nextBindCheck)
            {
                _nextBindCheck = Time.unscaledTime + 1f;
                BindProgress();
            }
            TickToast();
        }

        private void HealthChanged(float current, float max)
        {
            // 只改变裁切窗口，彩色原画保持固定尺寸，避免花纹和边框被压扁。
            _healthClip.style.width = Length.Percent(Ratio(current, max) * 100f);
            RefreshValue();
        }

        private void EnergyChanged(float current, float max)
        {
            _energyClip.style.width = Length.Percent(Ratio(current, max) * 100f);
            RefreshValue();
        }

        private void RefreshValue()
        {
            _value.text = $"生命 {_status.Health:0} / {_status.HealthMax:0}    能量 {_status.Energy:0} / {_status.EnergyMax:0}";
        }

        private static float Ratio(float value, float max) => max > 0f ? Mathf.Clamp01(value / max) : 0f;

        // ------------------------------------------------------------------
        //  解锁提示
        // ------------------------------------------------------------------

        private void BindProgress()
        {
            if (_progress == null) _progress = UnityEngine.Object.FindAnyObjectByType<PlayerProgress>();
            if (_progress == null || _progress == _boundProgress) return;

            UnbindProgress();
            _boundProgress = _progress;
            _boundProgress.AbilityUnlocked += OnAbilityUnlocked;
            _boundProgress.AttributeUnlocked += OnAttributeUnlocked;
            _boundProgress.RelationUnlocked += OnRelationUnlocked;
        }

        private void UnbindProgress()
        {
            if (_boundProgress == null) return;

            _boundProgress.AbilityUnlocked -= OnAbilityUnlocked;
            _boundProgress.AttributeUnlocked -= OnAttributeUnlocked;
            _boundProgress.RelationUnlocked -= OnRelationUnlocked;
            _boundProgress = null;
        }

        private void OnAbilityUnlocked(PlayerAbility ability) => ShowToast("解锁能力：" + AbilityName(ability));

        private void OnAttributeUnlocked(AttributeId attribute)
            => ShowToast("解锁规则属性：" + AttributeWords[(int)attribute]);

        private void OnRelationUnlocked(RelationId relation)
            => ShowToast("解锁规则关系：" + RelationWords[(int)relation]);

        private void ShowToast(string text)
        {
            if (_toast == null)
            {
                Debug.Log("[解锁] " + text);
                return;
            }

            _toast.text = text;
            _toast.style.opacity = 1f;
            _toastUntil = Time.unscaledTime + 2.4f;
            _toastVisible = true;
        }

        private void TickToast()
        {
            if (!_toastVisible || _toast == null) return;

            float left = _toastUntil - Time.unscaledTime;
            if (left <= 0f)
            {
                _toastVisible = false;
                _toast.text = "";
                _toast.style.opacity = 0f;
                return;
            }

            _toast.style.opacity = Mathf.Clamp01(left / 0.35f);
        }

        private static string AbilityName(PlayerAbility ability) => ability switch
        {
            PlayerAbility.WallClimb => "爬墙跳",
            PlayerAbility.AirJump => "二段跳",
            PlayerAbility.Attack => "攻击",
            PlayerAbility.Throw => "飞刀",
            _ => "能力",
        };
    }
}
