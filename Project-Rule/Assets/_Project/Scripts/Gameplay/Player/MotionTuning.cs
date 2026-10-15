using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 一个实体全部可调的机动参数。每物理帧由 <see cref="PlayerController"/> 从序列化字段重打包一次。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么参数留在 PlayerController、只打包过来</b>：三个资产
    /// （<c>LevelRuntime.prefab</c> / <c>Test_RuleSystem.unity</c> / <c>Test_RuleWheel.unity</c>）
    /// 里存着这些字段的**现值**，搬进 ScriptableObject 会全部丢失，得逐字段核对迁移。
    /// 所以**代码解耦与参数解耦分两步走** —— 这一步资产零改动。</para>
    /// <para><b>为什么是值类型、而且每帧重打包</b>：运行期改字段立刻生效，
    /// 包括调试时用反射改值那条流程。打包成本是几十个浮点拷贝，50Hz 下可以忽略。</para>
    /// <para>⚠️ 按值传参（不用 <c>in</c>）：MCP 的 <c>execute_code</c> 走 codedom 只到 C# 5，
    /// 会把 <c>in</c> 误读成 <c>ref</c> 而编译不过 —— 这点微优化不值得让工具链测不了这些方法。</para>
    /// </remarks>
    public struct MotionTuning
    {
        // ------------------------------------------------------------------
        //  移动
        // ------------------------------------------------------------------

        /// <summary>水平最大速度。按住方向键时速度朝它加速（不是瞬间到）。</summary>
        public float MoveSpeedMax;

        /// <summary>地面水平加速度。到全速的时间 ≈ 最大速度 / 本值。</summary>
        public float GroundAccel;

        /// <summary>空中水平加速度：能左右改向，但不瞬移。0 = 空中完全锁死横向速度。</summary>
        public float AirAccel;

        /// <summary>空中无输入时的水平阻力。0 = 空中完整保持动量。</summary>
        public float AirDrag;

        /// <summary>地面减速度基准。实际减速 = 本值 × 脚下摩擦 × 摩擦规则缩放。</summary>
        public float FrictionDecel;

        // ------------------------------------------------------------------
        //  跳跃
        // ------------------------------------------------------------------

        /// <summary>第一段起跳速度。跳跃高度 = 本值² / (2 × 有效上升重力)。</summary>
        public float JumpSpeed;

        /// <summary>空中段的起跳速度。比第一段略弱，读起来像「再补一下」。</summary>
        public float AirJumpSpeed;

        /// <summary>空中还能再跳几次。0 = 只有一段跳；1 = 二段跳。</summary>
        public int MaxAirJumps;

        /// <summary>本实体自身的重力倍率（叠在规则改过的全局重力之上）。</summary>
        public float GravityScale;

        /// <summary>下落时的额外重力倍率 —— 落得比升得快，滞空更紧凑。1 = 上下对称。</summary>
        public float FallGravityMultiplier;

        /// <summary>下落速度上限（终端速度）。防止长距离下落越来越快、难以阅读。</summary>
        public float MaxFallSpeed;

        /// <summary>松开跳跃键时向上速度保留的比例 —— 可变跳跃高度。1 = 按固定高度跳。</summary>
        public float JumpCutMultiplier;

        /// <summary>土狼时间：走出平台边缘后仍可起跳的宽限秒数。</summary>
        public float CoyoteTime;

        /// <summary>跳跃缓冲：落地之前提前按下的跳跃仍然生效的秒数。</summary>
        public float JumpBuffer;

        // ------------------------------------------------------------------
        //  冲刺
        // ------------------------------------------------------------------

        /// <summary>冲刺速度。远高于 <see cref="MoveSpeedMax"/>，要的就是爆发感。</summary>
        public float DashSpeed;

        /// <summary>冲刺持续秒数。冲刺距离 = 本值 × <see cref="DashSpeed"/>。</summary>
        public float DashDuration;

        /// <summary>冲刺结束时保留多少水平速度。1 = 冲完像被弹射出去。</summary>
        public float DashExitSpeedScale;

        /// <summary>两次冲刺之间的最小间隔。冲刺进行中再按也无效。</summary>
        public float DashCooldown;

        /// <summary>冲刺次数上限，落地回满。0 = 没有冲刺。</summary>
        public int MaxDashes;

        // ------------------------------------------------------------------
        //  贴墙 / 蹬墙
        // ------------------------------------------------------------------

        /// <summary>
        /// <b>贴墙能力总开关</b>：关掉后贴墙不下滑、也蹬不了墙。
        /// </summary>
        /// <remarks>它是**这个实体自己的能力**（序列化字段），不是玩家特权 —— 敌人配一下就有。
        /// 关掉时 <see cref="WallProbeDistance"/> 等参数全部失效，但保留着不碍事。</remarks>
        public bool WallClimbEnabled;

        /// <summary>横向墙面探测距离（米）。</summary>
        public float WallProbeDistance;

        /// <summary>贴墙时的下滑速度上限。远低于终端速度 —— 贴墙是「缓慢下滑」不是「挂在墙上」。</summary>
        public float WallSlideSpeed;

        /// <summary>贴到墙那一瞬间，下落速度被墙面摩擦吃掉的快慢（单位/秒²）。</summary>
        public float WallSlideImpactDecel;

        /// <summary>【朝墙外按】时弹离墙壁的水平速度 —— 「跳出去」用。</summary>
        public float WallJumpPushSpeed;

        /// <summary>【朝墙按 / 不按】时的推力 —— 「爬升」用。**必须足够小**，
        /// 小到方向键能在人离开墙之前把它拉回来，否则一面墙爬不上去。</summary>
        public float WallClimbPushSpeed;

        /// <summary>蹬墙后的向上速度。调大它是最划算的提升：弧线更高、滞空更久、水平距离白拿。</summary>
        public float WallJumpUpSpeed;

        // ------------------------------------------------------------------
        //  碰撞与位移
        // ------------------------------------------------------------------

        /// <summary>阻挡移动、并用于接地判定的层。地形与墙体都放这里。</summary>
        public LayerMask SolidLayer;

        /// <summary>接地探测距离（米）。</summary>
        public float GroundProbeDistance;

        /// <summary>贴地吸附距离：下坡时把角色按回地面，0 = 关闭。</summary>
        public float SurfaceAnchorDistance;

        /// <summary>可沿其滑行的最大坡面角度。竖直墙是 90°，所以必须小于 90 才会被挡住。</summary>
        public float SurfaceSlideAngle;

        // ------------------------------------------------------------------
        //  击退
        // ------------------------------------------------------------------

        /// <summary>外部冲量每秒的衰减量。</summary>
        public float KnockbackDecay;

        // ------------------------------------------------------------------
        //  攻击连段
        // ------------------------------------------------------------------

        /// <summary>起手：从按键到「判定开窗」的秒数。</summary>
        public float AttackWindup;

        /// <summary>生效：判定窗的时长。只在开窗那一瞬做一次命中判定，不做持续判定。</summary>
        public float AttackActive;

        /// <summary>收招：这段时间里再按一次就接下一刀，不按就收剑。</summary>
        public float AttackRecover;

        /// <summary>连段最多打几刀。1 = 只有一下，不接。</summary>
        public int MaxCombo;
    }
}
