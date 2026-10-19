using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>三色龙珠：按 <see cref="CityRewardSource.Kind"/> 选贴图，并压掉场景里按颜色区分的旧 override。</summary>
    public sealed class CityRewardSpriteView : MonoBehaviour
    {
        public CityRewardSource Source;
        public SpriteRenderer Target;
        public Sprite Power;
        public Sprite Health;
        public Sprite Energy;

        private void LateUpdate()
        {
            if (Source == null || Target == null) return;

            Sprite sprite = Source.Kind == CityRewardKind.Health ? Health
                : Source.Kind == CityRewardKind.Energy ? Energy : Power;
            if (sprite != null && Target.sprite != sprite) Target.sprite = sprite;
            if (Target.color != Color.white) Target.color = Color.white;
        }
    }
}
