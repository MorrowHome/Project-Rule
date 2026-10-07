# 规则数据资产

这里放两类 ScriptableObject 资产：

| 资产 | 说明 |
|---|---|
| `RuleCatalog` | **一个就够**。把所有 RuleDef 列进它的数组 |
| `RuleDef` | **每个「属性 × 关系」一个**。35 个格位里已定稿的那些 |

## 建资产的顺序

1. 先建所有 `RuleDef`（右键 → Create → 规则游戏 → RuleDef）
2. 再建一个 `RuleCatalog`
3. 把 RuleDef 全部拖进 RuleCatalog 的 `_rules` 数组

## 第一个要建的 RuleDef

跑通端到端只需要一条：

| 字段 | 值 |
|---|---|
| Attribute | Gravity |
| Relation | Revert |
| Duration | 10 |
| ActivationCost | 20 |
| Cooldown | 0 |
| DisplayName | 重力反转 |

> ⚠️ **不要提交空的 RuleDef。** 只建已经定稿的格位（见 `Doc/规则矩阵.json`）。
