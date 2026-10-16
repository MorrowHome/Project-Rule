using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>只提供交互请求，不读取按键。正式输入与白盒输入都调用 TryInteract，避免每个机关各抢一个 E 键。</summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class CityInteractor : MonoBehaviour
    {
        public float Range = 2f;
        private PlayerController _player;
        private readonly Collider2D[] _hits = new Collider2D[32];
        private void Awake() => _player = GetComponent<PlayerController>();
        public CityInteractable FindNearest()
        {
            var filter = new ContactFilter2D { useTriggers = true };
            int count = Physics2D.OverlapCircle(transform.position, Range, filter, _hits);
            CityInteractable nearest = null; float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var candidate = _hits[i].GetComponentInParent<CityInteractable>();
                if (candidate == null || !candidate.Available) continue;
                float d = ((Vector2)transform.position - _hits[i].ClosestPoint(transform.position)).sqrMagnitude;
                if (d < distance) { nearest = candidate; distance = d; }
            }
            return nearest;
        }
        public bool TryInteract()
        {
            if (CityMechanism.Paused) return false;
            var target = FindNearest();
            return target != null && target.Interact(_player);
        }
    }
}
