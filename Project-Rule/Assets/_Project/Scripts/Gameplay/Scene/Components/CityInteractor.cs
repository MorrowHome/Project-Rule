using UnityEngine;
using UnityEngine.InputSystem;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>玩家交互入口：找最近的 <see cref="CityInteractable"/>，把「我要用你」交给它。</summary>
    /// <remarks>
    /// <para><b>只提供交互请求</b>：正式输入与白盒输入都调 <see cref="TryInteract"/>，
    /// 避免每个机关各抢一个 E 键。</para>
    ///
    /// <para><b>交互键由本组件自己读</b>（键盘 E / 手柄 Y），所以正式关卡只要把本组件挂在玩家身上
    /// 就能用 —— 不需要再动 <c>PlayerInput</c> / <c>PlayerController</c>。
    /// 门禁照旧走 <see cref="PlayerController.IsGameplayInputSuppressed"/>（对话 / 规则轮盘 / 恢复帧）
    /// 加暂停与死亡判断。</para>
    ///
    /// <para>⚠️ <b>与 NPC 抢同一个键</b>：<c>NpcPlayerInteraction</c> 的执行顺序是 −50（比本组件早），
    /// 它一开口就 <c>BlockGameplayInput</c>，本组件看到「输入被接管」就不再触发 ——
    /// 站在 NPC 与拉杆中间按一次 E，只会被一边吃掉。</para>
    ///
    /// <para>⚠️ <b>白盒场景会自己读 E</b>（<c>BrickCityLabControls</c> / <c>DemonCastleLabControls</c>），
    /// 生成工具把本组件的 <c>_readInteractKey</c> 关掉了；不关的话一次按键触发两回，
    /// 切换型开关（摇杆）会「按一下等于没按」。</para>
    /// </remarks>
    [RequireComponent(typeof(PlayerController))]
    [DefaultExecutionOrder(0)] // 晚于 NpcPlayerInteraction(-50)：让对话先占门禁。
    public sealed class CityInteractor : MonoBehaviour
    {
        public float Range = 2f;

        [Tooltip("自己读交互键（键盘 E / 手柄 Y）。\n" +
                 "正式关卡保持勾上；白盒自己读 E 时关掉，否则一次按键触发两次（切换型开关等于没动）。")]
        [SerializeField] private bool _readInteractKey = true;

        private PlayerController _player;
        private EntityStatus _status;
        private InputAction _interact;
        private readonly Collider2D[] _hits = new Collider2D[32];

        /// <summary>是否自己读交互键。</summary>
        public bool ReadInteractKey => _readInteractKey;

        /// <summary>只给编辑器 / 白盒生成工具用。</summary>
        public void SetReadInteractKey(bool value) => _readInteractKey = value;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _status = GetComponent<EntityStatus>();

            // 与 NpcPlayerInteraction 同一条做法：代码里建动作、直接绑定设备，
            // 不动团队共享的 .inputactions（那个 Interact 动作还带 Hold 交互）。
            _interact = new InputAction("Mechanism Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonNorth");
        }

        private void OnEnable() => _interact?.Enable();

        private void OnDisable() => _interact?.Disable();

        private void OnDestroy() => _interact?.Dispose();

        private void Update()
        {
            if (!_readInteractKey || _interact == null) return;
            if (!_interact.WasPressedThisFrame()) return;
            if (!CanInteractNow()) return;
            TryInteract();
        }

        /// <summary>暂停 / 输入被接管（对话、轮盘）/ 死亡 —— 都不响应交互键。</summary>
        private bool CanInteractNow()
        {
            if (_player == null || _player.IsGameplayInputSuppressed) return false;
            if (TimeManager.Instance?.IsPaused == true) return false;
            if (_status != null && (_status.IsPaused || _status.Health <= 0f)) return false;
            return true;
        }

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
