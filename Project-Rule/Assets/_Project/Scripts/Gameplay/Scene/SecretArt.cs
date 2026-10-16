using UnityEngine;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 标记：这块美术属于「可穿透的暗墙」。
    /// </summary>
    /// <remarks>
    /// <para>暗墙被揭开时，瓦片和它的墙皮要一起消失 —— 铺装器给暗墙那一段的构件自动挂上本组件，
    /// <see cref="SecretWallField.RevealIn"/> 按范围把它们销毁。</para>
    /// <para><b>不要手工删</b>：重铺一次会自动重建。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SecretArt : MonoBehaviour
    {
        private SpriteRenderer _renderer;

        /// <summary>用于和揭示区域做范围判断的世界包围盒。</summary>
        public Bounds Bounds
        {
            get
            {
                if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
                return _renderer != null
                    ? _renderer.bounds
                    : new Bounds(transform.position, Vector3.zero);
            }
        }
    }
}
