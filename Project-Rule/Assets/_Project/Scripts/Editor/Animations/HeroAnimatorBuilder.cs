using System.Collections.Generic;
using RuleGame.Gameplay.Player;
using RuleGame.Presentation.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RuleGame.Editor.Animations
{
    /// <summary>
    /// 从一份**声明**重建玩家动画状态图（<c>Hero.controller</c>）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用脚本建图</b>：<c>.controller</c> 在 git 里是二进制，没法 diff、没法 review ——
    /// 「谁把哪条过渡删了」看提交记录根本看不出来。把整张图写成这份 C# 声明之后，
    /// 「谁在什么时候切到谁」就和别的代码一样能 review、能 blame。</para>
    ///
    /// <para><b>它是真相源</b>：它是「用脚本生成、脚本进仓库」形态 —— 你在窗口里手动调完，
    /// 下次要重构就重跑本脚本。⚠️ <b>重跑会覆盖窗口里的手改</b>，所以调参请改这份声明。</para>
    ///
    /// <para>参数名引用 <see cref="PlayerAnimationDriver"/> 上的常量，图和代码不会各说各话。</para>
    /// </remarks>
    public static class HeroAnimatorBuilder
    {
        private const string ControllerPath = "Assets/_Project/Art/Animations/Hero/Hero.controller";
        private const string ClipFolder     = "Assets/_Project/Art/Animations/Hero";

        // 参与重建的全部 state（名字 = 剪辑名去掉 Hero_ 前缀，也是动画参数要用的名字）
        // ⚠️ 没有 Walk：角色加速快（_groundAccel=200，0→满速约 0.04 秒），
        //    Walk 只会停**一个物理帧**就被 Run 顶掉 —— 看起来就是一次卡顿，不如不要。
        //    剪辑 Hero_Walk.anim 还在，将来若加了「按住走路」再把它加回来。
        private static readonly string[] StateNames =
        {
            // 地面
            "Idle", "Run",
            // 空中
            // ⚠️ Airborne 是**二段跳**那一下的动作（5 帧），不是滞空 —— 它是一次性动作。
            //     滞空用 Fall（美术的 air 六帧序列）。
            "JumpStart", "Airborne", "Fall", "Weightless", "WallSlide", "WallJump",
            // 其它机动
            "Push", "Land",
            // 反应
            "Hurt", "Die",
            // 战斗
            "DrawSword", "Slash1", "Slash2", "SlashDown", "Sheathe", "Throw",
            // 交互
            "PullOut", "PutBack", "OrbHold", "PickupOne", "PickupBoth",
        };

        // ==================================================================
        //  入口
        // ==================================================================

        [MenuItem("RuleGame/Animations/重建 Hero 动画图（会覆盖窗口里的手改）")]
        public static void Rebuild()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError("[HeroAnimatorBuilder] 找不到 " + ControllerPath);
                return;
            }

            // 每次重建都先清空,保证结果是「声明决定」而不是「声明 + 上次残留」。
            ClearParameters(controller);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            ClearStates(machine);
            ClearAnyStateTransitions(machine);

            Dictionary<string, AnimatorState> states = CreateStates(machine);

            Dictionary<string, float> attackSpeeds = ApplyAttackSpeeds(states);

            AddParameters(controller);
            LinkLocomotion(states);
            LinkAir(states);
            LinkActionStates(states);
            LinkReactions(states);
            LinkCombat(states, attackSpeeds);
            LinkInteract(states);
            LinkAnyState(machine, states);
            ProtectReactions(controller);

            machine.defaultState = states["Idle"];

            // ⚠️ AnimatorControllerLayer 是**值类型**，layers[0].defaultWeight = 1 改的是临时副本、
            //    不会写回 —— 必须整个数组取出来改完再赋回去。
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].defaultWeight = 1f;                 // 磁盘上原本是 0
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[HeroAnimatorBuilder] 已重建 Hero.controller："
                      + StateNames.Length + " 个 state、"
                      + controller.parameters.Length + " 个参数。");
        }

        [MenuItem("RuleGame/Animations/修复 Hero 受伤动画打断规则（保留现有动画图）")]
        public static void RepairReactionInterruptions()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new System.InvalidOperationException("找不到 " + ControllerPath);
            ProtectReactions(controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static void ProtectReactions(AnimatorController controller)
        {
            string parameter = PlayerAnimationDriver.ParamReacting;
            if (!System.Array.Exists(controller.parameters, item => item.name == parameter))
                controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            foreach (var transition in controller.layers[0].stateMachine.anyStateTransitions)
            {
                var destination = transition.destinationState;
                if (destination == null || destination.name == "Hurt" || destination.name == "Die") continue;
                if (!System.Array.Exists(transition.conditions, condition => condition.parameter == parameter))
                    transition.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
            }
        }

        // ==================================================================
        //  参数
        // ==================================================================

        private static void AddParameters(AnimatorController c)
        {
            // 持续条件：每帧重写
            c.AddParameter(PlayerAnimationDriver.ParamSpeed,          AnimatorControllerParameterType.Float);
            c.AddParameter(PlayerAnimationDriver.ParamMoving,         AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamGrounded,       AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamDashing,        AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamWallSliding,    AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamGravityFlipped, AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamRising,         AnimatorControllerParameterType.Bool);

            // 一次性事件：由玩法层的边沿事件发 Trigger
            c.AddParameter(PlayerAnimationDriver.ParamJump,     AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamAirJump,  AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamWallJump, AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamLand,     AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamHurt,     AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamDie,      AnimatorControllerParameterType.Trigger);

            // 攻击：起手一个、接续一个。分成两个是因为 AnyState → 拔剑的优先级高于一切，
            // 共用一个的话连段中途再按会从拔剑重来。
            c.AddParameter(PlayerAnimationDriver.ParamAttack,     AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamAttackNext, AnimatorControllerParameterType.Trigger);

            // 重击：独立的一条，不共用 Attack —— 那条会先进拔剑，重击是直接劈下去。
            c.AddParameter(PlayerAnimationDriver.ParamHeavy,      AnimatorControllerParameterType.Trigger);
        }

        private static void ClearParameters(AnimatorController c)
        {
            while (c.parameters.Length > 0) c.RemoveParameter(c.parameters[0]);
        }

        // ==================================================================
        //  State 布局
        // ==================================================================

        private static Dictionary<string, AnimatorState> CreateStates(AnimatorStateMachine sm)
        {
            // 手写坐标：图是给人看的，横平竖直比「自动排」可读得多。
            var layout = new Dictionary<string, Vector3>
            {
                { "Idle",       new Vector3(   0f,    0f) },
                { "Run",        new Vector3( 220f,    0f) },
                { "Land",       new Vector3( 440f,    0f) },

                { "JumpStart",  new Vector3(   0f, -130f) },
                { "Airborne",   new Vector3( 220f, -130f) },
                { "Fall",       new Vector3( 220f,  -70f) },
                { "Weightless", new Vector3( 440f, -130f) },
                { "WallSlide",  new Vector3( 660f, -130f) },
                { "WallJump",   new Vector3( 880f, -130f) },

                { "Push",       new Vector3(   0f, -260f) },

                { "Hurt",       new Vector3( 220f, -260f) },
                { "Die",        new Vector3( 440f, -260f) },

                { "DrawSword",  new Vector3(   0f, -390f) },
                { "Slash1",     new Vector3( 220f, -390f) },
                { "Slash2",     new Vector3( 440f, -390f) },
                { "SlashDown",  new Vector3( 660f, -390f) },
                { "Sheathe",    new Vector3( 880f, -390f) },
                { "Throw",      new Vector3(1100f, -390f) },

                { "PullOut",    new Vector3(   0f, -520f) },
                { "PickupOne",  new Vector3( 220f, -520f) },
                { "PickupBoth", new Vector3( 440f, -520f) },
                { "OrbHold",    new Vector3( 660f, -520f) },
                { "PutBack",    new Vector3( 880f, -520f) },
            };

            var found = new Dictionary<string, AnimatorState>();
            foreach (string name in StateNames)
            {
                Vector3 pos = layout.TryGetValue(name, out var p) ? p : Vector3.zero;

                AnimatorState state = sm.AddState(name, pos);
                state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    ClipFolder + "/Hero_" + name + ".anim");

                if (state.motion == null)
                    Debug.LogWarning("[HeroAnimatorBuilder] 剪辑缺失：Hero_" + name + ".anim（该 state 会是空的）");

                found[name] = state;
            }

            sm.anyStatePosition = new Vector3(-220f, -130f);
            return found;
        }

        private static void ClearStates(AnimatorStateMachine sm)
        {
            ChildAnimatorState[] existing = sm.states;
            for (int i = existing.Length - 1; i >= 0; i--) sm.RemoveState(existing[i].state);
        }

        private static void ClearAnyStateTransitions(AnimatorStateMachine sm)
        {
            AnimatorStateTransition[] any = sm.anyStateTransitions;
            for (int i = any.Length - 1; i >= 0; i--) sm.RemoveAnyStateTransition(any[i]);
        }

        // ==================================================================
        //  连线的两个小工具
        // ==================================================================

        /// <summary>拉一条 <paramref name="from"/> → <paramref name="to"/> 的过渡。</summary>
        private static AnimatorStateTransition Link(AnimatorState from, AnimatorState to,
                                                    float duration = 0.1f,
                                                    bool exitTime = false, float at = 1f)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime       = exitTime;
            t.exitTime          = at;
            t.hasFixedDuration  = true;      // duration 按「秒」算，不按源 state 的比例
            t.duration          = duration;
            t.canTransitionToSelf = false;
            return t;
        }

        /// <summary>给一条过渡加条件。Trigger 用 <paramref name="value"/> = true 即可。</summary>
        private static void When(AnimatorStateTransition t, AnimatorConditionMode mode,
                                 string param, float value = 0f)
            => t.AddCondition(mode, value, param);

        // ==================================================================
        //  连线：地面运动
        // ==================================================================

        private static void LinkLocomotion(Dictionary<string, AnimatorState> s)
        {
            // 地面只有 **Idle ⇄ Run** 两档，没有 Walk（理由见 StateNames 上面的说明）。
            //
            // 条件用 **Moving（有没有按方向键）**，不是速度。
            //
            // 为什么不用速度：速度会被三种情况骗到 ——
            //   ① 冰面滑行：松开方向后人还在滑，按速度切就会一直播 Run；
            //   ② 被击退 / 冲刺收尾：没人按方向，横向速度却很大；
            //   ③ 起步那一帧：速度还是 0，但人已经按下了，动画会慢半拍。
            // 用输入就没有这些歧义：**动画跟着「玩家在做什么」，不是跟着「物理结果」**。
            //
            // 另外也不该用 Blend Tree：Blend Tree 靠「按权重插值曲线」工作，而精灵帧是对象引用
            // 曲线（Sprite），Unity 对它不做插值 —— 落在两档之间就是硬切。transition + 交叉淡入才对。
            AnimatorState idle = s["Idle"], run = s["Run"];

            When(Link(idle, run,  0.08f), AnimatorConditionMode.If,    PlayerAnimationDriver.ParamMoving);
            When(Link(run,  idle, 0.10f), AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamMoving);
        }

        // ==================================================================
        //  连线：空中 / 贴墙
        // ==================================================================

        private static void LinkAir(Dictionary<string, AnimatorState> s)
        {
            // ⚠️ 这里的底座是 **Fall（滞空）**，不是 Airborne。
            //    Airborne 是**二段跳那一下**的动作，属于「一次性」，由 AnyState 的 AirJump 触发。

            // 离地 → 滞空（走下去、或在空中待着）
            foreach (string name in new[] { "Idle", "Run" })
            {
                When(Link(s[name], s["Fall"], 0.10f),
                     AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamGrounded);
            }

            // 起跳（地面跳）与二段跳统一由 AnyState 的 Jump / AirJump 触发接管，这里不重复连。

            // JumpStart 是 4 帧的一次性动作：靠 Has Exit Time 播完再进滞空，
            // 这样就**不会冻在最后一帧** —— 旧的代码驱动正是死在这里。
            // 两条出边：上升结束时提前走 / 播完兜底走。
            When(Link(s["JumpStart"], s["Fall"], 0.12f, exitTime: true, at: 0.9f),
                 AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamRising);
            Link(s["JumpStart"], s["Fall"], 0.12f, exitTime: true, at: 1f);

            // 二段跳（Airborne）也是一次性动作：翻完回滞空。
            Link(s["Airborne"], s["Fall"], 0.12f, exitTime: true, at: 1f);

            // 重力反转 ↔ 正常
            When(Link(s["Fall"], s["Weightless"], 0.12f),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamGravityFlipped);
            When(Link(s["Weightless"], s["Fall"], 0.12f),
                 AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamGravityFlipped);

            // 贴墙下滑 ↔ 滞空
            When(Link(s["Fall"], s["WallSlide"], 0.08f),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamWallSliding);
            When(Link(s["WallSlide"], s["Fall"], 0.08f),
                 AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamWallSliding);

            // 蹬墙上升时保留翻滚动画；上升结束后按贴墙输入进入下滑或滞空。
            When(Link(s["WallJump"], s["WallSlide"], 0.08f),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamWallSliding);
            When(Link(s["WallJump"], s["Fall"], 0.12f),
                 AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamRising);
            Link(s["WallJump"], s["Fall"], 0.12f, exitTime: true, at: 1f);
        }

        // ==================================================================
        //  连线：冲刺 / 落地（一次性，靠 Has Exit Time 收尾）
        // ==================================================================

        private static void LinkActionStates(Dictionary<string, AnimatorState> s)
        {
            // ⚠️ 退出必须**同时**要求 Dashing == false。只看接地的话，进入 Push 的那一帧
            //    （冲刺刚开始、Dashing 还是 true）就会立刻被这条过渡弹出去 —— 表现是「冲刺没有动画」。
            AnimatorStateTransition dashOut = Link(s["Push"], s["Fall"], 0.08f);
            When(dashOut, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamDashing);
            When(dashOut, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamGrounded);

            dashOut = Link(s["Push"], s["Idle"], 0.08f);
            When(dashOut, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamDashing);
            When(dashOut, AnimatorConditionMode.If, PlayerAnimationDriver.ParamGrounded);

            // 落地：4 帧的一次性动作，播完按当前速度回到地面那一档
            When(Link(s["Land"], s["Run"], 0.08f, exitTime: true, at: 0.85f),
                 AnimatorConditionMode.Greater, PlayerAnimationDriver.ParamSpeed, 0.35f);
            Link(s["Land"], s["Idle"], 0.08f, exitTime: true, at: 0.85f);
        }

        // ==================================================================
        //  连线：受击 / 死亡
        // ==================================================================

        private static void LinkReactions(Dictionary<string, AnimatorState> s)
        {
            Link(s["Hurt"], s["Idle"], 0.1f, exitTime: true, at: 1f);
            // Die 没有出边：它是终点，播完停在那里
        }

        // ==================================================================
        //  连线：战斗连段
        // ==================================================================
        //
        // 结构照美术给的「使用说明」：**起手拔剑 → 中间任意挥砍 → 收手收剑**。
        // 起手由 AnyState 的 Attack 触发（见 LinkAnyState），接续靠 AttackNext。

        private static void LinkCombat(Dictionary<string, AnimatorState> s,
                                       Dictionary<string, float> speeds)
        {
            // 过渡时长按源 state 的速度缩 —— 理由见 AttackBlend。
            float blendDraw  = AttackBlend(speeds, "DrawSword");
            float blendSlash1 = AttackBlend(speeds, "Slash1");
            float blendSlash2 = AttackBlend(speeds, "Slash2");
            float blendSlashDown = AttackBlend(speeds, "SlashDown");

            // 拔剑（4 帧）之后自动进第一段挥砍
            Link(s["DrawSword"], s["Slash1"], blendDraw, exitTime: true, at: 1f);

            // 第一段：收招前再按一下 → 接第二段；不按 → 收剑。
            // ⚠️ 不带 exitTime 的那条排在前面，所以按下时立刻接、不按下才等播完。
            When(Link(s["Slash1"], s["Slash2"], blendSlash1),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamAttackNext);
            Link(s["Slash1"], s["Sheathe"], blendSlash1, exitTime: true, at: 1f);

            Link(s["Slash2"], s["Sheathe"], blendSlash2, exitTime: true, at: 1f);

            // 重击：入边在 LinkAnyState（下 + 攻击），播完同样收剑。
            Link(s["SlashDown"], s["Sheathe"], blendSlashDown, exitTime: true, at: 1f);

            Link(s["Sheathe"], s["Idle"], 0.1f, exitTime: true, at: 1f);
            Link(s["Throw"], s["Idle"], 0.1f, exitTime: true, at: 1f);
        }

        // ==================================================================
        //  攻击链的播放速度：把每一刀压到 PlayerController.AttackSwingSeconds
        // ==================================================================
        //
        // ⚠️ 「动画多快播完」和「判定多久开窗」是**两个旋钮**，别绑一起：
        //    · 判定三段（起手 / 生效 / 收招）在 PlayerController，管玩法节奏 ——
        //      起手决定判定开窗**落在动画的哪一帧**（要对齐刀刃接触）。
        //    · 本文件这一套只管动画时长。
        //    动画提速 → 接触时刻提前 → 起手要跟着缩短。改一个记得看另一个。
        //
        // ⚠️ 速度按**剪辑原始时长**现算，不是写死的倍率：美术重导出更长的剪辑时，
        //    速度会自己跟着变，不用回来改这里。

        /// <summary>
        /// 「一刀」在图上连续播哪几个 state，以及**这一段该占多少秒**。
        /// </summary>
        /// <remarks>
        /// ⚠️ 目标秒数**每段各自带**，不是一个全局值：重击的 <c>SlashDown</c> 原片 0.9167 秒，
        /// 套轻击那 0.54 秒会被压成和轻击一样快，就"重"不起来了。
        /// 段的划分照美术的连段结构：起手拔剑 → 挥砍 → 收剑。
        /// </remarks>
        private static readonly (string[] States, float Seconds)[] AttackSegments =
        {
            (new[] { "DrawSword", "Slash1" }, PlayerController.AttackSwingSeconds),   // 第一刀：拔剑起手 + 挥砍
            (new[] { "Slash2" },              PlayerController.AttackSwingSeconds),   // 第二刀：回旋斩
            (new[] { "SlashDown" },           PlayerController.HeavySwingSeconds),    // 重击：单发
        };

        /// <summary>
        /// 给攻击链上的 state 算好播放速度，写进 <c>AnimatorState.speed</c>，并返回这张表。
        /// </summary>
        /// <remarks>
        /// 倍率 = 这一段的剪辑总时长 ÷ <b>这一段自己的</b>目标秒数。
        /// 攻击链以外的 state 一律不碰，保持 1（二段跳、贴墙、冲刺那些节奏都不受影响）。
        /// </remarks>
        private static Dictionary<string, float> ApplyAttackSpeeds(Dictionary<string, AnimatorState> states)
        {
            var speeds = new Dictionary<string, float>();

            foreach ((string[] segment, float target) in AttackSegments)
            {
                if (target <= 0f) continue;

                float total = 0f;
                foreach (string name in segment) total += ClipSeconds(states, name);

                if (total <= 0f) continue;   // 剪辑缺失：保持原速，别把图弄坏

                float speed = total / target;
                foreach (string name in segment)
                {
                    if (!states.TryGetValue(name, out AnimatorState state)) continue;
                    state.speed = speed;
                    speeds[name] = speed;
                }
            }

            Debug.Log("[HeroAnimatorBuilder] 攻击速度 " + DescribeSpeeds(speeds) + "。");
            return speeds;
        }

        private static float ClipSeconds(Dictionary<string, AnimatorState> states, string name)
        {
            if (!states.TryGetValue(name, out AnimatorState state)) return 0f;
            var clip = state.motion as AnimationClip;
            return clip != null ? clip.length : 0f;
        }

        private static string DescribeSpeeds(Dictionary<string, float> speeds)
        {
            string result = "";
            foreach (KeyValuePair<string, float> pair in speeds)
            {
                if (result.Length > 0) result += "、";
                result += pair.Key + " ×" + pair.Value.ToString("0.00");
            }
            return result;
        }

        /// <summary>
        /// 攻击链的过渡时长。过渡是**按秒**算的（<c>hasFixedDuration = true</c>），
        /// 所以动画被提速后必须一起缩 —— 否则同一段过渡会占掉剪辑更大的一块，看起来发糊。
        /// </summary>
        private static float AttackBlend(Dictionary<string, float> speeds, string fromName,
                                         float seconds = 0.05f)
            => speeds.TryGetValue(fromName, out float speed) && speed > 0f
                   ? seconds / speed
                   : seconds;

        // ==================================================================
        //  连线：拾取 / 交互
        // ==================================================================
        //
        // ⚠️ 同样只有接收端 —— 道具系统还没立项，没有东西会发 Interact。

        private static void LinkInteract(Dictionary<string, AnimatorState> s)
        {
            Link(s["PullOut"], s["PickupOne"], 0.05f, exitTime: true, at: 1f);
            Link(s["PickupOne"], s["PickupBoth"], 0.05f, exitTime: true, at: 1f);
            Link(s["PickupBoth"], s["PutBack"], 0.05f, exitTime: true, at: 1f);
            Link(s["PutBack"], s["Idle"], 0.1f, exitTime: true, at: 1f);

            Link(s["OrbHold"], s["PutBack"], 0.1f, exitTime: true, at: 1f);
        }

        // ==================================================================
        //  连线：AnyState（打断一切的那几条）
        // ==================================================================
        //
        // ⚠️ **顺序 = 优先级**：Unity 按列表顺序求值，先满足的赢。
        //    死亡 > 受击 > 冲刺 > 蹬墙 > 起跳 > 落地 > 攻击（轻击 / 重击）。
        //    攻击排在最后：机动动作优先，免得一刀把冲刺 / 蹬墙取消掉。

        private static void LinkAnyState(AnimatorStateMachine sm, Dictionary<string, AnimatorState> s)
        {
            AnimatorStateTransition t;

            t = sm.AddAnyStateTransition(s["Die"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamDie);

            t = sm.AddAnyStateTransition(s["Hurt"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamHurt);

            t = sm.AddAnyStateTransition(s["Push"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamDashing);

            t = sm.AddAnyStateTransition(s["WallJump"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamWallJump);

            t = sm.AddAnyStateTransition(s["JumpStart"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamJump);

            // 二段跳：和起跳分开（两个动作不一样），所以排在起跳后面独立一条。
            // 两者不会同时触发 —— 玩法层一次跳跃只发其中一个事件。
            t = sm.AddAnyStateTransition(s["Airborne"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamAirJump);

            t = sm.AddAnyStateTransition(s["Land"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamLand);

            t = sm.AddAnyStateTransition(s["DrawSword"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamAttack);

            // 重击（下 + 攻击）。和轻击并列排在最末：它也是攻击，机动动作优先。
            // ⚠️ 它**不进 DrawSword**（拔剑）—— 重击是直接劈下去的单发动作，
            //    和轻击那条链只共用「收剑」这个尾巴。所以这里的 DstState 是 SlashDown。
            t = sm.AddAnyStateTransition(s["SlashDown"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamHeavy);
        }
    }
}
