using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Development.Testing;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Rules
{
    /// <summary>
    /// 伤害处理模块的验证器：验的是**世界真的变了没有**。
    /// </summary>
    /// <remarks>
    /// <para>用**真实 RuleCatalog** + 真实 GameObject（带刚体、EntityStatus、
    /// EntityDamageable、RuleAffectedBody），跑完整的结算流水线。</para>
    /// <para>⚠️ 必须在 <b>Play 模式</b>下调用。</para>
    /// </remarks>
    public static class DamageProcessorVerifier
    {
        private const float Tol = 0.001f;
        private const string CatalogPath = "Assets/_Project/Data/Rules/RuleCatalog.asset";

        private static RuleSystem _sys;

        public static string Run()
        {
            var h = new RuleVerifyHarness();

            var catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>(CatalogPath);
            if (catalog == null) return "✗ 找不到 RuleCatalog.asset";

            WorldRegistry.Initialize();
            RuleSystem.Initialize(catalog);
            _sys = RuleSystem.Instance;
            if (_sys == null) return "✗ RuleSystem 初始化失败";

            var processor = new DamageProcessor(_sys.Attributes, WorldRegistry.Instance);

            // ==============================================================
            //  ① 基线
            // ==============================================================
            h.Section("1 基线（无规则）");

            Spawn("P", Faction.Player, new Vector2(20f, 0f), out var player, out var pDmg);
            Spawn("E1", Faction.Enemy, new Vector2(0f, 0f), out var e1, out var e1Dmg);

            SetHealth(e1, 100f);
            int n = Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);
            h.Check("命中 1 个目标", n == 1, n.ToString());
            h.Check("敌人掉 10 血（100 → 90）", Mathf.Abs(e1.Health - 90f) < Tol, e1.Health.ToString("0.##"));

            // ==============================================================
            //  ② 伤害属性
            // ==============================================================
            h.Section("2 伤害属性对结算的影响");

            _sys.Request(AttributeId.Damage, RelationId.Nullify, RuleSource.Player);
            SetHealth(e1, 100f);
            n = Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);
            h.Check("伤害失效 → 目标数为 0", n == 0, n.ToString());
            h.Check("伤害失效 → 血量纹丝不动", Mathf.Abs(e1.Health - 100f) < Tol, e1.Health.ToString("0.##"));

            _sys.Request(AttributeId.Damage, RelationId.Enhance, RuleSource.Player);
            SetHealth(e1, 100f);
            Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);
            h.Check("伤害增强 → 掉 20（缩放 2）", Mathf.Abs(e1.Health - 80f) < Tol, e1.Health.ToString("0.##"));

            _sys.Request(AttributeId.Damage, RelationId.Weaken, RuleSource.Player);
            SetHealth(e1, 100f);
            Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);
            h.Check("伤害削弱 → 掉 5（缩放 0.5）", Mathf.Abs(e1.Health - 95f) < Tol, e1.Health.ToString("0.##"));

            _sys.Request(AttributeId.Damage, RelationId.Revert, RuleSource.Player);
            SetHealth(e1, 50f);
            Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);
            h.Check("伤害反转 → 攻击变回血（50 → 60）", Mathf.Abs(e1.Health - 60f) < Tol, e1.Health.ToString("0.##"));

            // ⚠️ 伤害与因果是两个独立槽位，不清掉上一节留下的伤害规则，
            //    下一节的因果测试会被「攻击转回血」之类的残留污染。
            //    （注意：不能拿 Request(Damage, Constant) 当「恢复正常」用 ——
            //    那个格位按矩阵就是「挂账」。）
            _sys.ClearAll();

            // ==============================================================
            //  ③ 因果
            // ==============================================================
            h.Section("3 因果");

            // 反转：作用对象转换
            _sys.Request(AttributeId.Causality, RelationId.Revert, RuleSource.Player);
            SetHealth(player, 100f); SetHealth(e1, 100f);
            Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);
            h.Check("✅ 因果反转 → **发起方自己**掉 10", Mathf.Abs(player.Health - 90f) < Tol, player.Health.ToString("0.##"));
            h.Check("✅ 因果反转 → 被打的目标反而没掉血", Mathf.Abs(e1.Health - 100f) < Tol, e1.Health.ToString("0.##"));

            // 削弱
            _sys.Request(AttributeId.Causality, RelationId.Weaken, RuleSource.Player);
            SetHealth(e1, 100f);
            Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);
            h.Check("因果削弱 → 掉 5", Mathf.Abs(e1.Health - 95f) < Tol, e1.Health.ToString("0.##"));

            // 增强 —— 扩散
            _sys.Request(AttributeId.Causality, RelationId.Enhance, RuleSource.Player);
            Spawn("E2", Faction.Enemy, new Vector2(1f, 0f), out var e2, out var e2Dmg);
            Spawn("E3", Faction.Enemy, new Vector2(10f, 0f), out var e3, out var e3Dmg);
            SetHealth(e1, 100f); SetHealth(e2, 100f); SetHealth(e3, 100f);

            n = Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);
            h.Check("因果增强 → 作用到 2 个目标（命中的 + 半径内的）", n == 2, n.ToString());
            h.Check("✅ 被命中的 E1 掉血", Mathf.Abs(e1.Health - 90f) < Tol, e1.Health.ToString("0.##"));
            h.Check("✅ 半径内的 E2 也被扩散到", Mathf.Abs(e2.Health - 90f) < Tol, e2.Health.ToString("0.##"));
            h.Check("✅ 半径外的 E3 不受影响", Mathf.Abs(e3.Health - 100f) < Tol, e3.Health.ToString("0.##"));

            // 增强 —— 不打自己人：敌人打玩家，旁边的另一个敌人不该被波及
            SetHealth(player, 100f); SetHealth(e2, 100f);
            n = Attack(processor, e1Dmg, new Vector2(0f, 0f), 10f, pDmg);
            h.Check("因果增强 → 敌人打玩家时作用到 1 个目标", n == 1, n.ToString());
            h.Check("✅ 玩家掉血", Mathf.Abs(player.Health - 90f) < Tol, player.Health.ToString("0.##"));
            h.Check("✅ 旁边的友军敌人**没有**被友伤", Mathf.Abs(e2.Health - 100f) < Tol, e2.Health.ToString("0.##"));

            // 失效 —— 倍率随机 + 作用对象随机
            //
            // ⚠️ 这里必须同时盯 E1 和 E2：效果倍率会随机，**作用对象也会随机**，
            //    所以「E1 血量没变」既可能是出了问题、也可能是伤害被换到 E2 身上了。
            //    只断言 E1 的话，两种情况分不开。
            _sys.Request(AttributeId.Causality, RelationId.Nullify, RuleSource.Player);

            var seen = new List<float>();
            bool rerouted = false;                 // 出现过「E1 没变」
            bool rerouteLanded = true;             // 且每次换对象时伤害确实落到了 E2 身上
            bool radiusHeld = true;                // 半径外的 E3 从不被碰

            for (int i = 0; i < 60; i++)
            {
                SetHealth(e1, 50f);
                SetHealth(e2, 50f);
                SetHealth(e3, 100f);

                Attack(processor, player, new Vector2(0f, 0f), 10f, e1Dmg);

                float applied = 50f - e1.Health;   // 正 = 掉血，负 = 回血
                if (!seen.Exists(v => Mathf.Abs(v - applied) < Tol)) seen.Add(applied);

                if (Mathf.Abs(applied) < Tol)
                {
                    rerouted = true;
                    if (Mathf.Abs(e2.Health - 50f) < Tol) rerouteLanded = false;   // 两边都没动 = 伤害蒸发了
                }

                if (Mathf.Abs(e3.Health - 100f) > Tol) radiusHeld = false;
            }

            bool allValid = seen.TrueForAll(v =>
                Mathf.Abs(v) < Tol              // 对象被换走，E1 没挨到
                || Mathf.Abs(v - 10f) < Tol     // 正常
                || Mathf.Abs(v - 20f) < Tol     // 2 倍
                || Mathf.Abs(v + 10f) < Tol);   // -1 倍：效果反向，攻击变回血

            seen.Sort();
            string seenText = string.Join("/", seen.ConvertAll(v => v.ToString("0.##")).ToArray());

            h.Check("✅ 倍率随机：E1 的变化只可能是 10（正常）/ 20（2倍）/ -10（-1倍，回血）/ 0（对象被换走）",
                    allValid, seenText);
            h.Check("✅ 确实随机（60 次里出现了不止一种结果）", seen.Count >= 2, seenText);
            h.Check("✅ 作用对象也随机（出现过 E1 挨不到打的情况）", rerouted, "一次都没换过对象");
            h.Check("✅ 换对象时伤害真的落到了范围内另一个敌人身上，没有凭空消失", rerouteLanded, "伤害蒸发了");
            h.Check("✅ 半径外的 E3 一次都没被随机到", radiusHeld, "被随机到了");

            // ==============================================================
            //  ④ 挂账
            // ==============================================================
            h.Section("4 伤害挂账（伤害 × 恒定）");

            _sys.ClearAll();                             // 因果恢复正常：不然随机会干扰断言
            _sys.Request(AttributeId.Damage, RelationId.Constant, RuleSource.Player);
            processor.Tick();                            // 让模块观察到「窗口开了」
            SetHealth(e1, 100f);

            var kb = e1.gameObject.GetComponent<TestKnockbackReceiver>();
            kb.Clear();

            n = Attack(processor, player, new Vector2(0f, 0f), 20f, e1Dmg, knockback: 5f);
            h.Check("挂账期间：算作命中 1 个目标", n == 1, n.ToString());
            h.Check("✅ 挂账期间**不立即扣血**（血量仍是 100）",
                    Mathf.Abs(e1.Health - 100f) < Tol, e1.Health.ToString("0.##"));
            h.Check("✅ 账本里挂着 1 个实体", processor.PendingCount == 1, processor.PendingCount.ToString());
            h.Check("✅ **击退立即施加**，不跟着挂账（收到 5 的冲量）",
                    kb.Count == 1 && Mathf.Abs(kb.Last.magnitude - 5f) < Tol,
                    $"次数={kb.Count} 冲量={kb.Last}");

            // 窗口内的治疗抵消挂账：再来一次 -5 的治疗
            Attack(processor, player, new Vector2(0f, 0f), -5f, e1Dmg);
            h.Check("治疗也进账本（不立即回血）", Mathf.Abs(e1.Health - 100f) < Tol, e1.Health.ToString("0.##"));
            h.Check("治疗不带击退，所以没有再推一下", kb.Count == 1, kb.Count.ToString());

            int kbBeforeSettle = kb.Count;
            _sys.ClearAll();                             // 规则结束
            processor.Tick();                            // 模块捕捉到翻转并结账
            h.Check("✅ 结算：净额 20 - 5 = 15，一次扣掉（100 → 85）",
                    Mathf.Abs(e1.Health - 85f) < Tol, e1.Health.ToString("0.##"));
            h.Check("✅ 结算时**不补击退**（挂账当时已经推过了）",
                    kb.Count == kbBeforeSettle, kb.Count.ToString());
            h.Check("结算后账本清空", processor.PendingCount == 0, processor.PendingCount.ToString());

            // ==============================================================
            //  清理
            // ==============================================================
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == "P" || go.name.StartsWith("E")) Object.DestroyImmediate(go);

            RuleSystem.Reset();
            WorldRegistry.Reset();

            return h.Report("伤害处理模块验证");
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        private static int Attack(DamageProcessor processor, object attacker, Vector2 origin,
                                  float baseAmount, IDamageable target, float knockback = 0f)
        {
            var targets = new List<IDamageable> { target };
            var attack = new AttackInfo
            {
                Attacker   = attacker,
                Origin     = origin,
                BaseAmount = baseAmount,
                Knockback  = knockback,
                Targets    = targets,
            };
            return processor.Process(attack);
        }

        private static void Spawn(string name, Faction faction, Vector2 position,
                                  out EntityStatus status, out EntityDamageable damageable)
        {
            var go = new GameObject(name);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.position = position;

            go.AddComponent<BoxCollider2D>();
            status = go.AddComponent<EntityStatus>();
            go.AddComponent<TestKnockbackReceiver>();     // ⚠️ 必须在 EntityDamageable 之前：
                                                          //    它在 Awake 里 GetComponent 一次就缓存了
            damageable = go.AddComponent<EntityDamageable>();
            go.AddComponent<RuleAffectedBody>();          // 登记进对象注册表

            // 验证要的是确定性：无抗性、无无敌帧
            var so = new SerializedObject(damageable);
            so.FindProperty("_faction").enumValueIndex = (int)faction;
            so.FindProperty("_resistance").floatValue = 0f;
            so.FindProperty("_invincibleSeconds").floatValue = 0f;
            so.ApplyModifiedProperties();
        }

        private static void SetHealth(EntityStatus status, float health)
        {
            var data = new EntityStatusData
            {
                Energy         = status.Energy,
                Health         = health,
                HeldItem       = status.HeldItem,
                ThrowableCount = status.ThrowableCount,
            };
            status.ApplyData(data);
        }
    }
}
