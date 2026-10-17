using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RuleGame.Presentation.Player
{
    /// <summary>由剑气拥有的单次补光：Lit 用 Light2D，Unlit 用精灵轮廓内的反光叠层。</summary>
    internal sealed class SwordLightPulse
    {
        private const int MaxReflections = 12;
        private static readonly int PositionId = Shader.PropertyToID("_LightPosition");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private readonly Transform _owner;
        private readonly SpriteRenderer _hero;
        private readonly Material _reflectionMaterial;
        private readonly Reflection[] _reflections = new Reflection[MaxReflections];
        private readonly Collider2D[] _colliders = new Collider2D[24];
        private readonly List<SpriteRenderer> _candidates = new List<SpriteRenderer>(16);
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        private readonly Light2D _light;
        private readonly int[] _sortingLayers;
        private Light2D _ambient;
        private bool _ambientChecked;
        private float _age, _duration, _intensity;
        private int _count;

        private sealed class Reflection
        {
            public SpriteRenderer Source, Renderer;
        }

        public SwordLightPulse(Transform owner, SpriteRenderer hero)
        {
            _owner = owner;
            _hero = hero;
            _reflectionMaterial = Resources.Load<Material>("PlayerSwordReflection_Mat");
            var go = new GameObject("PlayerSwordLight");
            go.SetActive(false);
            _light = go.AddComponent<Light2D>();
            _light.lightType = Light2D.LightType.Point;
            _light.blendStyleIndex = 0; // 叠加光能、保留 Sprite 的纹理与底色。
            _light.color = new Color(.55f, .78f, 1f);
            _light.shadowIntensity = .45f;
            _light.shadowsEnabled = true;
            _light.intensity = 0f;
            _light.enabled = false;
            var layers = SortingLayer.layers;
            _sortingLayers = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++) _sortingLayers[i] = layers[i].id;
            _light.targetSortingLayers = _sortingLayers;
            go.SetActive(true);
        }

        public void Pulse(Vector2 center, float radius, float intensity, float duration)
        {
            Clear();
            if (intensity <= 0f || _light == null) return;
            EnsureAmbient();
            if (_ambient != null) _ambient.enabled = true;
            _age = 0f; _duration = duration; _intensity = intensity;
            _light.transform.position = new Vector3(center.x, center.y, _owner.position.z);
            _light.pointLightInnerRadius = radius * .12f;
            _light.pointLightOuterRadius = radius;
            _light.intensity = intensity;
            _light.enabled = true;
            _properties.SetVector(PositionId, new Vector4(center.x, center.y, 0f, 0f));
            _properties.SetFloat(RadiusId, radius);
            _properties.SetFloat(IntensityId, intensity * .6f);
            Add(_hero);
            // 只在出手时查询附近实体，更新期复用固定槽位，不扫描全场。
            var filter = new ContactFilter2D { useTriggers = true };
            int found = Physics2D.OverlapCircle(center, radius, filter, _colliders);
            for (int i = 0; i < found && _count < MaxReflections; i++)
            {
                Collider2D collider = _colliders[i];
                var root = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                _candidates.Clear();
                root.GetComponentsInChildren(false, _candidates);
                foreach (SpriteRenderer candidate in _candidates) Add(candidate);
            }
            RefreshReflections();
        }

        private void Add(SpriteRenderer source)
        {
            if (_count >= MaxReflections || source == null || source.sprite == null
                || !source.enabled || !source.gameObject.activeInHierarchy || _reflectionMaterial == null) return;
            // Lit 精灵已经接收真实 Light2D，避免再叠一份反光。
            Material material = source.sharedMaterial;
            if (material == null || material.shader == null) return;
            string shader = material.shader.name;
            if (shader != "Sprites/Default" && !shader.Contains("Unlit")) return;
            for (int i = 0; i < _count; i++)
                if (_reflections[i].Source == source) return;
            Reflection reflection = _reflections[_count];
            if (reflection == null)
            {
                var go = new GameObject("PlayerSwordReflection");
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sharedMaterial = _reflectionMaterial;
                renderer.enabled = false;
                reflection = _reflections[_count] = new Reflection { Renderer = renderer };
            }
            reflection.Source = source;
            _count++;
        }

        public void Advance(float dt)
        {
            if (_light == null || !_light.enabled) return;
            _age += dt;
            if (_age >= _duration) { Clear(); return; }
            float t = _age / _duration;
            float fade = (1f - t) * (1f - t);
            _light.intensity = _intensity * fade;
            _properties.SetFloat(IntensityId, _intensity * fade * .6f);
            RefreshReflections();
        }

        private void EnsureAmbient()
        {
            if (_ambientChecked) return;
            _ambientChecked = true;
            // URP 在无灯时按原色显示 Lit 精灵；第一盏局部灯不能把其余场景突然变黑。
            // 仅在原本完全无灯的场景，随脉冲临时补回等价白色基底；已有灯光布置不覆盖。
            foreach (Light2D existing in Object.FindObjectsByType<Light2D>())
                if (existing != _light && existing.enabled && existing.gameObject.activeInHierarchy) return;
            var go = new GameObject("PlayerSwordAmbient"); go.SetActive(false);
            _ambient = go.AddComponent<Light2D>();
            _ambient.lightType = Light2D.LightType.Global;
            _ambient.blendStyleIndex = 0;
            _ambient.color = Color.white; _ambient.intensity = 1f;
            _ambient.targetSortingLayers = _sortingLayers;
            _ambient.enabled = false;
            go.SetActive(true);
        }

        private void RefreshReflections()
        {
            for (int i = 0; i < _count; i++)
            {
                Reflection reflection = _reflections[i];
                SpriteRenderer source = reflection.Source, renderer = reflection.Renderer;
                bool visible = source != null && source.enabled && source.gameObject.activeInHierarchy && source.sprite != null
                    && !source.forceRenderingOff;
                renderer.enabled = visible;
                if (!visible) continue;
                renderer.sprite = source.sprite;
                renderer.color = source.color;
                renderer.flipX = source.flipX; renderer.flipY = source.flipY;
                renderer.drawMode = source.drawMode; renderer.size = source.size;
                renderer.tileMode = source.tileMode;
                renderer.maskInteraction = source.maskInteraction;
                renderer.sortingLayerID = source.sortingLayerID;
                renderer.sortingOrder = source.sortingOrder + 1;
                renderer.gameObject.layer = source.gameObject.layer;
                renderer.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                renderer.transform.localScale = source.transform.lossyScale;
                renderer.SetPropertyBlock(_properties);
            }
        }

        public void Clear()
        {
            if (_light != null) { _light.intensity = 0f; _light.enabled = false; }
            if (_ambient != null) _ambient.enabled = false;
            foreach (Reflection reflection in _reflections)
            {
                if (reflection == null) continue;
                if (reflection.Renderer != null) reflection.Renderer.enabled = false;
                reflection.Source = null;
            }
            _count = 0;
        }

        public void Dispose()
        {
            if (_light != null) Object.Destroy(_light.gameObject);
            if (_ambient != null) Object.Destroy(_ambient.gameObject);
            foreach (Reflection reflection in _reflections)
                if (reflection != null && reflection.Renderer != null) Object.Destroy(reflection.Renderer.gameObject);
        }
    }
}
