using System.Text;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Rules;
using RuleGame.UI.RuleWheel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RuleGame.Development.RuleWheel
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class RuleWheelLabHUD : MonoBehaviour
    {
        [SerializeField] private EntityStatus _status;
        [SerializeField] private RuleWheelPresenter _wheel;
        private RuleSystem _rules;
        private Label _life, _energy, _active;
        private Vector2 _initialGravity;

        private void Awake() { _rules = RuleSystem.Instance; _initialGravity = Physics2D.gravity; }
        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _life = root.Q<Label>("life"); _energy = root.Q<Label>("energy"); _active = root.Q<Label>("rules");
            _status.OnHealthChanged += Health;
            _status.OnEnergyChanged += Energy;
            _rules.OnRulesChanged += Rules;
            Health(_status.Health, _status.HealthMax); Energy(_status.Energy, _status.EnergyMax); Rules();
        }
        private void Health(float current, float max) => _life.text = "生命 " + Mathf.CeilToInt(current);
        private void Energy(float current, float max) => _energy.text = "能量 " + Mathf.CeilToInt(current);
        private void Rules()
        {
            string[] attributes = { "重力", "时间", "摩擦", "质量", "伤害/生命", "因果" };
            string[] relations = { "反转", "失效", "恒定", "增强", "削弱" };
            var text = new StringBuilder();
            foreach (var slot in _rules.Table.Slots)
            {
                if (text.Length > 0) text.Append("   ");
                text.Append(attributes[(int)slot.Key]).Append(" / ").Append(relations[(int)slot.Value.Relation]);
            }
            _active.text = text.ToString();
        }
        private void Update()
        {
            if (Keyboard.current?.rKey.wasPressedThisFrame != true) return;
            _wheel.Close(); _rules.ClearAll(); _status.RestoreEnergy(_status.EnergyMax);
        }
        private void OnDisable()
        {
            _status.OnHealthChanged -= Health; _status.OnEnergyChanged -= Energy;
            _rules.OnRulesChanged -= Rules;
        }
        private void OnDestroy()
        {
            // 独立试播场景退出时清理 P2 效果，避免反转重力遗留到其它场景。
            _rules?.ClearAll(); Physics2D.gravity = _initialGravity;
        }
    }
}
