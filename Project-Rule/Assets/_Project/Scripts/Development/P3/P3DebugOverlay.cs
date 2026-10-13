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

        private const int MaxLog = 8;

        private static readonly AttributeId[] AllAttributes = (AttributeId[])System.Enum.GetValues(typeof(AttributeId));
        private static readonly RelationId[]  AllRelations  = (RelationId[])System.Enum.GetValues(typeof(RelationId));

        private readonly List<string> _log = new List<string>();
        private bool _paused;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_status == null)     _status     = GetComponent<EntityStatus>();
            if (_damageable == null) _damageable = GetComponent<EntityDamageable>();
            if (_player == null)     _player     = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            if (_combat == null) return;
            _combat.OnDamageApplied  += LogDamage;
            _combat.OnEntityDied     += LogDeath;
            _combat.OnDamageDeferred += LogDeferred;
            _combat.OnDamageSettled  += LogSettled;
        }

        private void OnDisable()
        {
            if (_combat == null) return;
            _combat.OnDamageApplied  -= LogDamage;
            _combat.OnEntityDied     -= LogDeath;
            _combat.OnDamageDeferred -= LogDeferred;
            _combat.OnDamageSettled  -= LogSettled;
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
        /// 挂账（伤害 × 恒定）期间欠了多少。正数是欠下的伤害，负数是多出来的治疗。
        /// </summary>
        /// <remarks>不开挂账窗口时账本恒为空，这里显示为 0。</remarks>
        private void DrawLedger()
        {
            float net = _combat != null && _damageable != null
                ? _combat.Damage.Ledger.GetNet(_damageable)
                : 0f;

            if (Mathf.Approximately(net, 0f)) return;

            GUILayout.Label($"欠账：{net:F1}（伤害 × 恒定 结束时一次性结算）");
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
            _combat.Damage.Apply(_attackFrom != null ? _attackFrom : transform,
                                 _damageable, _testDamage, _testKnockback);
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _status.SetPaused(_paused);                    // 冷却冻结（PlayerController 也从这里读）
            if (_rules != null) _rules.SetPaused(_paused);  // 规则时限冻结
            Push(_paused ? "⏸ 暂停" : "▶ 恢复");
        }

        // ==================================================================
        //  事件与日志
        // ==================================================================

        private void LogDamage(DamageInfo info)
            => Push($"命中 {Describe(info.Target)}：{info.Amount:F1} 伤害，击退 {info.Knockback:F1}");

        private void LogDeferred(DamageInfo info)
            => Push($"[挂账] {Describe(info.Target)} 记下 {info.Amount:F1}，未扣血");

        private void LogSettled(DamageInfo info)
            => Push($"[结算] {Describe(info.Target)} 一次落地 {info.Amount:F1}");

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
