using System.Collections.Generic;
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
            //     滞空用 Fall：定格 Airborne 第 1 帧（见 EnsureHeldFrameClips）。
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

            EnsureHeldFrameClips();

            // 每次重建都先清空,保证结果是「声明决定」而不是「声明 + 上次残留」。
            ClearParameters(controller);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            ClearStates(machine);
            ClearAnyStateTransitions(machine);

            Dictionary<string, AnimatorState> states = CreateStates(machine);

            AddParameters(controller);
            LinkLocomotion(states);
            LinkAir(states);
            LinkActionStates(states);
            LinkReactions(states);
            LinkCombat(states);
            LinkInteract(states);
            LinkAnyState(machine, states);

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

        private static void LinkCombat(Dictionary<string, AnimatorState> s)
        {
            // 拔剑（4 帧）之后自动进第一段挥砍
            Link(s["DrawSword"], s["Slash1"], 0.05f, exitTime: true, at: 1f);

            // 第一段：收招前再按一下 → 接第二段；不按 → 收剑。
            // ⚠️ 不带 exitTime 的那条排在前面，所以按下时立刻接、不按下才等播完。
            When(Link(s["Slash1"], s["Slash2"], 0.05f),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamAttackNext);
            Link(s["Slash1"], s["Sheathe"], 0.05f, exitTime: true, at: 1f);

            Link(s["Slash2"], s["Sheathe"], 0.05f, exitTime: true, at: 1f);
            Link(s["SlashDown"], s["Sheathe"], 0.05f, exitTime: true, at: 1f);

            Link(s["Sheathe"], s["Idle"], 0.1f, exitTime: true, at: 1f);
            Link(s["Throw"], s["Idle"], 0.1f, exitTime: true, at: 1f);
        }

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
        //    死亡 > 受击 > 冲刺 > 蹬墙 > 起跳 > 落地 > 攻击。
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
        }

        // ==================================================================
        //  定格帧剪辑（贴墙下滑 / 滞空）
        // ==================================================================
        //
        // 有些状态没有自己的素材，就**定格某张现成的帧**：把同一张图重复 N 次凑成一个
        // 合法时长的循环剪辑。比借一整段动画好 —— 借整段 = 在原地放那整段动作。
        // 📌 想换姿势就改下面的帧号；要真正的专属动画得让美术补图。

        /// <summary>贴墙下滑定格用 WallJump 序列里的第几帧（1 起数）。</summary>
        private const int WallSlideSourceFrame = 3;

        /// <summary>滞空定格用 Airborne 序列里的第几帧（1 起数）。</summary>
        private const int FallSourceFrame = 1;

        /// <summary>同一张图重复几次。给剪辑一个合法时长，循环时看不出接缝。</summary>
        private const int HeldKeyCount = 5;

        private static void EnsureHeldFrameClips()
        {
            EnsureHeldFrameClip("WallSlide", "WallJump", WallSlideSourceFrame);
            EnsureHeldFrameClip("Fall",      "Airborne", FallSourceFrame);

            // Airborne 现在是**二段跳**那一下的一次性动作，不能再循环 ——
            // 循环的话人在空中会反复翻，停不下来。
            EnsureOneShot("Airborne");
        }

        /// <summary>把 <c>&lt;源序列&gt;</c> 的第 <paramref name="frame"/> 帧定格成一个循环剪辑。</summary>
        private static void EnsureHeldFrameClip(string targetName, string sourceSequence, int frame)
        {
            string target = ClipFolder + "/Hero_" + targetName + ".anim";
            string spritePath = "Assets/_Project/Art/Sprites/Hero/" + sourceSequence
                                + "/Hero_" + sourceSequence + "_" + frame.ToString("D2") + ".png";

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite == null)
            {
                Debug.LogError("[HeroAnimatorBuilder] 找不到定格帧：" + spritePath);
                return;
            }

            // 已存在就复用（保住 GUID，场景 / 控制器引用不会断），只覆盖曲线。
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(target);
            if (clip == null)
            {
                clip = new AnimationClip { name = "Hero_" + targetName, frameRate = 12f };
                AssetDatabase.CreateAsset(clip, target);
            }

            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            var keys = new ObjectReferenceKeyframe[HeldKeyCount];
            for (int i = 0; i < HeldKeyCount; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / clip.frameRate, value = sprite };

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            SetLoopTime(clip, true);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            Debug.Log("[HeroAnimatorBuilder] 生成 Hero_" + targetName + ".anim（定格 "
                      + sourceSequence + " 第 " + frame + " 帧）。");
        }

        /// <summary>把剪辑设成一次性（不循环）。已经是就什么都不做。</summary>
        private static void EnsureOneShot(string clipName)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipFolder + "/Hero_" + clipName + ".anim");
            if (clip == null)
            {
                Debug.LogWarning("[HeroAnimatorBuilder] 找不到剪辑：" + clipName);
                return;
            }
            SetLoopTime(clip, false);
            EditorUtility.SetDirty(clip);
        }

        private static void SetLoopTime(AnimationClip clip, bool loop)
        {
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopTime == loop) return;
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
    }
}
