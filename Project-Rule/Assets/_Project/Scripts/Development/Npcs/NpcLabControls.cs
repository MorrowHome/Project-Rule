using System;
using System.Collections.Generic;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Gameplay.Npcs;
using RuleGame.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Development.Npcs
{
    /// <summary>独立试播场景的画面内操作，不要求玩家操作 Hierarchy 或 Inspector。</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class NpcLabControls : MonoBehaviour
    {
        [SerializeField] private NpcPlayerInteraction _interaction;
        [SerializeField] private Camera _camera;
        [SerializeField] private DialogueSceneFlag[] _conditions = Array.Empty<DialogueSceneFlag>();
        private Label _prompt, _shopTitle, _products;
        private VisualElement _shop;
        private Button _close;
        private NpcActor _shownMerchant;
        private readonly List<Toggle> _toggles = new List<Toggle>();
        private readonly List<EventCallback<ChangeEvent<bool>>> _callbacks = new List<EventCallback<ChangeEvent<bool>>>();
        public string PromptText => _prompt?.text;
        public bool ShopVisible => _shop != null && !_shop.ClassListContains("hidden");

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _prompt = root.Q<Label>("interactionPrompt");
            _shop = root.Q("shopPanel");
            _shopTitle = root.Q<Label>("shopTitle");
            _products = root.Q<Label>("shopProducts");
            _close = root.Q<Button>("closeShop");
            _close.clicked += CloseShop;
            string[] names = { "abilityReady", "buttonPowered", "gearReleased", "hasPass", "townSoldOut" };
            for (int i = 0; i < names.Length; i++)
            {
                Toggle toggle = root.Q<Toggle>(names[i]);
                DialogueSceneFlag flag = i < _conditions.Length ? _conditions[i] : null;
                toggle.SetEnabled(flag != null);
                toggle.SetValueWithoutNotify(flag != null && flag.Value);
                EventCallback<ChangeEvent<bool>> callback = evt => flag?.SetValue(evt.newValue);
                toggle.RegisterValueChangedCallback(callback);
                _toggles.Add(toggle);
                _callbacks.Add(callback);
            }
            _shop.AddToClassList("hidden");
        }

        private void Update()
        {
            if (_interaction == null) return;
            NpcActor nearby = _interaction.NearbyNpc;
            _prompt.text = _interaction.IsReading ? "E / Y 继续交谈    Esc / B 结束交谈"
                : _interaction.IsTrading ? "E / Esc 关闭商品展示"
                : nearby != null ? "E / Y 与「" + nearby.Definition.DisplayName + "」交谈" : "走近 NPC，按 E / Y 交谈";
            bool trading = _interaction.IsTrading;
            _shop.EnableInClassList("hidden", !trading);
            if (trading && _shownMerchant != _interaction.ActiveNpc)
            {
                _shownMerchant = _interaction.ActiveNpc;
                _shopTitle.text = _shownMerchant.Definition.DisplayName + " · 商品展示";
                var lines = new List<string>();
                foreach (NpcProduct product in _shownMerchant.Definition.Products)
                    lines.Add(product.DisplayName + "    " + product.Effect);
                _products.text = string.Join("\n\n", lines);
            }
            if (!trading) _shownMerchant = null;
        }

        private void LateUpdate()
        {
            if (_camera == null || _interaction == null) return;
            Vector3 target = _interaction.transform.position;
            _camera.transform.position = new Vector3(Mathf.Clamp(target.x, -12.5f, 12.5f), Mathf.Max(1.6f, target.y), -10f);
        }

        public void CloseShop() => _interaction?.CloseTrade();

        private void OnDisable()
        {
            if (_close != null) _close.clicked -= CloseShop;
            for (int i = 0; i < _toggles.Count; i++) _toggles[i].UnregisterValueChangedCallback(_callbacks[i]);
            _toggles.Clear();
            _callbacks.Clear();
        }
    }
}
