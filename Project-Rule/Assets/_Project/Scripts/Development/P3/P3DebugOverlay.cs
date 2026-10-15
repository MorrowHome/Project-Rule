#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Development.P3
{
    /// <summary>
    /// P3 冒烟测试浮层：把画面上看不见的状态摊到屏幕上。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>开发用，不进构建</b>。双保险：整个文件包在 <c>#if UNITY_EDITOR</c> 里，
    /// 且住在 <c>Scripts/Development/</c>（白盒代码，与生产程序集隔离）。
    /// 发布前删掉 <c>Scripts/Development/P3/</c> 这个文件夹即可。</para>
    /// <para>只<b>读</b>状态 + 提供手动触发按钮，不参与任何玩法逻辑，所以留着也不会改变游戏行为。</para>
    /// </remarks>
    public class P3DebugOverlay : MonoBehaviour
    {
        [Header("引用（同物体的留空会自动取）")]
        [SerializeField] private EntityStatus     _status;
        [SerializeField] private EntityDamageable _damageable;
        [SerializeField] private PlayerController _player;

        [Tooltip("场景里的 CombatSystem。不接就没法测伤害。")]
        [SerializeField] private CombatSystem _combat;

        [Tooltip("场景里的 RuleSystemDriver。用于「暂停」按钮同步冻结规则时限。")]
        [SerializeField] private RuleSystemDriver _rules;

        [Header("测试伤害")]
        [SerializeField] private float _testDamage    = 10f;
        [SerializeField] private float _testKnockback = 6f;

        [Tooltip("「打我一下」的施力点：击退方向背离它。留空 = 用本物体。")]
        [SerializeField] private Transform _attackFrom;

        [Header("速度日志")]
        [Tooltip("打开面板上的「记录速度到 Console」后，每隔本值秒打一条速度向量。" +
                 "0.1 = 10 条/秒（默认，读得过来）；0.02 约等于每个物理帧一条，但 Console 会明显变慢。")]
        [SerializeField] private float _velocityLogInterval = 0.1f;

        [Header("冻结检测")]
        [Tooltip("位置没变、但竖直速度还不小时，自动打一条完整状态。" +
                 "这是「速度满速、位置不动」那个 bug 的签名（2026-10-15 查到：surfaceSlideAngle < 90 时，" +
                 "顶进竖墙的位移会被 Slide 整笔取消，且照样报 remainingVelocity=0）。")]
        [SerializeField] private bool _detectFrozenMotion = true;

        [Tooltip("低于这个竖直速度就不算「在动」，避免站在地上误报。")]
        [SerializeField] private float _frozenSpeedThreshold = 1f;

        [Tooltip("两次冻结告警的最小间隔秒数，避免刷屏。")]
        [SerializeField] private float _frozenLogInterval = 0.5f;

        private const int MaxLog = 8;

        private static readonly AttributeId[] AllAttributes = (AttributeId[])System.Enum.GetValues(typeof(AttributeId));
        private static readonly RelationId[]  AllRelations  = (RelationId[])System.Enum.GetValues(typeof(RelationId));

        private readonly List<string> _log = new List<string>();
        private bool _paused;

        private bool  _loggingVelocity;      // 是否正在往 Console 打速度
        private float _velocityLogTimer;
        private Rigidbody2D _body;           // 只为对照「物理位置 vs 插值后的 transform 位置」

        private float _lastY;                // 冻结检测用：上一帧的 transform.y
        private float _lastFrozenLog;        // 冻结检测用：上次告警的 Time.time

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_status == null)     _status     = GetComponent<EntityStatus>();
            if (_damageable == null) _damageable = GetComponent<EntityDamageable>();
            if (_player == null)     _player     = GetComponent<PlayerController>();
            _body = GetComponent<Rigidbody2D>();
        }

        private void OnEnable()
        {
            if (_combat == null) return;
            _combat.OnDamageApplied += LogDamage;
            _combat.OnEntityDied    += LogDeath;
        }

        private void OnDisable()
        {
            if (_combat == null) return;
            _combat.OnDamageApplied -= LogDamage;
            _combat.OnEntityDied    -= LogDeath;
        }

        /// <summary>
        /// 速度向量的实时日志。默认关闭 —— 打开后按 <see cref="_velocityLogInterval"/> 节流，
        /// 不节流的话 50Hz 的物理帧会把 Console 刷爆（每条 Debug.Log 都要抓调用栈，很贵）。
        /// </summary>
        /// <remarks>连带打上接地 / 冲刺 / 贴墙 / 朝向 —— 光看一个向量读不出它当时在干什么。</remarks>
        private void Update()
        {
            if (_player == null) return;

            DetectFrozenMotion();

            if (!_loggingVelocity) return;

            _velocityLogTimer -= Time.deltaTime;
            if (_velocityLogTimer > 0f) return;
            _velocityLogTimer = _velocityLogInterval;

            Vector2 vel = _player.Velocity;
            Vector3 pos = _player.transform.position;
            Vector2 rbPos = _body != null ? _body.position : Vector2.zero;

            Debug.Log($"[P3 速度] v=({vel.x:F3}, {vel.y:F3})  |v|={vel.magnitude:F3}"
                      + $"  transform=({pos.x:F4}, {pos.y:F4})  rb=({rbPos.x:F4}, {rbPos.y:F4})"
                      + $"  接地={_player.IsGrounded}  冲刺={_player.IsDashing}"
                      + $"  贴墙下滑={_player.IsWallSliding}  朝向={_player.Facing:+0;-0;0}",
                      this);   // 带上上下文对象，点 Console 那行能直接定位到本物体
        }

        /// <summary>
        /// 冻结检测：**位置没变、但竖直速度还不小** —— 位移没生效的签名。
        /// </summary>
        /// <remarks>
        /// <para>只在**异常**时打，所以常开也不会刷屏（正常情况下位置每帧都在变）。
        /// 阈值把「站在地上」（速度≈0）排除掉，不误报。</para>
        /// <para>命中时把能分辨病因的东西全打出来：物理位置 vs 插值位置、接地、左右墙、
        /// 贴墙下滑、冲刺、物理步进模式、两层暂停状态。</para>
        /// <para>⚠️ 2026-10-15 那次就是它：<c>surfaceSlideAngle &lt; 90</c> 时，速度里只要有分量
        /// 顶进竖墙，<c>Slide</c> 会把**整笔位移**取消（连沿墙下滑的竖直分量一起），
        /// 而且**照样报 <c>remainingVelocity = (0,0)</c>** —— 于是速度不清零、位置也不动。</para>
        /// </remarks>
        private void DetectFrozenMotion()
        {
            if (!_detectFrozenMotion) return;

            float y  = _player.transform.position.y;
            float vy = _player.Velocity.y;

            bool frozen = Mathf.Approximately(y, _lastY) && Mathf.Abs(vy) > _frozenSpeedThreshold;
            _lastY = y;

            if (!frozen) return;
            if (Time.time - _lastFrozenLog < _frozenLogInterval) return;   // 告警节流
            _lastFrozenLog = Time.time;

            Vector2 vel   = _player.Velocity;
            Vector2 rbPos = _body != null ? _body.position : Vector2.zero;

            // ⚠️ 全限定：本文件若 using RuleGame.Infrastructure.Time，会遮蔽 UnityEngine.Time。
            bool timePaused = RuleGame.Infrastructure.Time.TimeManager.Instance != null
                           && RuleGame.Infrastructure.Time.TimeManager.Instance.IsPaused;

            Debug.LogWarning($"[P3 冻结] 位置没变但速度非零！"
                + $"  v=({vel.x:F3}, {vel.y:F3})"
                + $"  transform.y={y:F4}  rb.y={rbPos.y:F4}"
                + $"  接地={_player.IsGrounded}  左墙={_player.WallLeft}  右墙={_player.WallRight}"
                + $"  贴墙下滑={_player.IsWallSliding}  冲刺={_player.IsDashing}"
                + $"  simulationMode={Physics2D.simulationMode}"
                + $"  TimeManager暂停={timePaused}  EntityStatus暂停={_status.IsPaused}",
                this);
        }

        // ==================================================================
        //  面板
        // ==================================================================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10f, 340f, 340f, 620f), GUI.skin.box);   // 避开 P2 测试面板的位置
            GUILayout.Label("=== P3 冒烟测试 ===");

            if (_status == null)
            {
                GUILayout.Label("✗ 本物体上没有 EntityStatus");
                GUILayout.EndArea();
                return;
            }

            Bar("生命", _status.Health, _status.HealthMax, new Color(0.9f, 0.25f, 0.25f, 0.85f));
            Bar("能量", _status.Energy, _status.EnergyMax, new Color(0.25f, 0.7f, 0.95f, 0.85f));

            string grounded = _player != null ? _player.IsGrounded.ToString() : "（未接 PlayerController）";
            GUILayout.Label($"接地：{grounded}");
            if (_player != null) GUILayout.Label($"跳数：{_player.JumpsLeft} / {_player.MaxJumps}");
            if (_player != null)
                GUILayout.Label($"冲刺：{_player.DashesLeft} / {_player.MaxDashes}"
                                + (_player.IsDashing ? "  ← 冲刺中" : ""));
            if (_player != null)
                GUILayout.Label($"贴墙：左={_player.WallLeft} 右={_player.WallRight}"
                                + (_player.IsWallSliding ? "  ← 缓慢下滑中" : ""));
            if (_player != null)
            {
                // 排查「按键和移动方向对不上」：这里能同时看到**输入**和**换算后的朝向**。
                // 按右时 Input 的 x 应该是正的；只有「摩擦 × 反转」生效时 Facing 才会与它反号。
                Vector2 inp = _player.MoveInput;
                GUILayout.Label($"输入：x={inp.x:F2}  朝向 Facing={_player.Facing:+0;-0;0}"
                                + (Mathf.Sign(inp.x) * _player.Facing < 0f && Mathf.Abs(inp.x) > 0.01f
                                   ? "  ← 输入与朝向反号（摩擦反转？）" : ""));
            }
            if (_player != null)
            {
                // 速度向量。分量带符号显示 —— 光看大小读不出是在上升还是下落。
                Vector2 vel = _player.Velocity;
                GUILayout.Label($"速度：({vel.x:+0.00;-0.00;0.00}, {vel.y:+0.00;-0.00;0.00})"
                                + $"  |v|={vel.magnitude:F2}");
                if (GUILayout.Button(_loggingVelocity ? "⏹ 停止记录速度" : "⏺ 记录速度到 Console"))
                    ToggleVelocityLog();
            }
            GUILayout.Label($"暂停：{_status.IsPaused}");
            DrawLedger();

            DrawCooldowns();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("打我一下"))       HitMe();
            if (GUILayout.Button(_paused ? "▶ 恢复" : "⏸ 暂停")) TogglePause();
            GUILayout.EndHorizontal();

            if (GUILayout.Button("回满生命与能量"))
            {
                _status.ApplyHealthDelta(_status.HealthMax);
                _status.RestoreEnergy(_status.EnergyMax);
                Push("回满");
            }

            // 冷却正常只由 RuleReleaseService 触发，而它还没有调用方（拼装界面是 P4 的活），
            // 所以这里手动造一个冷却，否则「暂停是否冻结冷却」永远测不到。
            if (GUILayout.Button("开始测试冷却（5 秒）"))
            {
                _status.BeginCooldown(AttributeId.Gravity, RelationId.Revert, 5f);
                Push("→ 重力 × 反转 冷却 5s（按暂停看它是否停住）");
            }

            if (GUILayout.Button("清空日志")) _log.Clear();

            GUILayout.Space(8f);
            GUILayout.Label($"--- 事件（最近 {MaxLog} 条）---");
            if (_log.Count == 0) GUILayout.Label("  （无）");
            foreach (string line in _log) GUILayout.Label("  " + line);

            GUILayout.EndArea();
        }

        /// <summary>
        /// 挂账（伤害 × 恒定）窗口里，模块手里压着几笔还没结算的伤害。
        /// </summary>
        /// <remarks>不开挂账窗口时恒为 0。</remarks>
        private void DrawLedger()
        {
            int pending = _combat != null && _combat.Processor != null
                ? _combat.Processor.PendingCount
                : 0;

            if (pending <= 0) return;

            GUILayout.Label($"挂账中：{pending} 笔（伤害 × 恒定 结束时一次性结算）");
        }

        private void DrawCooldowns()
        {
            GUILayout.Space(6f);
            GUILayout.Label("--- 冷却 ---");

            bool any = false;
            foreach (AttributeId attr in AllAttributes)
            {
                foreach (RelationId rel in AllRelations)
                {
                    float seconds = _status.GetCooldownRemaining(attr, rel);
                    if (seconds <= 0f) continue;
                    any = true;
                    GUILayout.Label($"  {attr} × {rel}：{seconds:F1}s");
                }
            }
            if (!any) GUILayout.Label("  （无）");
        }

        private static void Bar(string label, float value, float max, Color color)
        {
            GUILayout.Label($"{label}：{value:F1} / {max:F1}");

            Rect rect = GUILayoutUtility.GetRect(0f, 10f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;      // 只在重绘时画

            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            float ratio = max > 0f ? Mathf.Clamp01(value / max) : 0f;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ==================================================================
        //  测试动作
        // ==================================================================

        /// <summary>走的是和真实攻击完全相同的链路，所以击退、无敌帧、死亡都按正式逻辑走。</summary>
        private void HitMe()
        {
            if (_combat == null || _damageable == null)
            {
                Push("✗ 缺 CombatSystem 或 EntityDamageable");
                return;
            }

            // 先记「尝试」，再调用 —— 伤害为 0（伤害失效规则生效）时下面不会有任何事件进来，
            // 一眼就能看出「0 伤害不广播」是否成立。
            Push($"→ 尝试 {_testDamage:F1} 伤害 / 击退 {_testKnockback:F1}");
            _combat.ApplyDamage(_attackFrom != null ? _attackFrom : transform,
                                _damageable, _testDamage, _testKnockback);
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _status.SetPaused(_paused);                    // 冷却冻结（PlayerController 也从这里读）
            if (_rules != null) _rules.SetPaused(_paused);  // 规则时限冻结
            Push(_paused ? "⏸ 暂停" : "▶ 恢复");
        }

        /// <summary>开关速度向量的实时日志（见 <see cref="Update"/>）。</summary>
        private void ToggleVelocityLog()
        {
            _loggingVelocity = !_loggingVelocity;
            _velocityLogTimer = 0f;      // 打开后立刻打第一条，不用等一个间隔
            Push(_loggingVelocity
                ? $"⏺ 开始记录速度（每 {_velocityLogInterval:F2}s 一条）"
                : "⏹ 停止记录速度");
        }

        // ==================================================================
        //  事件与日志
        // ==================================================================

        private void LogDamage(DamageInfo info)
            => Push($"命中 {Describe(info.Target)}：{info.Amount:F1} 伤害，击退 {info.Knockback:F1}");

        private void LogDeath(object entity) => Push($"死亡：{Describe(entity)}");

        private static string Describe(object source)
        {
            switch (source)
            {
                case Component component: return component.gameObject.name;
                case GameObject go:       return go.name;
                default:                  return "（未知）";
            }
        }

        private void Push(string line)
        {
            _log.Insert(0, line);
            if (_log.Count > MaxLog) _log.RemoveAt(_log.Count - 1);
        }
    }
}
#endif
