using System;
using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 纸飞机撞碎时炸开的一把纸屑。
    /// </summary>
    /// <remarks>
    /// <para><b>运行时生成，不需要预制体</b>：<see cref="Spawn"/> 自己建一个空物体、挂上本组件、
    /// 再按数量创建几个 <see cref="SpriteRenderer"/> 子物体（用的是纸飞机自己的图，缩到 0.12~0.22）。
    /// 这样美术换图之后碎屑自动跟着换，也不用维护第二份资源。</para>
    ///
    /// <para><b>不接物理</b>：碎片没有碰撞体也没有刚体，位移自己积分、按 <c>Physics2D.gravity</c> 下落
    /// —— 与 <c>BoulderFragment</c> 同一条路线，理由也一样：实心碎片会把玩家卡住 / 顶起来。</para>
    ///
    /// <para>⚠️ 碎屑只在**表现层**：它不参与命中、不进快照、暂停时冻结（读 <c>TimeManager</c>）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PaperPlaneBurst : MonoBehaviour
    {
        /// <summary>默认碎片数量。</summary>
        public const int DefaultCount = 7;

        /// <summary>整把碎屑的寿命（秒）。到点整个物体销毁。</summary>
        public const float Lifetime = 0.55f;

        /// <summary>碎片受多少重力（1 = 与石头一致）。略小于 1 让纸片飘一会儿。</summary>
        private const float GravityScale = 0.55f;

        /// <summary>累计炸过几次（验证工具用来确认「真的炸了」）。</summary>
        public static int SpawnCount { get; private set; }

        private SpriteRenderer[] _pieces = Array.Empty<SpriteRenderer>();
        private Vector2[] _velocities = Array.Empty<Vector2>();
        private float[] _spins = Array.Empty<float>();
        private Color[] _colors = Array.Empty<Color>();
        private float _age;

        /// <summary>
        /// 在 <paramref name="position"/> 炸一把纸屑。
        /// </summary>
        /// <param name="position">撞击点。</param>
        /// <param name="direction">撞击时纸飞机的飞行方向 —— 碎屑主要朝反方向溅开。</param>
        /// <param name="sprite">碎片贴图（直接用纸飞机自己那张）。</param>
        /// <param name="material">碎片材质（沿用纸飞机的）。</param>
        /// <param name="tint">碎片颜色（沿用纸飞机的）。</param>
        /// <param name="count">碎片数量。</param>
        public static PaperPlaneBurst Spawn(Vector2 position, Vector2 direction, Sprite sprite,
                                            Material material, Color tint, int count = DefaultCount)
        {
            if (sprite == null || count <= 0) return null;

            var go = new GameObject("PaperPlaneBurst");
            go.transform.position = position;

            PaperPlaneBurst burst = go.AddComponent<PaperPlaneBurst>();
            burst.Build(direction, sprite, material, tint, count);

            SpawnCount++;
            return burst;
        }

        private void Build(Vector2 direction, Sprite sprite, Material material, Color tint, int count)
        {
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.up;
            direction.Normalize();

            _pieces     = new SpriteRenderer[count];
            _velocities = new Vector2[count];
            _spins      = new float[count];
            _colors     = new Color[count];

            Vector2 side = new Vector2(-direction.y, direction.x);

            for (int i = 0; i < count; i++)
            {
                var scrap = new GameObject("Scrap");
                scrap.transform.SetParent(transform, false);
                scrap.transform.localPosition = (Vector3)(direction * UnityEngine.Random.Range(-0.04f, 0.1f));
                scrap.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-180f, 180f));
                scrap.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.12f, 0.22f);

                var renderer = scrap.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sharedMaterial = material;
                renderer.sortingOrder = 7;

                Color color = tint;
                color.a = 1f;
                renderer.color = color;

                // 主要朝撞击反方向溅，再掺一点侧向，看起来才不是「匀速直线」
                _velocities[i] = -direction * UnityEngine.Random.Range(1.2f, 3.6f)
                                 + side * UnityEngine.Random.Range(-2.2f, 2.2f)
                                 + Vector2.up * UnityEngine.Random.Range(0.2f, 1.8f);

                _spins[i] = UnityEngine.Random.Range(-540f, 540f);
                _colors[i] = color;
                _pieces[i] = renderer;
            }
        }

        private void Update()
        {
            if (TimeManager.Instance?.IsPaused == true) return;   // 暂停：碎屑定格

            float dt = Time.deltaTime;
            _age += dt;

            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }

            float fade = 1f - _age / Lifetime;
            fade *= fade;                                        // 后半段掉得快一点

            for (int i = 0; i < _pieces.Length; i++)
            {
                SpriteRenderer piece = _pieces[i];
                if (piece == null) continue;

                _velocities[i] += Physics2D.gravity * GravityScale * dt;
                piece.transform.position += (Vector3)(_velocities[i] * dt);
                piece.transform.Rotate(0f, 0f, _spins[i] * dt);

                Color color = _colors[i];
                color.a = fade;
                piece.color = color;
            }
        }
    }
}
