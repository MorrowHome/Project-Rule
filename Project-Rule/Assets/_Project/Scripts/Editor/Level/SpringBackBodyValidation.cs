using System.Text;
using UnityEditor;
using UnityEngine;
using RuleGame.Editor.Combat;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 回弹块的自检：**纯函数 + 预制体装配**，不需要进 Play 模式。
    /// </summary>
    /// <remarks>
    /// <para>四块都在编辑期跑：① 摩擦倍率换算；② 击退方向与**轨道投影**；③ 预制体四件套
    /// （实心 / Ground / 受击箱 / 轨道）；④ 一维运动积分（调 <see cref="SpringBackBody.StepOffset"/>，
    /// 与 <c>FixedUpdate</c> 是**同一段代码**）。前两块配错了只是「手感不对」，
    /// 受击箱漏了是「砍上去毫无反应」—— 都不容易肉眼发现。</para>
    /// </remarks>
    public static class SpringBackBodyValidation
    {
        private const float Tol = 0.001f;

        // 与预制体上 SpringBackBody 的默认值一致（改默认值记得同步这里）。
        private const float Drag = 6f;
        private const float ReturnSpeed = 1.2f;
        private const float EaseDistance = 0.5f;
        private const float SettleDistance = 0.02f;
        private const float TrackHalf = 1.5f;

        [MenuItem("规则游戏/验证/回弹块（预制体 + 纯函数）", false, 71)]
        public static void Run()
        {
            var sb = new StringBuilder();
            int pass = 0;
            int fail = 0;

            void Check(string label, bool ok, string detail = "")
            {
                sb.Append(ok ? "✓ " : "✗ ").Append(label);
                if (!ok && !string.IsNullOrEmpty(detail)) sb.Append(" —— ").Append(detail);
                sb.AppendLine();
                if (ok) pass++;
                else fail++;
            }

            // ==============================================================
            //  ① 摩擦倍率 → 回正速度倍率
            // ==============================================================
            sb.AppendLine("【1】摩擦倍率 → 回正速度倍率（默认上下限 0.25 ~ 3）");

            Check("摩擦正常 ×1 → 倍率 1",
                  Near(SpringBackBody.ReturnMultiplierFor(1f, 0.25f, 3f), 1f),
                  SpringBackBody.ReturnMultiplierFor(1f, 0.25f, 3f).ToString("0.###"));

            Check("× 增强（×3）→ 1/3（回正慢到三分之一）",
                  Near(SpringBackBody.ReturnMultiplierFor(3f, 0.25f, 3f), 1f / 3f),
                  SpringBackBody.ReturnMultiplierFor(3f, 0.25f, 3f).ToString("0.###"));

            Check("× 削弱（×0.3）→ 3（封顶）",
                  Near(SpringBackBody.ReturnMultiplierFor(0.3f, 0.25f, 3f), 3f),
                  SpringBackBody.ReturnMultiplierFor(0.3f, 0.25f, 3f).ToString("0.###"));

            Check("× 失效（×0）→ 封顶 3，不会顶到无穷大",
                  Near(SpringBackBody.ReturnMultiplierFor(0f, 0.25f, 3f), 3f),
                  SpringBackBody.ReturnMultiplierFor(0f, 0.25f, 3f).ToString("0.###"));

            Check("× 反转（倍率仍是 1）→ 1（反转不参与回正）",
                  Near(SpringBackBody.ReturnMultiplierFor(1f, 0.25f, 3f), 1f),
                  SpringBackBody.ReturnMultiplierFor(1f, 0.25f, 3f).ToString("0.###"));

            // 单调性：摩擦越大，倍率越小（= 回正越慢）。
            float[] scales = { 0f, 0.1f, 0.3f, 0.5f, 1f, 2f, 3f, 10f };
            bool monotonic = true;
            float previous = float.MaxValue;
            foreach (float scale in scales)
            {
                float value = SpringBackBody.ReturnMultiplierFor(scale, 0.25f, 3f);
                if (value > previous + Tol) monotonic = false;
                previous = value;
            }
            Check("单调：摩擦越大 → 倍率越小（回正越慢）", monotonic, "序列不是单调不增");

            // ==============================================================
            //  ② 击退方向 + 轨道投影
            // ==============================================================
            sb.AppendLine("【2】击退方向与轨道投影");

            var attacker = new GameObject("SpringBackBodyValidation_Attacker")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            try
            {
                Vector2 target = new Vector2(10f, 5f);
                const float lift = 0.3f;

                attacker.transform.position = new Vector3(8f, 5f, 0f);
                Vector2 fromLeft = EntityDamageable.ResolveKnockbackDirection(target, attacker, lift);
                Check("左方来的攻击 → 往右推（背离施加方）",
                      fromLeft.x > 0f,
                      "方向 = " + fromLeft.ToString("0.###"));
                Check("抬升量与 lift 一致（y / x ≈ 0.3）",
                      fromLeft.x > 0f && Mathf.Abs(fromLeft.y / fromLeft.x - lift) < 0.01f,
                      "y / x = " + (fromLeft.x > 0f ? (fromLeft.y / fromLeft.x).ToString("0.###") : "-"));

                attacker.transform.position = new Vector3(12f, 5f, 0f);
                Vector2 fromRight = EntityDamageable.ResolveKnockbackDirection(target, attacker, lift);
                Check("右方来的攻击 → 往左推",
                      fromRight.x < 0f,
                      "方向 = " + fromRight.ToString("0.###"));

                attacker.transform.position = new Vector3(10f, 9f, 0f);
                Vector2 fromAbove = EntityDamageable.ResolveKnockbackDirection(target, attacker, lift);
                Check("正上方来的攻击（x 相同）→ 退回默认向右",
                      fromAbove.x > 0f,
                      "方向 = " + fromAbove.ToString("0.###"));
            }
            finally
            {
                Object.DestroyImmediate(attacker);
            }

            var horizontal = new Vector2(1f, 0f);
            var vertical = new Vector2(0f, 1f);
            var diagonal = new Vector2(1f, 0.3f).normalized;

            Check("水平轨道：横向击退全额算进来",
                  Near(SpringBackBody.ProjectKnockback(horizontal, horizontal, 6f), 6f),
                  SpringBackBody.ProjectKnockback(horizontal, horizontal, 6f).ToString("0.###"));
            Check("垂直分量被吃掉：垂直击退打在水平轨道上 = 0（块不动）",
                  Near(SpringBackBody.ProjectKnockback(vertical, horizontal, 6f), 0f),
                  SpringBackBody.ProjectKnockback(vertical, horizontal, 6f).ToString("0.###"));
            Check("竖直轨道：横向攻击只剩抬升那一份（0.3 / √1.09 ≈ 0.287）",
                  Mathf.Abs(SpringBackBody.ProjectKnockback(diagonal, vertical, 6f) - 6f * 0.3f / Mathf.Sqrt(1.09f)) < 0.01f,
                  SpringBackBody.ProjectKnockback(diagonal, vertical, 6f).ToString("0.###"));

            // ==============================================================
            //  ③ 预制体装配
            // ==============================================================
            sb.AppendLine("【3】预制体装配（" + SpringBackBodyGenerator.PrefabPath + "）");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpringBackBodyGenerator.PrefabPath);
            if (prefab == null)
            {
                Check("预制体存在（先跑菜单 ⑭ 生成）", false, "找不到资产");
            }
            else
            {
                int ground = LayerMask.NameToLayer("Ground");
                Check("根在 Ground 层（层 8）—— 否则玩家会穿过去",
                      prefab.layer == ground,
                      "layer = " + prefab.layer);

                var body = prefab.GetComponent<Rigidbody2D>();
                Check("刚体是 Kinematic、不吃重力（位移由组件沿轨道自己算）",
                      body != null && body.bodyType == RigidbodyType2D.Kinematic && Mathf.Approximately(body.gravityScale, 0f),
                      body == null ? "没有 Rigidbody2D" : "bodyType = " + body.bodyType);

                var block = prefab.GetComponent<SpringBackBody>();
                Check("根上挂着 SpringBackBody", block != null, "没有组件");

                var shape = prefab.GetComponent<BoxCollider2D>();
                Check("身体碰撞体是实心的（不是 Trigger）",
                      shape != null && !shape.isTrigger,
                      shape == null ? "没有 BoxCollider2D" : "isTrigger = true");

                Transform hurtbox = prefab.transform.Find("Hurtbox");
                Check("有 Hurtbox 子物体（命中判定只查这一层）", hurtbox != null, "找不到 Hurtbox");

                if (hurtbox != null)
                {
                    var area = hurtbox.GetComponent<BoxCollider2D>();
                    Check("受击箱在 Hurtbox 层（层 10）",
                          hurtbox.gameObject.layer == Hurtbox.Layer,
                          "layer = " + hurtbox.gameObject.layer);
                    Check("受击箱是 Trigger",
                          area != null && area.isTrigger,
                          area == null ? "没有碰撞体" : "isTrigger = false");
                    // 整块可打：受击箱和身体同尺寸（块会被关卡放大成宽板，0.75 的边距会大到打不到）。
                    Check("受击箱和身体同尺寸（整块可打，缩放后也不会缩出打不到的边距）",
                          area != null && shape != null &&
                          Mathf.Abs(area.size.x - shape.size.x) < Tol &&
                          Mathf.Abs(area.size.y - shape.size.y) < Tol,
                          area == null ? "-" : area.size.ToString("0.###") + " vs body " + (shape == null ? "?" : shape.size.ToString("0.###")));

                    var hurt = hurtbox.GetComponent<Hurtbox>();
                    Object owner = ReadField(hurt, "_owner");
                    Check("受击箱的承受方（_owner）显式指到 SpringBackBody",
                          owner is SpringBackBody,
                          hurt == null ? "没有 Hurtbox 组件" : "owner = " + (owner == null ? "null" : owner.GetType().Name));
                }

                if (block != null)
                {
                    Check("默认轨道 = 水平 ±1.5 格",
                          Near(block.TrackFrom.x, -TrackHalf) && Near(block.TrackFrom.y, 0f) &&
                          Near(block.TrackTo.x, TrackHalf) && Near(block.TrackTo.y, 0f),
                          block.TrackFrom.ToString("0.##") + " → " + block.TrackTo.ToString("0.##"));

                    Transform track = prefab.transform.Find("Track");
                    Check("有 Track 白盒条子物体", track != null, "找不到 Track");

                    Object visual = ReadField(block, "_trackVisual");
                    Check("Track 条已经接进组件的 _trackVisual", ReferenceEquals(visual, track),
                          visual == null ? "null" : visual.name);
                }
            }

            // ==============================================================
            //  ④ 一维运动积分（与 FixedUpdate 同一段代码）
            // ==============================================================
            sb.AppendLine("【4】轨道内运动积分（与 FixedUpdate 同一段代码）");

            const float dt = 0.02f;                 // 50 Hz 固定物理步长

            // 从「离锚点 1 个单位、没惯性」出发，跑 seconds 秒后还剩多远。
            float RemainingAfter(float multiplier, float seconds)
            {
                float probe = 1f;
                float probeImpulse = 0f;
                int steps = Mathf.RoundToInt(seconds / dt);

                for (int i = 0; i < steps; i++)
                {
                    probe = SpringBackBody.StepOffset(
                        probe, ref probeImpulse, dt,
                        Drag, ReturnSpeed, multiplier, EaseDistance, SettleDistance,
                        -TrackHalf, TrackHalf, out _);
                }

                return Mathf.Abs(probe);
            }

            // 击退：沿轨道 v0 = 6 → 推出去约 0.9 格，之后自己回来。
            float offset = 0f;
            float impulse = 6f;
            float maxDisplacement = 0f;

            for (int i = 0; i < 200; i++)           // 4 秒
            {
                offset = SpringBackBody.StepOffset(
                    offset, ref impulse, dt,
                    Drag, ReturnSpeed, 1f, EaseDistance, SettleDistance,
                    -TrackHalf, TrackHalf, out _);
                maxDisplacement = Mathf.Max(maxDisplacement, Mathf.Abs(offset));
            }

            Check("轻击（击退 6）沿轨道推出去约 0.9 格",
                  maxDisplacement > 0.8f && maxDisplacement < 1.05f,
                  maxDisplacement.ToString("0.###"));
            Check("摩擦正常：4 秒内回到锚点",
                  Mathf.Abs(offset) <= Tol,
                  offset.ToString("0.###"));

            // 轨道限位：再大的击退也停在轨道端点。
            offset = 0f;
            impulse = 200f;
            float furthest = 0f;

            for (int i = 0; i < 120; i++)
            {
                offset = SpringBackBody.StepOffset(
                    offset, ref impulse, dt,
                    Drag, ReturnSpeed, 1f, EaseDistance, SettleDistance,
                    -TrackHalf, TrackHalf, out _);
                furthest = Mathf.Max(furthest, Mathf.Abs(offset));
            }

            Check("轨道限位：200 的击退也走不出 ±1.5 格",
                  furthest <= TrackHalf + Tol,
                  furthest.ToString("0.###"));

            // 摩擦快慢：同样跑 1 秒，倍率越小（摩擦越大）剩下得越多。
            float slow = RemainingAfter(1f / 3f, 1f);
            float normal = RemainingAfter(1f, 1f);
            float fast = RemainingAfter(3f, 1f);

            Check("摩擦 × 增强（回正倍率 1/3）→ 1 秒后剩得更远（回得更慢）",
                  slow > normal + 0.05f,
                  "慢 " + slow.ToString("0.###") + " / 正常 " + normal.ToString("0.###"));
            Check("摩擦 × 削弱（回正倍率 3）→ 1 秒后更接近锚点（回得更快）",
                  fast < normal - 0.05f,
                  "快 " + fast.ToString("0.###") + " / 正常 " + normal.ToString("0.###"));

            // 吸附：很近 + 没惯性 → 精确落到锚点。
            offset = 0.01f;
            impulse = 0f;
            offset = SpringBackBody.StepOffset(
                offset, ref impulse, dt,
                Drag, ReturnSpeed, 1f, EaseDistance, SettleDistance,
                -TrackHalf, TrackHalf, out _);
            Check("很近 + 没惯性 → 精确吸附到锚点",
                  offset == 0f,
                  offset.ToString("0.###"));

            // 速度上报：必须是「本步实际位移 ÷ dt」（承载方靠它带人）。
            offset = 0f;
            impulse = 6f;
            float stepped = SpringBackBody.StepOffset(
                offset, ref impulse, dt,
                Drag, ReturnSpeed, 1f, EaseDistance, SettleDistance,
                -TrackHalf, TrackHalf, out float reported);
            Check("上报速度 = 本步位移 ÷ dt",
                  Mathf.Abs(reported - stepped / dt) < 1e-4f,
                  reported.ToString("0.####"));

            // ==============================================================
            //  报告
            // ==============================================================
            sb.AppendLine();
            sb.Append("回弹块自检：").Append(pass).Append(" 通过 / ").Append(fail).Append(" 失败");

            if (fail > 0) Debug.LogError(sb.ToString());
            else Debug.Log(sb.ToString());
        }

        /// <summary>读组件上的对象引用序列化字段（不触发任何懒解析）。</summary>
        private static Object ReadField(Object target, string fieldName)
        {
            if (target == null) return null;

            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(fieldName);
            return property != null ? property.objectReferenceValue : null;
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= Tol;
    }
}
