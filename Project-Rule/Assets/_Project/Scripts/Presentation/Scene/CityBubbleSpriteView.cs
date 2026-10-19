using UnityEngine;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>泡泡：破裂时换成破泡帧，重生后换回完整帧；CityBubble 的 SetVisible(false) 不再把渲染器关掉。</summary>
    public sealed class CityBubbleSpriteView : MonoBehaviour
    {
        public CityBubble Bubble;
        public SpriteRenderer Target;
        public Sprite Intact;
        public Sprite Burst;

        private void LateUpdate()
        {
            if (Bubble == null || Target == null) return;

            if (!Target.enabled) Target.enabled = true;
            Sprite sprite = Bubble.Broken ? Burst : Intact;
            if (sprite != null && Target.sprite != sprite) Target.sprite = sprite;
        }
    }
}
