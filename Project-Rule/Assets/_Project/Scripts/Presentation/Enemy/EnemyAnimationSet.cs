using System;
using UnityEngine;

namespace RuleGame.Presentation.Enemy
{
    [Serializable]
    public sealed class EnemySpriteSequence
    {
        public string Action;
        public Sprite[] Frames = Array.Empty<Sprite>();
        public Vector2[] ClockFaceCenters = Array.Empty<Vector2>();
        public float FrameRate = 8f;
        public bool FacesRight = true;
        public float Duration => Frames.Length / Mathf.Max(1f, FrameRate);
    }
    [CreateAssetMenu(menuName = "RuleGame/Enemy/Animation Set")]
    public sealed class EnemyAnimationSet : ScriptableObject
    {
        public EnemySpriteSequence[] Sequences = Array.Empty<EnemySpriteSequence>();
    }
}
