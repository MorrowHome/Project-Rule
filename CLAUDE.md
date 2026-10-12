# CLAUDE.md — 《规则游戏》项目说明

> 本文件是**团队共享**的项目说明，随 git 提交，所有协作者（及其 Claude）都会读到。
> 个人笔记请放 **`CLAUDE.local.md`**（已在 `.gitignore` 中，队友看不到）。

---

## 项目是什么

**《规则游戏》** —— 2026 年 26TapTap聚光灯 GameJam 参赛作品，活动主题「**涌现（Emergence）**」。
单机、2D 横版、关卡制**平台动作解谜**，单人游玩，单关约 10 分钟。
**开发周期 2026-10-01 ~ 10-21**；硬性约束：禁止联网、禁止广告、必须上架 TapTap。

核心玩法：玩家受与 NPC **相同的物理约束**，唯一特权是「**暂停游戏、用『属性 × 关系』词条拼规则**」。
规则一经拼出，在一定时间内对整个世界（玩家 / 敌人 / 子弹 / 石头）生效——改变的是**世界的物理法则**，
而不是玩家的操作手感。玩家与同样持有规则之力的敌人争夺规则主导权。

> 🔑 核心判据：**乐趣来自「世界变了」，不是「手不听话了」**。

## 引擎与环境

| 项 | 值 |
| --- | --- |
| Unity | **6000.6.4f1**（2D + URP） |
| 语言 | C# |
| 输入 | 新版 Input System（`Assets/_Project/Settings/InputSystem_Actions.inputactions`） |
| 工程位置 | `Project-Rule/`（在仓库根之下） |
| Unity MCP | 仓库根 `.mcp.json`，HTTP → `http://localhost:8080/mcp` |

## 设计文档（权威来源）

| 文档 | 内容 |
| --- | --- |
| `Doc/规则游戏_策划案_v3.md` | 正式策划案（玩法 / 规则系统 / 敌人 / 关卡 / 叙事 / 存档） |
| `Doc/系统模块架构.md` | 系统与模块级架构（9 个系统、四层） |
| `Doc/接口与事件定义.md` | **v3.0 接口冻结文档**（C# 接口签名、事件清单、依赖速查） |
| `Doc/工程规范与目录结构方案.md` | 目录结构、asmdef、命名规范、四人分工 |
| `Doc/规则矩阵.json` | 30 个规则格位（6 属性 × 5 关系）+ 未定稿（`undecided`） |

## 架构：四层 + 单向依赖

依赖方向 **Gameplay → Core → Infrastructure**，下层不认识上层。

| 程序集 | 目录 | 允许依赖 |
| --- | --- | --- |
| `RuleGame.Infrastructure` | `Scripts/Infrastructure/` | **无** |
| `RuleGame.Core` | `Scripts/Rules/` | Infrastructure |
| `RuleGame.Gameplay` | `Scripts/Gameplay/`（Combat / Enemy / Player / Scene） | Core, Infrastructure, Unity.InputSystem |
| `RuleGame.Persistence` | `Scripts/Persistence/` | Core, Infrastructure |
| `RuleGame.UI` | `Scripts/UI/` | 全部 |
| `RuleGame.Presentation` | `Scripts/Presentation/` | 全部 |
| `RuleGame.Editor` | `Scripts/Editor/` | 全部（仅编辑器） |
| `RuleGame.Development` | `Scripts/Development/` | demo / 白盒代码，与生产代码隔离 |

**铁律**：规则系统**不认识**「玩家 / 敌人」等具体实体；基础设施零依赖；战斗系统住在规则系统**下游，只读不写**。

## 代码规范（详见 `Doc/工程规范与目录结构方案.md`）

- 缩进 4 空格、**不用 Tab**；大括号 Allman（另起一行）
- 一个文件一个主要类型，**文件名 = 类型名**
- 命名空间 `RuleGame.<层>.<模块>`，如 `RuleGame.Core.Rules`
- 类 / 方法 PascalCase；私有字段 `_camelCase`；常量 PascalCase；事件 `On` 开头
- 资源命名用**后缀不用前缀**：`Hero_Run_01.png`、`GravityRevert_Rule.asset`、`Level_01.unity`
- **不用空格、不用中文文件名**；不用 `New` / `Copy` / `Final`
- 每个新文件都要配 `.meta`（Unity 靠 GUID 引用资源，漏了会重新生成 GUID 导致引用丢失）

## 不能踩的实现约束

- 2D 用 `Physics2D.*`，**不是** `Physics.*`（两套独立系统）
- 物理代码写在 `FixedUpdate()`，不是 `Update()`
- Unity 6 的速度 API 是 `Rigidbody2D.linearVelocity`（旧 `velocity` 已弃用）
- **规则系统不做特例判断**——免疫写在实体自己的物理属性里（约束 C4）
- **绝不序列化** Transform / Rigidbody / 物理状态（约束 C5）；只持久化「已解锁词条 + 激活历史」这类小数据
- **单一规则表**，后写的规则覆盖先写的
- 暂停**不用** `Time.timeScale = 0`（会瞬移穿碰撞体）

## 四人分工

| | 负责 | 程序集 |
| --- | --- | --- |
| **P1** | 基础设施 + 存档（时间管理器、快照系统、关卡流程、对象注册表、存档读写） | Infrastructure · Persistence |
| **P2** | 规则系统（规则维护表、属性管理器、属性规则类基类 + 6 个属性规则类） | Core |
| **P3** | 玩家 + 战斗（移动、状态含能量、规则释放入口、伤害结算、命中判定） | Gameplay |
| **P4** | 敌人 + 场景叙事 + UI | Gameplay · UI |

## 当前进度（截至 2026-10-12）

- ✅ 规则系统核心（`RuleSystem` / `RuleTable` / `AttributeManager`），重力属性端到端跑通
- ✅ 规则接口层下沉到 `Infrastructure/Shared/Rules/`（v3.0 冻结）
- ✅ P3：`IEntityStatus` / `EntityStatus`、`RuleReleaseService`、`DamageRule`、`DamageInfo` / `IDamageable`、`CombatSystem`、`PlayerController`、`EntityDamageable` / `IKnockbackReceiver`
- ✅ P4：白盒 HUD、规则拼装 demo、五种重力接入（`Scenes/Level_01.unity`）
- ⚠️ **P3 的组件还没有接进任何场景**（`Level_01.unity` 目前只有 P4 的 demo 控制器）。
  想验证玩法得先接线，接线前请注意下面两条
- ✅ `RuleCatalog.asset` 已接 **30 条**规则 —— **规则矩阵 6 属性 × 5 关系全部可达**。
  6 个属性规则类都已实现（重力 / 时间 / 摩擦 / 质量 / 伤害 / 因果）
- ✅ **伤害处理模块**（`Gameplay/Combat/DamageProcessor`）：全游戏唯一的伤害出口，
  读 `IAttributeManager` 不读 `IRuleSystem`。含伤害缩放/反转/失效、因果（作用对象转换 /
  随机倍率 / 随机对象 / 扩散）、以及**伤害挂账**的逐实体净额账本
- ⚠️ **阵营只有两个值**（`Faction.Player` / `Faction.Enemy`，后者是默认值）：
  玩家独一个阵营、没有友方，其余可攻击实体全是敌人。挂在 `EntityDamageable` 上。
  **场景里需要把玩家对象的 `_faction` 手动设成 `Player`**，其余不用动
- ⚠️ `CombatSystem` 的职责已收窄成**命中判定 + 事件宿主**；
  `ApplyDamage` 收的是**基础值**（不是最终值），一样要过伤害处理模块
- ⚠️ `PlayerController` 按 D1 用 **Kinematic 刚体**：位移走 `Rigidbody2D.Slide` 解算接触，
  重力自己读 `Physics2D.gravity` 积分。`MovePosition` 自身不做碰撞，**不要绕开 Slide 直接移动**
- ⚠️ **暂停不能用 `Time.timeScale = 0`**（策划案 §3.1）。冷却等按帧推进的状态改由
  `EntityStatus.SetPaused` 显式冻结 —— 架构文档 D12 的旧说法「暂停自动冻结」已作废
- ⚠️ 「关系」属性已**彻底删除**：`AttributeId` 6 个成员、`RuleTable.SlotCount = 6`。
  架构 D5 已被 D17 取代，规则矩阵已同步为 **6 × 5 = 30 格**（见下方「已定设计决策」）

## 已定设计决策（2026-10-12，合并 `mortis-sys/main` 时定）

| # | 决策 | 理由 / 代价 |
| --- | --- | --- |
| 1 | **删掉 `AttributeId.Relation`**，不保留枚举位（6 成员 / `SlotCount = 6`） | 枚举值会序列化进 `RuleDef.asset` 的 `Attribute` 字段，保留占位只是为了兼容旧资产；本次伤害资产随伤害系统重写、且没有需要兼容的存档，所以删除是免费的。**以后再删属性要先查引用，并准备重挂受影响的资产** |
| 2 | 规则类的 `IAttributeManager` 用**构造函数注入**，不用 `Bind()` | 漏注入变成**编译错误**而不是运行时 NRE；代价是每个子类多一行转发构造（用不到属性管理器的 `GravityRule` 也要写） |
| 3 | 暂停 = **两层**：`Physics2D.simulationMode = Script`（停物理步进）+ `RuleSystemDriver.IsPaused`（停规则时钟）。`IsPaused` 只是**过渡期闸门**，等 P1 的 `ITimeManager` 落地后改读它的 delta 并删掉这个布尔量 | `Time.timeScale` 保持 1，`Time.deltaTime` 照常推进，不挡就会照常扣规则时长 / 冷却。单一真相（`ITimeManager`）是终点，重复状态是临时债 |

## 提交约定

- Commit 信息用 Conventional Commits + 中文：`feat(gameplay): ...`、`docs: ...`、`chore: ...`
- 提交前确认带上 `.meta`
