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

## 证据

`CLAUDE.md`、`Doc/系统模块架构.md`、`Doc/敌人系统接入.md`；工程内 `Packages/manifest.json`、`ProjectSettings/{ProjectVersion.txt,QualitySettings.asset,GraphicsSettings.asset,EditorBuildSettings.asset,ProjectSettings.asset}`；一方 `.asmdef`；`Scripts/Gameplay/Enemy/*.cs`、`Gameplay/Player/EntityStatus.cs`、`PlayerController.cs`、`RuleReleaseService.cs`、`Scripts/Editor/Enemy/EnemyLabBuilder.cs`、`Scripts/Rules/RuleSystemDriver.cs`。

<!-- unity-onboarding:generated:end -->
