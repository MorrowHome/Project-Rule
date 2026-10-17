using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 表面瓦片：在普通 <see cref="Tile"/> 上再记一个**摩擦系数**，用来做冰面这类特殊地面。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要单独做一种瓦片</b>：摩擦力是从碰撞体的 <c>PhysicsMaterial2D</c> 读的，
    /// 而一整张 Tilemap 只有**一个**碰撞体 —— 没法逐格设摩擦。
    /// 所以把「这一格滑不滑」记在瓦片资产自己身上，玩家踩上去时按**脚下那一格**查。</para>
    ///
    /// <para><b>怎么用</b>：新建一个本类型的瓦片资产，<see cref="Friction"/> 填 0 就是冰面；
    /// 丢进 Tile Palette 当普通瓦片刷（仍然要在**逻辑层**，它照样是实心几何、有碰撞）。</para>
    ///
    /// <para><b>美术</b>：这种瓦片**不参与铺装**（铺装器会跳过它、只把瓦片自身的贴图搬到美术层），
    /// 所以它的贴图就是最终画面 —— 白盒阶段直接给一块纯色即可。</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "SurfaceTile", menuName = "RuleGame/瓦片/表面瓦片")]
    public sealed class SurfaceTile : Tile
    {
        [Tooltip("摩擦系数：0 = 完全打滑（冰面），1 = 普通地面。大于 1 = 更黏。")]
        [SerializeField, Range(0f, 4f)] private float _friction = 1f;

        /// <summary>脚下是这一格时的摩擦系数。</summary>
        public float Friction => _friction;
    }
}
