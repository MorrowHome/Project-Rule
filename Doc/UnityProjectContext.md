# Unity 项目上下文

<!-- unity-onboarding:generated:start -->

分析日期：2026-10-15；基线 `9af8f80`（最新角色动画），关闭白盒无敌已提交为 `f927297`。当前工作分支 `feat/better-boss`（fork → 原仓库 `p2-p3-merge` 的 PR #10），已衔接之前的 Boss 距离/破韧与身体阻挡，并新增蝙蝠高机动空战。Unity 工程在仓库下的 `Project-Rule/`，不是仓库根目录。

## 已确认环境

- Unity `6000.6.4f1`，来自 `ProjectSettings/ProjectVersion.txt`。
- 单机 2D 横版项目；URP 17.6.0，QualitySettings 的管线 GUID 指向 `Assets/_Project/Settings/UniversalRP.asset`。
- Input System 1.20.0，PlayerSettings `activeInputHandler = 1`；玩家控制器通过 PlayerInput 使用 Input System。Cinemachine 6.6.0 已用于最新测试场景。
- UnityHFSM 固定 `v2.3.0`，Boss 使用 Alive / Transition / Dead 外层状态机与动作子状态机。
- 已安装 Yarn Spinner 嵌入包；Unity Test Framework 1.8.0 声明在 manifest 中。未发现一方独立 NUnit 测试程序集；项目常用 Editor 验证器。
- 无已确认的联网玩法。TapTap 发行为文档目标，实际目标平台构建未验证。

## 结构与程序集

主要源码根为 `Assets/_Project/Scripts`，数据为 `Data`，表现资源为 `Art`、`UI`、`Shaders`，可复用实体为 `Prefabs`。

| 程序集 | 职责 / 依赖 |
| --- | --- |
| Infrastructure | 时间、注册表、快照、流程与共享契约；基础层 |
| Core | 规则门面与六类属性；依赖 Infrastructure |
| Gameplay | Player、Combat、Enemy、Scene；依赖 Core、Infrastructure、Input System、UnityHFSM |
| Persistence | 存档；依赖 Core、Infrastructure |
| UI / Presentation | 界面与表现，可读 Gameplay 和上游状态 |
| Development / Editor | 白盒、验证器、资源生成；Editor 仅编辑器使用 |

Dialogue 在 Gameplay、UI、Editor、Development 下另有专用程序集。保持现有单向依赖，规则系统不认识具体敌人。

## 启动与敌人链路

Build Settings 当前只启用 `Assets/_Project/Scenes/SampleScene.unity`。敌人六个独立白盒位于 `Scenes/EnemyLabs`，未加入构建。`RuleSystemDriver` 创建共享规则、时间等服务；完整产品关卡启动链未在本次任务验证。

玩家已拆为 PlayerController 编排与 PlayerInput、MotionTuning、ContactSensor、MotionMover 等职能类；身体阻挡必须通过 MotionTuning.BodyBlockLayer 传给 MotionMover，不能只向 PlayerController 加字段。

`EnemyActor` 接线并接收实际伤害 → `EnemyBrain` / 四个 Boss Brain 决策 → `EnemyMotor` 在物理步提交碰撞移动。伤害共用 CombatSystem / DamageProcessor，规则施法共用 RuleReleaseService。EnemyDefinition 是共享配置，运行状态由实例持有。表现层订阅动画、反馈和恢复事件。敌人及投射物通过 ISnapshotParticipant 参加内存快照。

## 约束与风格

- 先读仓库 `CLAUDE.md`，架构与接口文档位于 `Doc/`；四空格、Allman 大括号、命名空间 `RuleGame.<层>.<模块>`。
- 2D 物理使用 Physics2D，位移在 FixedUpdate 中推进；Unity 6 API 为 linearVelocity / Rigidbody2D.Slide。
- 暂停由 TimeManager / EntityStatus 显式控制，不能通过把 timeScale 设为零实现。
- 冷却使用游戏物理时钟，需支持暂停与快照。死亡/过期对象停用以保留回溯能力。
- 每个新 Assets 文件都要配 .meta；修改配置字段保持序列化名称和资源 GUID。
- 动画素材和第三方资源不作为一方代码重构范围。

## 工具与验证

manifest 已有 Coplay Unity MCP，仓库 `.mcp.json` 指向 `localhost:8080/mcp`。本次端口不可用，未暴露可调用的 Unity MCP 工具；未安装或替换提供方。

本机现已安装项目要求的 Unity `6000.6.4f1`。本次在 `/tmp/rule-bat-flight-20261015` 中复制相关一方运行源码、原版 UnityHFSM 2.3.0、真实蝙蝠定义/动画及精灵，使用同一版本运行隔离 Play Mode。输入程序集复制自当前工程 Library，未修改项目包依赖、项目设置或主角动画资源。

- `BatFlightValidation.RunBatch`：21 项通过，检查真实位移、第一阶段俯冲、预警与锁定方向、恢复、撤离、边界、暂停、快照，以及 DashWindup 的四帧配置、实际精灵播放与休息时实际上升、高空往返、停火、边界和暂停。
- `BossCombatValidation.RunBatch`：119 项通过，包含阶段节奏、破韧打断、俯冲方向快照与短展翅动作断言，以及四种 Boss 的休息窗口、暂停、快照、反击受伤和指针停火。
- `BossBodyCollisionValidation.RunBatch`：9 项通过，覆盖新版玩家移动模块的身体阻挡、冲刺、反向脱离、迎面、接触伤害和触发器。
- 当前完整 `Test_Enemy_BatBoss` 白盒已进入 Play 观察到一阶段 Recover/Fly、飞行与攻击特效，主角无敌关闭并正常掉血；测试后退出 Play。Editor.log 未发现 C# 编译错误。未执行 Player 构建或完整难度平衡验收。

已修复一次表现回归：将完整 23 帧展翅压入 0.5 秒会产生约 46 帧/秒的快速拍翼。独立 DashWindup 仅取前四帧，完整 WingSpread 施法序列不改；生成工具也同步生成短序列，避免重建后复发。验证期间出现与此前相同的 UnityEditor.Search.SearchDatabase 索引异常，以及隔离靶标未绑定死亡广播的警告，未改生产逻辑来掩盖它们。

日志位于 `/tmp/rule-bat-windup-validation.log`、`/tmp/rule-bat-windup-brains.log`、`/tmp/rule-bat-body-validation.log`，各验证器的计数结果保存在隔离工程 `Logs/`。`git diff --check` 已通过；未改动同事的 Hero 图集、动画剪辑和 Animator Controller。

2026-10-16 增加统一 `Rest`：一套攻击后不再出招，`BossRestDuration` 地面默认 1.8 秒、蝙蝠默认 3.2 秒，均不随阶段缩短。发条默认两轮指针后休息，由 `ClockBeamVolleysBeforeRest` 调整；指针时钟在休息中冻结。兔子每轮投掷后休息，蝙蝠攻击后升至场地高度的 80%，围绕休息起点左右各 2.5 米往返，速度为基础巡航的 0.85 倍；不追踪玩家、不出招。高度、距离和速度均在 Inspector 配置，巡飞端点与方向参加快照。破韧恢复继续剩余休息；阶段和死亡优先。同时关闭飞行实体的 Slide 贴地吸附，修复落地后有上升速度却无法升空的问题。地面实体的吸附行为不变。生成工具同步配置。新增验证日志为 `/tmp/rule-bat-rest-patrol-combat.log`、`/tmp/rule-bat-rest-patrol-flight.log` 和 `/tmp/rule-bat-rest-patrol-body.log`。

2026-10-16 三种地面 Boss 重做战术：积木新增 Advance 压近与分裂后有限连招、起手锁定投掷；发条改为瞄准/错位/分叉激光，预警站定，响铃锁定短冲撞，受阻或到边界后休息；兔子近身藏身撤退、墙角高抛，快弹/高抛交替，翻转后有限散射。新参数在 EnemyDefinition 对应三个分组。兔子素材仍为空，藏身用半透明占位表现，不加无敌或穿人。遵照本轮要求只运行 `BossCombatValidation.RunTacticsBatch` 做必要的定向验证，28 项通过且编译通过，不重跑前述历史完整套件。日志 `/tmp/rule-three-boss-tactics.log`，计数 `Logs/BossTacticsValidation.txt`。前述 119/21/9 是战术重做前的历史结果。

2026-10-16 蝙蝠新增 Approach：高空休息结束后主动降至玩家侧面，近身优先连斩；近战后轮换远程或阶段招式，不再一直停留在高处，也不再刚接近就先撤离。距离/高度/尝试时长/速度均可调，近战偏好参加快照。EnemyDefinitionEditor 将全部 65 个参数、枚举选项和动作时长列表中文化，保留原字段名、Undo、多选与数值范围。本轮只运行蝙蝠相关的 22 项物理/行为检查并通过编译，未重复运行三 Boss 或全量验证。日志 `/tmp/rule-bat-approach-inspector.log`。用户同期在本地调整了三份 Definition、两份 Boss Prefab、字体和项目设置，未纳入本轮代码提交。

2026-10-16 修复快照异常与震屏后镜头停住：UnityHFSM 2.3.0 的子状态机退出时会清空 activeState，阶段过渡/死亡期间不应读取 Actions.ActiveStateName；EnemyBrain.Capture 现在仅在 Root 为 Alive 时记录动作，恢复逻辑继续按 Root 分支处理。BossCameraImpulse 结束渲染/停用时改为 ResetWorldToCameraMatrix，归还随 Transform 更新的自动视图计算；写回旧矩阵仍会保留手动模式。隔离 Play Mode 先复现四种 Boss 过渡/死亡采集异常，以及震屏收尾/停用后的视图冻结，共 10 个失败用例；同组修复后 36 项断言通过、退出码 0。验证入口 BossCombatValidation.RunSnapshotCameraBatch，日志 /tmp/rule-snapshot-camera-{baseline,fixed}.log，结果 Logs/SnapshotCameraValidation.txt。摄像机通过调用实际渲染回调并检查移动后的视图矩阵验证，未运行完整白盒画面验收或全量战斗套件。

2026-10-16 排查时钟冲撞起点跳变：用户本地 ClockBoss_Definition 已设 Time × Revert，JumpShake 完成后 CastRule 会同步回溯整个世界，包括施法者的位置、Brain 动作与锁定方向；旧回调随后无条件 Go(Charge) 会覆盖恢复结果，甚至使用快照中尚未锁定的零方向。ClockBossBrain 增加不随时间回退的恢复修订计数，施法期间发生恢复就结束旧回调，按恢复后的动作继续决策；未改变时间反转语义或用户资产配置。使用真实 TimeRevert 规则、费用服务和 SnapshotSystem，先复现旧位置 Idle 被强制替换为 Charge，修复后 RunClockRewindBatch 的 8 项检查通过，包含成功回溯/扣费、恢复动作、重新响铃选向，以及缓冲不足时从当时位置正常冲撞/不扣费。日志 /tmp/rule-clock-rewind-{baseline,fixed}.log。仅做这组定向验证，未重跑完整战斗套件。

2026-10-16 时钟按三阶段重整独立指针：一阶段时针，二阶段加分针，击破既有弱点窗口后第三阶段加秒针。旧实现计时被身体动作/其他激光预警阻塞，显示另用全局时钟且先显示秒针占位；现在各针自己倒计时、只在自身出手时重置，解锁时从完整周期起算，显示直接读取 Brain 的对应进度。休息和阶段过渡统一冻结，身体忙时到期只保留一次待发，不积压补射。沿用 ClockHourInterval=12，新增 ClockMinuteInterval=6 与 ClockSecondInterval=3，Inspector 中文化，既有资源槽位顺序保持不变。RunClockHandsBatch 在 Unity 6000.6.4f1 隔离 Play Mode 中 30 项通过（含上一轮真实时间反转的 8 项），覆盖三阶段解锁/实际资源槽位、独立出手与预警不互阻、显示角度/暂停/快照、身体动作计时、休息和弱点失败路线；只运行该定向组，未重跑其他 Boss 或全量战斗。日志 /tmp/rule-clock-hands.log，结果 Logs/ClockHandsValidation.txt。同期其他任务正在修改兔子素材与相关源码，本次只提交时钟相关内容；EnemyLabBuilder 仅取三项周期默认值的独立改动。

2026-10-16 用户再次反馈乱跑瞬移，最终取消时钟 JumpShake 完成时的隐式 CastRule，直接从当前位置进入 Charge。此前恢复修订计数只防止旧回调覆盖恢复结果，并未去掉起跑前的全局回溯，不能满足乱跑起点连续的要求；该计数已删除。保留 Definition 的用户配置和显式施法语义，不修改 TimeRule/SnapshotSystem 或为施法者添加免疫。RunClockRewindBatch 的定向用例改为：有/无历史缓冲时起点一致、不扣施法能量、锁向正确、实际物理步连续移动、缓冲未被消耗，以及显式反转仍回溯/扣费。修复前复现有缓冲时回到历史位置；修复后 Unity 6000.6.4f1 隔离 Play Mode 中 14 项通过，日志 /tmp/rule-clock-charge-fixed.log，计数 Logs/ClockChargeValidation.txt。未重跑独立指针或全量战斗检查。

2026-10-16 接入补交的兔子 111 张 PNG：正式 RabbitBoss Prefab、18 个动作/辅助序列和三类双帧投掷物，原画按 8 fps 播放。固定画布脚底锚点保留跳跃，藏身改为空帽跳跃；Flip 先缩回再反转，二阶段持续显示倒置帽子，Dead 完整播放后停用。投掷间隔延长时，弹体仍在原画第 12 帧附近出手。三类白底投掷物仅生成透明裁切运行时副本，交付原件和身体 PNG 字节不变。新增 import_rabbit.py 与保留参数的兔子单独导入菜单，通用资源/特效重建保留兔子接线。使用真实 Prefab、动画与组件的隔离 Play Mode 定向验证 58 项通过、退出码 0，覆盖播放、出手、自定义间隔、藏身、阶段、暂停、池复用、快照和死亡；原工程兔子白盒也已进入 Play 观察到冒出、施法与翻转起手。日志 /tmp/rule-rabbit-animation-validation.log，结果 Project-Rule/Logs/RabbitAnimationValidation.txt。隔离工程出现既有 UnityEditor.Search.SearchDatabase 索引异常；无新增 C# 编译错误，未执行 Player 构建或全套战斗平衡验证。

2026-10-16 在兔子交付资源基础上强化 AI：目标超射程时持续追近；投掷先跟踪、出手前 0.35 秒锁定，按距离和高度选弹珠平射/跳棋高抛。藏帽只沿锁定路线向外撤退，墙角无空间就反击；冒出动画结束后保留完整撤退冷却，避免反复藏身。翻转后由实例 Brain.IsFlying 驱动 EnemyMotor 实际升空换侧，不改共享 Definition；用鸭子打两轮三发交错扇形，结束后悬停休息且仍可受伤。飞行路线与弹道选择参加快照，回溯到第一阶段恢复地面模式。新增六项中文 Inspector 参数。用户提供路径的 111 张运行时帧已核对来源哈希，身体原件未改，其它角色清单未改。本轮只运行 RunRabbitTacticsBatch：Unity 6000.6.4f1 独立副本 /tmp/rule-rabbit-ai-20261016 中 23 项通过，含真实物理升空、实际精灵选择、平射/高抛、墙角反击、撤退冷却、扇形角度、休息/暂停/快照；日志 /tmp/rule-rabbit-ai.log。前述 58 项资源播放检查为接入时的历史结果，本轮未重跑。同期时钟发射器等改动不纳入本次提交。

2026-10-16 用户补充微信目录中的“敌人逻辑”，并明确“不必完全参考”。原始文本与八张 drawio 已保存到 Doc/References/EnemyLogic，作为可调整的角色/阶段/招式参考；本轮兔子主动换位、实际飞行、有限散射和休息节奏保留，不回退为原图的机械循环。原文本普通投掷写 0.5 秒、行为图写 2 秒；当前沿用交付 16 帧、8 fps 的 2 秒投掷动作及其原画出手点。该轮仅补充参考与约束说明，没有修改已经通过 23 项检查的运行代码。

2026-10-16 修复兔子投掷物提前砸地：下落速度外推会把锁定点推入地板；原高抛补偿随后被固定总速度归一化，在增强重力下射程不足。仅兔子改为按目标碰撞体中心瞄准，用身体盒扫掠限制预判点不穿地形；按飞行时间保留完整初速度，近距离高抛保留至少 0.65 秒飞行时间。出手时机、锁定窗口与其他敌人弹道不变。新增 RabbitProjectileValidation，Unity 6000.6.4f1 隔离 Play Mode 基线 7 项中 3 项失败（正反向下落、三倍重力）；修复后 7 项通过，并检查弹池复用刚体位置。兔子原有 RunRabbitTacticsBatch 的 23 项也通过。日志 /tmp/rule-rabbit-projectile-before.log、/tmp/rule-rabbit-projectile-after.log、/tmp/rule-rabbit-projectile-tactics.log；未跑其他 Boss 或全量构建。

2026-10-16 用户反馈仍向脚底投掷、倒立未实际起飞。本轮补齐上一轮未覆盖的近身与倒立进攻条件：翻转演出开始即启用实例飞行并垂直离地；空中换位超时不能越过身体离地间距和侧向站位检查，玩家钻到正下方时先重新换位。地面贴身时重新站位，墙角反击落点至少越过自己的身体。兔子使用身体前方的独立出手点，弹道按该点计算；可选 origin 对其他敌人的原有调用不产生变化。中文 Inspector 将换侧时长说明为参考时长。新增三条回归在旧实现均失败，修复后 RabbitProjectileValidation 共 10 项通过，含实际 RabbitBoss prefab 物理：翻转中已上升，首轮 body=(0.87,3.00)、出手点=(2.12,2.70)、目标=(6,0.65)。RunRabbitTacticsBatch 23 项通过，原正下方攻击用例改为有效侧向站位；墙角反击、休息、快照保留。日志 /tmp/rule-rabbit-takeoff-before.log、/tmp/rule-rabbit-takeoff-after.log、/tmp/rule-rabbit-takeoff-tactics.log。未跑其他 Boss 或全量构建。

## 证据

`CLAUDE.md`、`Doc/系统模块架构.md`、`Doc/敌人系统接入.md`；工程内 `Packages/manifest.json`、`ProjectSettings/{ProjectVersion.txt,QualitySettings.asset,GraphicsSettings.asset,EditorBuildSettings.asset,ProjectSettings.asset}`；一方 `.asmdef`；`Scripts/Gameplay/Enemy/*.cs`、`Gameplay/Player/EntityStatus.cs`、`PlayerController.cs`、`RuleReleaseService.cs`、`Scripts/Editor/Enemy/EnemyLabBuilder.cs`、`Scripts/Rules/RuleSystemDriver.cs`。

<!-- unity-onboarding:generated:end -->
