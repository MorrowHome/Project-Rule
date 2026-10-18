using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 跳穿平台瓦片：画在**逻辑层**，运行时由 <see cref="OneWayField"/> 变成一格一块的单向平台。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么做成瓦片</b>：这样它能和地形一样按格对齐、一样由铺装器铺美术
    /// （用所在区域套件的方块），只是**染一层色**让人一眼看出「这块能跳穿」。</para>
    ///
    /// <para><b>Collider Type 必须是 None</b>：碰撞由 <see cref="OneWayField"/> 逐格生成 ——
    /// 整张 Tilemap 只有一个碰撞体，没法逐格开关，所以不能让 Tilemap 自己带碰撞。</para>
    ///
    /// <para><b>颜色</b>：<see cref="_tint"/> 就是铺装时的染色，改它就能换滤镜，
    /// 不用动美术素材。</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "OneWayTile", menuName = "RuleGame/瓦片/跳穿平台瓦片")]
    public sealed class OneWayTile : Tile
    {
        [Tooltip("铺装时给方块染的颜色：让「能跳穿」一眼看得出来")]
        [SerializeField] private Color _tint = new Color(0.62f, 0.86f, 1f, 1f);

        [Tooltip("主动跳穿的放行时长（秒），转交给每格平台")]
        [SerializeField] private float _dropDuration = 0.35f;

        public Color Tint => _tint;

        public float DropDuration => _dropDuration;
    }
}
