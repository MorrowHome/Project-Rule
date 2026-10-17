using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    public sealed class CityRefill : CityInteractable
    {
        public float Amount = 30f, Cooldown = 4f;
        public UnityEvent OnRefilled = new UnityEvent();
        private float _remaining;
        public override bool Available => _remaining <= 0f;
        private void FixedUpdate() { if (!Paused) _remaining = Mathf.Max(0f, _remaining - Time.fixedDeltaTime); }
        public override bool Interact(PlayerController player)
        {
            if (!Available || Paused || player == null) return false;
            var status = player.GetComponent<EntityStatus>();
            if (status == null || status.Energy >= status.EnergyMax) return false;
            status.RestoreEnergy(Mathf.Max(0f, Amount)); _remaining = Cooldown; OnRefilled.Invoke(); return true;
        }
        protected override object CaptureMechanismState() => _remaining;
        protected override void RestoreMechanismState(object state) => _remaining = (float)state;
    }
}
