# BetterBTD Script File Format

当前脚本文件使用 JSON 文档结构，顶层固定为 5 个部分：

- `schema`
  - 固定值 `better-btd/script`
  - 用于识别 BetterBTD 脚本文件
- `formatVersion`
  - 当前格式版本为 `1`
- `metadata`
  - 脚本基础元数据
- `monkeyObjects`
  - 编辑器维护的猴子对象快照
- `instructions`
  - 指令序列

## Metadata

`metadata` 当前字段如下：

- `scriptVersion`
  - 脚本自身版本，默认 `1.0.0`
- `description`
  - 脚本说明
- `map`
  - 地图枚举名，例如 `MonkeyMeadow`
- `difficulty`
  - 难度枚举名，例如 `Medium`
- `mode`
  - 模式枚举名，例如 `Standard`
- `hero`
  - 英雄枚举名，例如 `Quincy`
- `tags`
  - 标签列表，既支持内建标签，也支持用户自定义标签

说明：

- `category` 和 `name` 已废弃，不再属于脚本文件格式。
- 脚本在“我的脚本”页中的显示名、导入来源、任务槽位绑定，属于脚本库管理信息，不写回单个脚本文件。
- 当前实现按最新格式工作，不再为旧字段增加兼容逻辑。

## Monkey Objects

`monkeyObjects` 是编辑器内部对象图的持久化快照。每个对象包含：

- `bindingId`
  - 编辑器内部稳定引用 ID
- `objectId`
  - 展示和追踪使用的对象键，例如 `DartMonkey:1`
- `selectionCode`
  - 放置指令对应的选择值，例如 `Tower:DartMonkey`
- `placementOrder`
  - 放置顺序

这一层的目的是让脚本在回读后仍能稳定恢复对象引用关系。

## Instructions

`instructions` 采用统一指令 DTO 设计：

- 每条指令都包含 `commandType`
- 其余字段为参数超集，未使用字段保持默认值
- 目标猴子类指令通过 `targetMonkeyBindingId` 关联 `monkeyObjects`
- 放置指令通过 `monkeyBindingId` 和 `monkeyObjectId` 维护对象身份

当前会持久化的关键字段包括：

- 猴子相关：`selectedMonkeyTower`、`monkeyBindingId`、`monkeyObjectId`、`targetMonkeyBindingId`、`targetMonkeyObjectId`
- 行为参数：`upgradePath`、`upgradeCount`、`switchDirection`、`switchCount`、`selectedAbility`、`clickCount`
- 资源和技能：`selectedInventoryItem`、`selectedActivatedAbility`
- 节奏控制：`nextRoundAction`、`nextRoundSendCount`、`waitMode`、`clickIntervalMilliseconds`
- 等待参数：`waitTimeMilliseconds`、`waitGoldAmount`、`waitRoundCount`
- 坐标参数：`positionX`、`positionY`、`abilityCoordinateX`、`abilityCoordinateY`
- 颜色等待：`waitColorCoordinateX`、`waitColorCoordinateY`、`waitColorHex`、`waitColorTolerance`
- 附加信息：`commentContent`、`intervalToNextInstructionMs`、`notes`

## Example

```json
{
  "schema": "better-btd/script",
  "formatVersion": 1,
  "metadata": {
    "scriptVersion": "1.0.0",
    "description": "Early game farm route",
    "map": "MonkeyMeadow",
    "difficulty": "Medium",
    "mode": "Standard",
    "hero": "Quincy",
    "tags": [
      "collection",
      "custom-route"
    ]
  },
  "monkeyObjects": [
    {
      "bindingId": "f35513d719f04bb0a17ccfd8f4c57077",
      "objectId": "DartMonkey:1",
      "selectionCode": "Tower:DartMonkey",
      "placementOrder": 1
    }
  ],
  "instructions": [
    {
      "commandType": "PlaceMonkey",
      "selectedMonkeyTower": "Tower:DartMonkey",
      "monkeyBindingId": "f35513d719f04bb0a17ccfd8f4c57077",
      "monkeyObjectId": "DartMonkey:1",
      "positionX": 412.5,
      "positionY": 276.0,
      "intervalToNextInstructionMs": 100
    },
    {
      "commandType": "UpgradeMonkey",
      "targetMonkeyBindingId": "f35513d719f04bb0a17ccfd8f4c57077",
      "upgradePath": "Top",
      "upgradeCount": 2,
      "intervalToNextInstructionMs": 100
    }
  ]
}
```

## Implementation Notes

- 持久化模型位于 `BetterBTD/Models/ScriptEditor/ScriptDocumentModels.cs`
- JSON 读写位于 `BetterBTD/Services/MyScripts/ScriptDocumentService.cs`
- 编辑器状态和脚本文档互转位于 `BetterBTD/ViewModels/ScriptEditorPageViewModel.cs`
- “我的脚本”受管资源库位于 `LocalAppData/BetterBTD/MyScripts`
