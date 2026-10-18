using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    public abstract class CityInteractable : CityMechanism
    {
        public string Prompt = "交互";
        public virtual bool Available => true;
        public abstract bool Interact(PlayerController player);
    }
}
