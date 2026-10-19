using UnityEngine;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 炸弹（<see cref="CityBomb"/>）的交付美术表现：待机帧与引信帧。
    /// </summary>
    /// <remarks>
    /// <para>状态每帧直接读机关，不订阅 <c>OnDetonated</c> —— 快照回溯把炸弹复活时也能自己回到待机帧。</para>
    /// <para>白盒时期引信靠 <c>CityBomb</c> 往渲染器里写红色染色；真美术不需要，这里统一把颜色压回白色，
    /// 否则「红方块 × 红染色」会变成一块暗红。</para>
    /// </remarks>
    public sealed class CityBombSpriteView : MonoBehaviour
    {
        public CityBomb Bomb;
        public SpriteRenderer Target;
        public Sprite Idle;
        public Sprite Armed;

        private void LateUpdate()
        {
            if (Bomb == null || Target == null) return;

            Target.color = Color.white;
            Sprite sprite = Bomb.Armed ? Armed : Idle;
            if (sprite != null && Target.sprite != sprite) Target.sprite = sprite;
        }
    }
}
