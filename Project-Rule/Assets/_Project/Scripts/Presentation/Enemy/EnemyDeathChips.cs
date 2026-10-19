using System;
using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>
    /// 敌人死亡时炸开的一把同色碎片。
    /// </summary>
    /// <remarks>
    /// <para><b>运行时生成，不需要预制体</b>：碎片就是敌人自己的那张图，按 0.1 倍缩小，
    /// 换色之后自动跟着换，不用维护第二份资源。</para>
    /// <para>没有碰撞体也不进快照 —— 与 <c>PaperPlaneBurst</c>、<c>BoulderFragment</c> 同一条路线：
    /// 垃圾纯表现，落在哪都不影响玩法。</para>
    /// </remarks>
    public sealed class EnemyDeathChips : MonoBehaviour
    {
        /// <summary>整把碎片的寿命（秒）。</summary>
        public const float Lifetime = .45f;

        private SpriteRenderer[] _chips = Array.Empty<SpriteRenderer>();
        private Vector2[] _velocities = Array.Empty<Vector2>();
        private Color[] _colors = Array.Empty<Color>();
        private float _age;

        public static EnemyDeathChips Spawn(Vector3 position, Sprite sprite, Material material, Color tint, int count)
        {
            if (sprite == null || count <= 0) return null;

            var go = new GameObject("EnemyDeathChips");
            go.transform.position = position;
            EnemyDeathChips chips = go.AddComponent<EnemyDeathChips>();
            chips.Build(sprite, material, tint, count);
            return chips;
        }

        private void Build(Sprite sprite, Material material, Color tint, int count)
        {
            _chips = new SpriteRenderer[count];
            _velocities = new Vector2[count];
            _colors = new Color[count];

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Chip");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = UnityEngine.Random.insideUnitCircle * .16f;
                go.transform.localScale = Vector3.one * UnityEngine.Random.Range(.10f, .18f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                if (material != null) renderer.sharedMaterial = material;
                renderer.color = tint;
                renderer.sortingOrder = 6;

                _chips[i] = renderer;
                _velocities[i] = new Vector2(UnityEngine.Random.Range(-2.4f, 2.4f), UnityEngine.Random.Range(1.4f, 3.6f));
                _colors[i] = tint;
            }
        }

        private void Update()
        {
            if (TimeManager.Instance?.IsPaused == true) return;

            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / Lifetime);
            for (int i = 0; i < _chips.Length; i++)
            {
                _velocities[i] += Physics2D.gravity * (Time.deltaTime * .8f);
                _chips[i].transform.localPosition += (Vector3)(_velocities[i] * Time.deltaTime);
                _chips[i].transform.Rotate(0f, 0f, 240f * Time.deltaTime);
                Color color = _colors[i];
                color.a = _colors[i].a * (1f - t);
                _chips[i].color = color;
            }

            if (t >= 1f) Destroy(gameObject);
        }
    }
}
