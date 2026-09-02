# 托管脚本库设计

“我的脚本”不是任意目录浏览器，而是由应用维护的脚本资产库。它为脚本导入、检索、自动任务绑定和订阅包提供稳定数据源。

## 存储结构

默认根目录为 `<安装目录>\User\MyScripts\`：

```text
MyScripts\
├─ library.json
├─ Assets\
└─ Bindings\
   ├─ collection.json
   ├─ goldballoon.json
   └─ blackborder.json
```

- `library.json` 保存脚本资产记录和通用绑定。
- `Assets` 保存受管脚本文件。
- `Bindings` 保存需要独立编辑和订阅导入导出的任务绑定。

所有路径由 `UserDataPathHelper` 和 `ManagedScriptLibraryService` 创建。文档和代码不应再假设脚本库存放在 `%LocalAppData%`。

## 数据边界

脚本文件只保存可移植内容：

- schema 与格式版本
- 地图、难度、模式、英雄、说明和标签
- 猴子对象快照
- 指令序列

脚本库记录额外保存：

- `ScriptId`
- `DisplayName`
- 来源和受管文件名
- 文件指纹
- 元数据缓存
- 导入与更新时间
- 槽位绑定

显示名、来源和任务绑定不写回脚本文件，以免同一脚本在不同脚本库中失去可移植性。

## 导入

导入流程负责：

1. 读取并验证当前或旧版脚本。
2. 计算文件指纹和稳定的脚本记录。
3. 将文件复制到 `Assets`。
4. 更新 `library.json` 中的元数据缓存。
5. 对旧版包批量转换并报告进度。

重复导入时应优先更新已有记录，避免相同脚本产生重复键或孤立资产。

## 槽位与绑定

`ManagedScriptSlotCatalogService` 根据任务类型生成稳定槽位。典型槽位由以下维度组成：

- 任务类型
- 地图
- 难度
- 模式
- 收集变体或其他任务限定值

绑定保存 `SlotId → ScriptId`，因此一个脚本可被多个槽位复用。删除脚本时需要同步清理或标记失效绑定。

收集、金气球和黑框使用专用绑定文件；循环刷关、竞速和奥德赛在任务请求中携带一个或多个脚本 ID。专用文件属于内部持久化格式：收集任务由 `CollectionScriptBindingWindowViewModel` 读取脚本库快照，并通过 `ManagedScriptLibraryService.SetTaskBindings` 整批校验和保存，界面不直接编辑 JSON。

## 订阅包

订阅包同时携带脚本资产和槽位绑定。导入时必须：

- 校验包路径，防止目录穿越。
- 处理脚本 ID 冲突和重复资源。
- 将外部绑定映射到当前脚本库中的实际脚本 ID。
- 在部分失败时给出可定位的错误信息。

## 页面职责

`MyScriptsPageViewModel` 负责筛选、选择和命令状态，收集绑定窗口负责暂存用户选择，`ManagedScriptLibraryService` 负责持久化。页面不直接扫描或修改文件结构。

脚本编辑器从脚本库打开文件后保存时，应刷新库中的元数据缓存。另存为外部路径不会自动成为新的托管脚本，仍需显式导入。

## 关键文件

- `BetterBTD/Models/MyScripts/ManagedScriptLibraryModels.cs`
- `BetterBTD/Services/MyScripts/ManagedScriptLibraryService.cs`
- `BetterBTD/Services/MyScripts/ManagedScriptSlotCatalogService.cs`
- `BetterBTD/ViewModels/MyScriptsPageViewModel.cs`
- `BetterBTD/ViewModels/CollectionScriptBindingWindowViewModel.cs`
- `BetterBTD/Views/Pages/MyScriptsPageView.xaml`
- `BetterBTD/Views/Windows/CollectionScriptBindingWindow.xaml`

返回 [开发者文档](../README.md) · [脚本文件格式](../script-file-format.md)
