using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>
    /// 实际扣血后的轮廓闪光和定向冲击。覆盖层不替换原材质，也不修改动画、身体或全局时间。
    /// 四个复用槽保留连续命中的短尾迹；暂停冻结，停用与回溯清除旧时间线。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyActor))]
    [DefaultExecutionOrder(100)]
    public sealed class EnemyHitFeedback : MonoBehaviour
    {
        [Header("敌人受伤反馈")]
        [Range(.1f, .6f)] public float FlashDuration = .30f;
        [Range(.15f, .6f)] public float ImpactDuration = .32f;
        [Range(.3f, 2f)] public float ImpactScale = 1f;
        public Color HitColor = new Color(1f, .58f, .18f);
        public Color StaggerColor = new Color(1f, .28f, .07f);
        public Color WeakPointColor = new Color(.25f, .9f, 1f);

        private const int Capacity = 4;
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static Sprite _quad;
        private EnemyActor _actor;
        private Material _flashMaterial, _impactMaterial;
        private readonly List<FlashLayer> _layers = new List<FlashLayer>(2);
        private readonly Impact[] _impacts = new Impact[Capacity];
        private MaterialPropertyBlock _properties;
        private float _flashAge, _flashLength;
        private Color _flashColor;
        private bool _flashing;

        private sealed class FlashLayer
        {
            public SpriteRenderer Source, Overlay;
        }

        private sealed class Impact
        {
            public SpriteRenderer Renderer;
            public Vector3 Position;
            public float Age, Duration, Size, Angle, Strength;
            public Color Color;
            public bool Active;
        }

        public static EnemyHitFeedback Attach(EnemyActor actor, SpriteRenderer body)
        {
            if (actor == null || body == null) return null;
            var feedback = actor.GetComponent<EnemyHitFeedback>();
            if (feedback == null) feedback = actor.gameObject.AddComponent<EnemyHitFeedback>();
            feedback.Register(body);
            return feedback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedSprite()
        {
            if (_quad != null) Destroy(_quad);
            _quad = null;
        }

        private void Awake()
        {
            _actor = GetComponent<EnemyActor>();
            _properties = new MaterialPropertyBlock();
            // Resources 中的材质提供 Player 构建的 shader 引用，避免 Shader.Find 被剥离。
            _flashMaterial = Resources.Load<Material>("EnemyHitFeedback/Flash");
            _impactMaterial = Resources.Load<Material>("EnemyHitFeedback/Impact");
            foreach (FlashLayer layer in _layers) layer.Overlay.sharedMaterial = _flashMaterial;
            if (_flashMaterial == null || _impactMaterial == null)
            {
                Debug.LogError("敌人受伤反馈缺少 Resources/EnemyHitFeedback 材质。", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            _actor.Feedback += OnFeedback;
            _actor.Restored += Clear;
        }

        private void OnDisable()
        {
            _actor.Feedback -= OnFeedback;
            _actor.Restored -= Clear;
            Clear();
        }

        private void Register(SpriteRenderer source)
        {
            if (source == null) return;
            foreach (FlashLayer layer in _layers)
                if (layer.Source == source) return;
            var go = new GameObject("HitFlash") { layer = source.gameObject.layer };
            go.transform.SetParent(source.transform, false);
            var overlay = go.AddComponent<SpriteRenderer>();
            overlay.sharedMaterial = _flashMaterial;
            overlay.enabled = false;
            _layers.Add(new FlashLayer { Source = source, Overlay = overlay });
        }

        private void OnFeedback(EnemyFeedback cue)
        {
            if (cue.Kind != EnemyFeedbackKind.Hit || cue.Damage <= 0f || !enabled) return;
            bool weak = cue.Subject is EnemyWeakPoint;
            if (weak) Register(cue.Subject.GetComponent<SpriteRenderer>());
            float weight = Mathf.Clamp01(cue.Damage / Mathf.Max(1f, _actor.Status.HealthMax) * 4f);
            float strength = cue.Lethal ? 1f : cue.Staggered ? .8f : Mathf.Lerp(.15f, .5f, weight);
            _flashColor = weak ? WeakPointColor : cue.Staggered || cue.Lethal ? StaggerColor : HitColor;
            _flashAge = 0f;
            _flashLength = FlashDuration * (cue.Staggered || cue.Lethal ? 1.3f : 1f);
            _flashing = true;

            Impact slot = null;
            for (int i = 0; i < Capacity; i++)
            {
                if (_impacts[i] == null) _impacts[i] = CreateImpact();
                if (!_impacts[i].Active) { slot = _impacts[i]; break; }
            }
            // 满载时替换最旧的一次命中，数量始终有界，不占用 Boss 的招式/预警槽。
            if (slot == null)
            {
                slot = _impacts[0];
                foreach (Impact candidate in _impacts)
                    if (candidate.Age > slot.Age) slot = candidate;
            }
            float bodySize = Mathf.Min(_actor.Definition.BodySize.x, _actor.Definition.BodySize.y);
            slot.Position = cue.Position;
            slot.Age = 0f;
            slot.Duration = ImpactDuration * (cue.Lethal ? 1.25f : 1f);
            slot.Size = Mathf.Clamp(bodySize * .95f, .8f, 2.4f) * ImpactScale * Mathf.Lerp(1f, 1.65f, strength);
            slot.Angle = Mathf.Atan2(cue.Direction.y, cue.Direction.x) * Mathf.Rad2Deg;
            slot.Strength = strength;
            slot.Color = _flashColor;
            slot.Active = true;
            Present();
        }

        private Impact CreateImpact()
        {
            if (_quad == null)
            {
                Texture2D texture = Texture2D.whiteTexture;
                _quad = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    Vector2.one * .5f, texture.width);
                _quad.name = "EnemyHitQuad";
                _quad.hideFlags = HideFlags.HideAndDontSave;
            }
            var go = new GameObject("HitImpact") { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = _impactMaterial;
            renderer.sprite = _quad;
            renderer.enabled = false;
            return new Impact { Renderer = renderer };
        }

        private void LateUpdate()
        {
            if (TimeManager.Instance?.IsPaused != true && _actor.Status?.IsPaused != true)
                Advance(Time.deltaTime);
            Present();
        }

        private void Advance(float dt)
        {
            if (_flashing)
            {
                _flashAge += dt;
                if (_flashAge >= _flashLength) _flashing = false;
            }
            foreach (Impact slot in _impacts)
            {
                if (slot == null || !slot.Active) continue;
                slot.Age += dt;
                if (slot.Age >= slot.Duration) slot.Active = false;
            }
        }

        private void Present()
        {
            int sortingLayer = 0, sortingOrder = 5;
            foreach (FlashLayer layer in _layers)
            {
                SpriteRenderer source = layer.Source;
                if (layer.Overlay == null) continue;
                if (source == null) { layer.Overlay.enabled = false; continue; }
                sortingLayer = source.sortingLayerID;
                sortingOrder = Mathf.Max(sortingOrder, source.sortingOrder);
                SpriteRenderer overlay = layer.Overlay;
                overlay.enabled = _flashing && source.enabled && source.gameObject.activeInHierarchy && source.sprite != null;
                if (!overlay.enabled) continue;
                overlay.sprite = source.sprite;
                overlay.flipX = source.flipX; overlay.flipY = source.flipY;
                overlay.drawMode = source.drawMode; overlay.size = source.size;
                overlay.spriteSortPoint = source.spriteSortPoint;
                overlay.maskInteraction = source.maskInteraction;
                overlay.sortingLayerID = source.sortingLayerID;
                overlay.sortingOrder = source.sortingOrder + 1;
                overlay.color = new Color(1f, 1f, 1f, source.color.a);
                SetProperties(overlay, _flashAge / Mathf.Max(.01f, _flashLength), _flashColor, 1f);
            }
            foreach (Impact slot in _impacts)
            {
                if (slot == null) continue;
                slot.Renderer.enabled = slot.Active;
                if (!slot.Active) continue;
                Transform visual = slot.Renderer.transform;
                // 敌人移动/翻转不会拖着已发生的命中跑；按父级缩放补偿世界尺寸。
                visual.SetPositionAndRotation(slot.Position, Quaternion.Euler(0f, 0f, slot.Angle));
                Vector3 parentScale = transform.lossyScale;
                visual.localScale = new Vector3(slot.Size / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                    slot.Size / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), 1f);
                slot.Renderer.sortingLayerID = sortingLayer;
                slot.Renderer.sortingOrder = sortingOrder + 3;
                SetProperties(slot.Renderer, slot.Age / Mathf.Max(.01f, slot.Duration), slot.Color, slot.Strength);
            }
        }

        private void SetProperties(SpriteRenderer renderer, float progress, Color accent, float strength)
        {
            // 独立覆盖层自有属性块，原 Renderer 的颜色、材质和其它属性块完全保留。
            renderer.GetPropertyBlock(_properties);
            _properties.SetFloat(ProgressId, progress);
            _properties.SetColor(AccentId, accent);
            _properties.SetFloat(StrengthId, strength);
            renderer.SetPropertyBlock(_properties);
        }

        private void Clear()
        {
            _flashing = false;
            foreach (FlashLayer layer in _layers)
                if (layer.Overlay != null) layer.Overlay.enabled = false;
            foreach (Impact slot in _impacts)
                if (slot != null) { slot.Active = false; slot.Renderer.enabled = false; }
        }
    }
}
