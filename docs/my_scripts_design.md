# “我的脚本”框架说明

## 目标

“我的脚本”不是目录扫描器，而是一个**受管脚本资源库**。

它负责两件事：

1. 管理脚本导入、导出、删除和基础筛选
2. 为后续 `AutoTask` 提供脚本槽位绑定

明确约束：

- 不再依赖用户手工维护目录结构
- 不为旧字段和旧格式增加兼容逻辑
- 标签只用于说明用途，不直接等价为任务绑定
- 页面只保留关键操作，不堆叠批量管理能力

## 受管资源库

脚本库统一存放在：

- `%LocalAppData%/BetterBTD/MyScripts`

其中包含：

- `library.json`
  - 受管脚本清单
  - 槽位绑定信息
- `Assets/`
  - 实际脚本资源文件

用户通过“导入脚本”把外部脚本复制进资源库，通过“导出脚本”把资源库脚本导出到外部。

这样做的目的，是把：

- 脚本文件本体
- 脚本显示名
- 来源文件名
- 脚本槽位绑定

统一收口到应用控制的资源清单里，避免用户直接改目录或误删内部文件。

## 当前数据结构

### 1. 脚本文件

脚本文件仍然只保存脚本自身内容：

- `schema`
- `formatVersion`
- `metadata`
- `monkeyObjects`
- `instructions`

脚本文件内部只保留这些元数据：

- `scriptVersion`
- `description`
- `map`
- `difficulty`
- `mode`
- `hero`
- `tags`

### 2. 资源库记录

受管脚本库会额外保存：

- `ScriptId`
- `DisplayName`
- `SourceFileName`
- `StoredFileName`
- `Map`
- `Difficulty`
- `Mode`
- `Hero`
- `Tags`
- `ImportedAt`
- `UpdatedAt`

这里的 `DisplayName` 属于资源库，不属于脚本文件本体。

### 3. 槽位定义

当前框架已经支持以下槽位类型：

- `Custom`
  - `custom/default`
- `Collection`
  - 3 组模式
  - 每组 13 个脚本槽位
  - 目前是占位槽位框架
- `BlackBorder`
  - 所有地图
  - 所有难度
  - 所有对应模式
- `Race`
  - `race/current`

### 4. 槽位绑定

绑定关系单独保存在资源库清单中：

- `SlotId`
- `ScriptId`
- `UpdatedAt`

这保证了：

- 一个脚本可被多个槽位复用
- 删除脚本时可同步清理绑定
- `AutoTask` 可以通过 `SlotId` 稳定找到脚本

## 与 AutoTask 的接入方式

当前默认解析器已经从“未实现”切到“受管脚本解析器”。

解析顺序如下：

1. 如果 `PreferredFilePath` 存在且文件有效，优先直接使用
2. 如果存在 `SlotId` 绑定，按资源库绑定解析
3. 如果是 `BlackBorder / Custom / Race` 且没有显式 `SlotId`，按默认规则推导槽位
4. 如果以上都失败，则返回未配置

当前已接入：

- `Custom`
  - 支持指定路径
  - 同时保留 `custom/default` 槽位
- `BlackBorder`
  - 已生成 `Map + Difficulty + Mode` 的稳定槽位 ID
- `Race`
  - 已支持 `race/current` 槽位
- `Collection`
  - 槽位框架已建好
  - 后续等活动上下文和脚本位定义稳定后，再把策略接入运行时

## 页面结构

“我的脚本”页当前保持最小关键功能，布局分两块：

### 脚本资源

支持：

- 导入脚本
- 导出当前脚本
- 删除当前脚本
- 刷新资源库
- 按名称、标签、地图、难度、模式筛选

列表展示：

- 名称
- 地图
- 难度
- 模式
- 标签
- 绑定数
- 状态

### 任务槽位

支持：

- 按任务类型查看槽位
- 搜索槽位
- 把当前选中的脚本绑定到当前选中的槽位
- 清除当前槽位绑定

列表展示：

- 分组
- 槽位
- 当前脚本
- 状态

## 当前实现文件

核心模型：

- `BetterBTD/Models/MyScripts/ManagedScriptLibraryModels.cs`

核心服务：

- `BetterBTD/Services/MyScripts/ManagedScriptLibraryService.cs`
- `BetterBTD/Services/MyScripts/ManagedScriptSlotCatalogService.cs`

AutoTask 对齐：

- `BetterBTD/Models/AutoTasks/AutoTaskExecutionModels.cs`
- `BetterBTD/Services/Tasks/AutoTasks/AutoTaskRuntimeAdapters.cs`
- `BetterBTD/Core/AutoTasks/Strategies/BlackBorderAutoTaskStrategy.cs`
- `BetterBTD/Core/AutoTasks/Strategies/CustomAutoTaskStrategy.cs`
- `BetterBTD/Core/AutoTasks/Strategies/RaceAutoTaskStrategy.cs`

页面：

- `BetterBTD/ViewModels/MyScriptsPageViewModel.cs`
- `BetterBTD/Views/Pages/MyScriptsPageView.xaml`

## 后续建议

下一阶段最值得继续补的是两块：

1. 脚本编辑器与受管资源库打通
   - 支持直接编辑资源库中的内部脚本文件
   - 编辑后回写资源库元数据缓存
2. `Collection` 任务上下文落地
   - 把当前 3 x 13 占位槽位替换成真实活动槽位
   - 让 `CollectionAutoTaskStrategy` 返回准确 `SlotId`
