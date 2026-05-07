# BetterBTD Script File Format

脚本文件当前采用 JSON 文档结构，顶层固定为 5 个部分：

- `schema`: 固定值 `better-btd/script`，用于识别 BetterBTD 脚本文件。
- `formatVersion`: 文件格式版本，当前为 `1`，用于后续迁移。
- `metadata`: 脚本元数据，保存脚本版本、分类、地图、难度、模式、英雄等信息。
- `monkeyObjects`: 编辑器维护的猴子对象快照，保存放置指令对应的绑定关系。
- `instructions`: 指令序列，按执行顺序保存每条指令的完整参数。

## Metadata

`metadata` 当前字段如下：

- `scriptVersion`: 脚本自身版本，默认 `1.0.0`。
- `category`: 脚本分类，当前约定值包括 `Collection`、`BlackBorder`、`Race`、`Custom`。
- `name`: 脚本名称。
- `description`: 脚本说明。
- `map`: 地图枚举名，例如 `MonkeyMeadow`。
- `difficulty`: 难度枚举名，例如 `Medium`。
- `mode`: 模式枚举名，例如 `Standard`。
- `hero`: 英雄枚举名，例如 `Quincy`。

## Monkey Objects

`monkeyObjects` 是对编辑器内部猴子对象表的持久化快照，每个对象包含：

- `bindingId`: 编辑器内部稳定引用 ID，其他指令通过它关联目标猴子。
- `objectId`: 展示和追踪使用的对象键，例如 `DartMonkey:1` 或 `Hero:Quincy`。
- `selectionCode`: 放置指令选择值，例如 `Tower:DartMonkey` 或 `Hero:Quincy`。
- `placementOrder`: 放置顺序，便于重建和排查问题。

这层数据和 `PlaceMonkey` 指令中的字段有冗余，目的是保证：

- 回读时可以稳定恢复对象引用。
- 后续对象生成逻辑调整时仍保留可迁移依据。
- 手动排查脚本时可以直接看到当前对象图。

## Instructions

`instructions` 采用“统一指令 DTO”设计：

- 每条指令都包含 `commandType`。
- 其余字段为参数超集，未使用字段保留默认值。
- 目标猴子类指令通过 `targetMonkeyBindingId` 关联 `monkeyObjects`。
- 放置指令通过 `monkeyBindingId` 和 `monkeyObjectId` 维护对象身份。

当前会持久化的关键字段包括：

- 猴子相关：`selectedMonkeyTower`、`monkeyBindingId`、`monkeyObjectId`、`targetMonkeyBindingId`、`targetMonkeyObjectId`
- 行为参数：`upgradePath`、`upgradeCount`、`switchDirection`、`switchCount`、`selectedAbility`、`clickCount`
- 资源/技能：`selectedInventoryItem`、`selectedActivatedAbility`
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
    "category": "Collection",
    "name": "Monkey Meadow Standard Quincy",
    "description": "Early game farm route",
    "map": "MonkeyMeadow",
    "difficulty": "Medium",
    "mode": "Standard",
    "hero": "Quincy"
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
    },
    {
      "commandType": "MouseClick",
      "positionX": 620.0,
      "positionY": 148.0,
      "clickCount": 2,
      "clickIntervalMilliseconds": 80,
      "intervalToNextInstructionMs": 100
    }
  ]
}
```

## Implementation Notes

- 持久化模型位于 `BetterBTD/Models/ScriptEditor/ScriptDocumentModels.cs`。
- JSON 读写和格式校验位于 `BetterBTD/Services/ScriptDocumentService.cs`。
- 编辑器状态和脚本文档互转位于 `BetterBTD/ViewModels/ScriptEditorPageViewModel.cs`。
