# 新增地图维护工作流

这份文档记录 BetterBTD 随 BTD6 更新新增地图时的通用修改流程。目标是让地图能进入脚本元数据、地图选择 UI、自动任务槽位和 OCR 模板识别链路，同时避免破坏既有脚本和配置。

## 总体原则

- 新地图的主标识使用 `GameMapType` 枚举名。
- 模板资源文件名必须和 `GameMapType` 枚举名一致。
- 地图分组、显示顺序和任务范围以 `GameElementCatalog.Maps` 为准。
- 新脚本格式保存地图枚举名字符串，不依赖 `GameMapType` 的具体数值。
- 不要重排现有 `GameMapType` 成员。即使主链路按名称工作，仍可能存在外部配置、调试数据或历史文件用数字保存枚举值。
- 旧 `.btd6` 导入链路使用 `LegacyMapType` 的显式数值。只有明确需要继续兼容旧格式，并且已经知道对应旧格式 ID 时，才更新 `LegacyMapType`。

## 需要准备的信息

新增地图前先确认：

- 地图英文枚举名，例如 `SkullTweak`。
- 地图英文显示名，例如 `Skull Tweak`。
- 地图中文显示名，例如 `骷髅改`。
- 地图难度分组：`Beginner`、`Intermediate`、`Advanced` 或 `Expert`。
- 1080p OCR 模板图片，通常来自游戏地图卡片截图裁剪。
- 是否需要兼容旧 `.btd6` 数字地图 ID。默认不需要。

## 修改文件

### 1. 加入模板资源

把模板图片放到：

```text
BetterBTD/Assets/OcrIcons/Maps/1080p/{GameMapType}.png
```

示例：

```text
BetterBTD/Assets/OcrIcons/Maps/1080p/SkullTweak.png
```

`BetterBTD.csproj` 已经通过下面的通配规则复制 OCR 图标资源，一般不需要额外修改项目文件：

```xml
<Content Include="Assets\OcrIcons\**\*.png">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

加载逻辑在 `IconTemplateRepository.LoadMapTemplates` 中，会遍历 `Enum.GetValues<GameMapType>()` 并按 `{map}.png` 查找文件。文件名不匹配时不会加载模板。

### 2. 新增 `GameMapType`

文件：

```text
BetterBTD/Models/GameElements/MapDefinitions.cs
```

在 `GameMapType` 中追加新成员。

推荐：

```csharp
TrickyTracks,
SkullTweak
```

不要把新成员插入到现有枚举中间，除非先把所有既有成员改成显式数值并验证兼容性。当前主链路按名称工作，但追加是最小风险做法。

### 3. 更新地图目录

文件：

```text
BetterBTD/Models/GameElements/GameElementCatalog.cs
```

在 `GameElementCatalog.Maps` 的对应难度分组中加入 `MapDefinition`。

示例：

```csharp
new(GameMapType.SkullTweak, MapDifficultyTier.Beginner, "GameElements.Map.SkullTweak"),
```

这一处会影响：

- 脚本编辑器地图下拉。
- 我的脚本页面地图筛选。
- 黑框任务地图范围。
- 金气球新手图候选范围。
- 收集任务专家图候选范围。
- 托管脚本槽位生成。

### 4. 更新本地化

文件：

```text
BetterBTD/Services/Shell/Localization/LocalizationService.Resources.GameElements.cs
```

分别在中文和英文资源字典中加入地图显示名。

示例：

```csharp
["GameElements.Map.SkullTweak"] = "骷髅改",
["GameElements.Map.SkullTweak"] = "Skull Tweak",
```

注意：同一个 key 会分别出现在 `BuildZhCnGameElementsResources` 和 `BuildEnUsGameElementsResources` 中，不要只加一处。

### 5. 判断是否更新旧格式枚举

文件：

```text
BetterBTD/Models/ScriptEditor/LegacyScriptModels.cs
```

`LegacyMapType` 只用于旧 `.btd6` 导入转换。它依赖显式数字 ID。

默认不更新。

只有同时满足下面条件时才更新：

- 项目仍要求导入包含该新地图的旧 `.btd6` 文件。
- 已经确认该地图在旧格式中的准确数字 ID。

不要猜 ID。错误的数字映射会让旧脚本导入到错误地图，比不支持更难排查。

## 调用点检查清单

新增地图后用 `rg` 检查是否存在硬编码地图列表：

```powershell
rg -n "GameMapType|GameElementCatalog\.Maps|MapDifficultyTier|CreateGoldBalloonSlotId|TryLocateBestMap" BetterBTD BetterBTD.Tests
```

重点确认：

- `GameUiStateService` 的金气球候选来自 `GameElementCatalog.Maps.Where(Tier == Beginner)`。
- `ManagedScriptSlotCatalogService` 的金气球槽位来自 `GameElementCatalog.Maps.Where(Tier == Beginner)`。
- `GameElementCascadingItems` 的地图选择项来自 `GameElementCatalog.Maps`。
- `IconTemplateRepository.LoadMapTemplates` 按 `GameMapType` 和模板文件名自动加载。
- 测试中没有写死旧的地图数量。

如果新地图是专家图，还要确认收集任务候选和订阅槽位是否符合预期。

## 测试建议

至少补一条测试覆盖目录和任务槽位：

```csharp
Assert.Contains(GameElementCatalog.Maps, x =>
    x.Type == GameMapType.SkullTweak &&
    x.Tier == MapDifficultyTier.Beginner);

Assert.Contains(slots, x =>
    x.SlotId == ManagedScriptSlotIdFactory.CreateGoldBalloonSlotId(GameMapType.SkullTweak));
```

如果新地图属于专家图，改为验证收集任务相关槽位。

资源层面可以人工确认文件存在：

```powershell
Get-Item BetterBTD\Assets\OcrIcons\Maps\1080p\SkullTweak.png
```

## 验证流程

完成修改后运行：

```powershell
dotnet test BetterBTD.Tests\BetterBTD.Tests.csproj
```

再检查差异：

```powershell
git status --short
git diff
```

预期差异通常只包括：

- `MapDefinitions.cs`
- `GameElementCatalog.cs`
- `LocalizationService.Resources.GameElements.cs`
- 一张地图模板图片
- 对应测试文件

如果出现大量无关本地化、格式化或项目文件变更，先确认是否由编码、换行或格式化工具造成，不要混入新增地图提交。

## Skull Tweak 示例

本次新增 “骷髅改 / Skull Tweak” 的最小改动为：

- `GameMapType.SkullTweak` 追加到主地图枚举末尾。
- `GameElementCatalog.Maps` 中加入 Beginner 分组。
- 增加 `GameElements.Map.SkullTweak` 的中英文显示名。
- 复制模板到 `Assets/OcrIcons/Maps/1080p/SkullTweak.png`。
- 不更新 `LegacyMapType`，因为旧脚本不再维护新地图 ID。
- 增加测试确认 Skull Tweak 是新手图，并拥有金气球槽位。
