using UnityEngine;

namespace RuleGame.Presentation.RuleWheel
{
    /// <summary>三组交付动作共享脚底锚点与身体比例；不复用旧剪辑的 Transform 曲线。</summary>
    [CreateAssetMenu(menuName = "RuleGame/Rule Wheel/Orb Art")]
    public sealed class RuleOrbArt : ScriptableObject
    {
        public Texture2D Atlas;
        public Vector2Int CellSize = new Vector2Int(512, 384);
        public int Columns = 4;
        public int DrawCount = 8, HoldCount = 4, ReturnCount = 5;
        public float FramesPerSecond = 8f, PixelsPerUnit = 270f, BodyHeight = 1.014f;
        [Tooltip("掏出动作使用真实时间，独立于待机帧率与世界慢时间。")]
        [Min(.05f)] public float DrawDuration = .28f;
        [Min(.05f)] public float ReturnDuration = .22f;
        public Vector2 FootPivot = new Vector2(.5f, 15f / 384f);
        public Vector2 OrbOffset = new Vector2(-.608f, .740f);
        public Material BodyMaterial, HaloMaterial;
        [System.NonSerialized] private Sprite[] _frames;
        public Sprite Frame(int index)
        {
            if (Atlas == null) return null;
            int count = DrawCount + HoldCount + ReturnCount;
            if (_frames == null || _frames.Length != count || _frames[0] == null)
            {
                Release(); _frames = new Sprite[count];
                for (int i = 0; i < count; i++)
                    _frames[i] = Sprite.Create(Atlas, new Rect(i % Columns * CellSize.x,
                        Atlas.height - (i / Columns + 1) * CellSize.y, CellSize.x, CellSize.y),
                        FootPivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
            return _frames[Mathf.Clamp(index, 0, count - 1)];
        }
        private void OnDisable() => Release();
        private void Release()
        {
            if (_frames == null) return;
            foreach (Sprite frame in _frames)
                if (frame != null) { if (Application.isPlaying) Destroy(frame); else DestroyImmediate(frame); }
            _frames = null;
        }
    }
}
