#if UNITY_EDITOR
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;

namespace RuleGame.Development.P3
{
    /// <summary>
    /// 攻击训练靶：**被打会闪、会被推、头顶有血条、停手一会儿自动回满**。
    /// </summary>
    /// <remarks>
    /// <para>为什么需要它：光有 <see cref="EntityDamageable"/> + <see cref="EntityStatus"/>
    /// 的靶子挨打时**毫无反馈**（血量掉在内存里、屏幕上什么也看不见），根本没法拿它试判定框。
    /// 这个组件把「挨打」变成看得见的事。</para>
    ///
    /// <para><b>同时实现 <see cref="IKnockbackReceiver"/></b>：<see cref="EntityDamageable"/>
    /// 在 <c>Awake</c> 里 <c>GetComponent&lt;IKnockbackReceiver&gt;()</c>，本组件必须在**同一个物体**上
    /// 且早于它（挂上去就是序列化组件，满足）。没有它的话击退会被静默丢掉。</para>
    ///
    /// <para>⚠️ <b>开发用，不进构建</b>：整个文件包在 <c>#if UNITY_EDITOR</c> 里，
    /// 且住在 <c>Scripts/Development/</c>（白盒代码，与生产程序集隔离）。</para>
    /// </remarks>
    [RequireComponent(typeof(EntityStatus), typeof(EntityDamageable))]
    public class AttackTestDummy : MonoBehaviour, IKnockbackReceiver
    {
        [Header("受击表现")]
        [Tooltip("闪白的持续时间（秒）。")]
        [SerializeField] private float _flashSeconds = 0.08f;

        [Tooltip("击退冲量缩放。1 = 原样。")]
        [SerializeField] private float _impulseScale = 1f;

        [Tooltip("被推起来之后每秒衰减多少速度（只在靶子是 Dynamic 刚体时生效）。")]
        [SerializeField] private float _damping = 8f;

        [Header("自动回血")]
        [Tooltip("停手多少秒之后回满。0 = 不回血（打完一直掉）。")]
        [SerializeField] private float _resetDelay = 2f;

        [Header("血条")]
        [SerializeField] private bool  _showHealthBar = true;

        [Tooltip("血条画在靶子上方多高（世界单位）。")]
        [SerializeField] private float _barHeight = 1.0f;

        // ==================================================================
        //  运行时
        // ==================================================================

        private EntityStatus   _status;
        private SpriteRenderer _renderer;
        private Rigidbody2D    _body;
        private Color          _baseColor = Color.white;

        private float   _lastHitTime = -999f;
        private float   _lastAmount;
        private Vector2 _lastImpulse;
        private bool    _healing;          // 回血时压住"又被打了"的判断，否则会无限重置计时
        private float   _prevHealth;

        private void Awake()
        {
            _status   = GetComponent<EntityStatus>();
            _renderer = GetComponentInChildren<SpriteRenderer>();
            _body     = GetComponent<Rigidbody2D>();
            if (_renderer != null) _baseColor = _renderer.color;
        }

        private void OnEnable()
        {
            if (_status != null)
            {
                _status.OnHealthChanged += HandleHealthChanged;
                _prevHealth = _status.Health;
            }
        }

        private void OnDisable()
        {
            if (_status != null) _status.OnHealthChanged -= HandleHealthChanged;
        }

        private void HandleHealthChanged(float current, float max)
        {
            if (_healing) return;                  // 自动回血自己触发的那次，不算"被打"
            _lastAmount  = _prevHealth - current;  // 掉多少就是这一下打了多少
            _prevHealth  = current;
            _lastHitTime = Time.time;
            if (_renderer != null) _renderer.color = Color.white;
        }

        /// <summary>见 <see cref="IKnockbackReceiver"/>。靶子是 Dynamic 刚体，直接加到速度上就有可见的位移。</summary>
        public void AddImpulse(Vector2 impulse)
        {
            _lastImpulse = impulse;
            if (_body != null && _body.bodyType == RigidbodyType2D.Dynamic)
                _body.linearVelocity += impulse * _impulseScale;
        }

        private void Update()
        {
            // 闪白 → 恢复原色
            if (_renderer != null && _renderer.color != _baseColor && Time.time - _lastHitTime > _flashSeconds)
                _renderer.color = _baseColor;

            // 被推起来之后慢慢停（Dynamic 才有意义）
            if (_body != null && _body.bodyType == RigidbodyType2D.Dynamic
                && _body.linearVelocity.sqrMagnitude > 0.01f)
            {
                _body.linearVelocity = Vector2.MoveTowards(
                    _body.linearVelocity, Vector2.zero, _damping * Time.deltaTime);
            }

            // 停手一会儿自动回满 —— 训练靶要能反复打
            if (_resetDelay > 0f && _status != null && Time.time - _lastHitTime > _resetDelay
                && _status.Health < _status.HealthMax)
            {
                _healing = true;
                _status.ApplyHealthDelta(_status.HealthMax - _status.Health);
                _healing    = false;
                _prevHealth = _status.Health;      // 对齐，免得下一刀把回血也算进伤害
            }
        }

        // ==================================================================
        //  血条（世界坐标 → 屏幕）
        // ==================================================================

        private void OnGUI()
        {
            if (!_showHealthBar || _status == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * _barHeight);
            if (sp.z < 0f) return;                 // 在相机背后

            const float W = 90f, H = 12f;
            var rect = new Rect(sp.x - W / 2f, Screen.height - sp.y - H, W, H);

            float ratio = _status.HealthMax > 0f ? Mathf.Clamp01(_status.Health / _status.HealthMax) : 0f;

            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), Texture2D.whiteTexture);

                GUI.color = new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);

                GUI.color = Color.Lerp(new Color(0.95f, 0.25f, 0.2f), new Color(0.35f, 0.85f, 0.45f), ratio);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            var style = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
            GUI.Label(rect, $"{_status.Health:F0} / {_status.HealthMax:F0}", style);

            // 最后一次挨打的读数（调判定框时最有用）
            if (Time.time - _lastHitTime < 1.5f)
            {
                var s2 = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
                s2.normal.textColor = new Color(1f, 0.9f, 0.4f);
                GUI.Label(new Rect(rect.x - 60f, rect.y - 18f, W + 120f, 16f),
                          $"伤害 {_lastAmount:F1}   击退 ({_lastImpulse.x:F1}, {_lastImpulse.y:F1})", s2);
            }
        }
    }
}
#endif
