# 项目架构

BetterBTD 是单进程 WPF 桌面应用。主程序把游戏窗口捕获、画面识别、输入模拟、脚本执行和自动任务编排在同一运行时中，并通过全局协调器避免多个自动流程争用共享资源。

## 分层

```text
Views (XAML / Window / Page)
        ↓ binding
ViewModels (页面状态与命令)
        ↓
Services (捕获、识别、存储、设置、更新、协议)
        ↓
Core (脚本执行、自动任务策略、模拟器)
        ↓
Windows 与游戏窗口
```

| 目录 | 职责 |
| --- | --- |
| `BetterBTD/Views` | 页面、窗口和复合控件 |
| `BetterBTD/ViewModels` | UI 状态、命令与页面编排 |
| `BetterBTD/Services` | 平台能力和应用服务 |
| `BetterBTD/Core` | 与 UI 无关的脚本、任务和模拟核心 |
| `BetterBTD/Models` | 配置、脚本、任务、游戏元素和运行时 DTO |
| `BetterBTD/Assets` | 数据表、图标、OCR 模板和应用资源 |

## 主要运行链路

### 游戏捕获

`GameWindowInfoService` 解析目标窗口，`GameCaptureService` 管理捕获会话。截图既供开始页诊断，也供关卡状态、菜单状态、地图、英雄和徽章识别使用。

### 脚本执行

脚本文档由 `ScriptDocumentService` 读写，编辑器将文档转换为可执行指令。`ScriptTaskFlowExecutor` 按顺序调度指令处理器，运行时服务提供截图、状态探测和输入能力，执行会话负责停止、暂停检查点和日志。

### 托管脚本库

`ManagedScriptLibraryService` 管理安装目录 `User\MyScripts\` 下的脚本资产、索引和自动任务绑定。脚本 ID 是自动任务引用脚本的稳定主键；显示名和导入来源属于脚本库数据，不写入脚本文件格式。

### 自动任务

自动任务由协调器保证全局互斥。识别层生成当前游戏 UI 快照，导航层根据状态执行下一步动作，策略层决定任务目标和脚本，运行时在进入关卡后复用脚本执行器。

### 设置与用户数据

普通应用配置写入 `%LocalAppData%\BetterBTD\appsettings.json`。脚本、绑定、识别规则和日志写入应用目录的 `User\`，由 `UserDataPathHelper` 统一创建路径。

## 共享资源约束

- 捕获会话、目标窗口和输入设备是进程级共享资源。
- 脚本执行与自动任务不能并行控制游戏。
- UI 导航动作必须在执行后重新识别状态，不能假设点击必然成功。
- 识别失败应记录诊断信息并等待、重试或回退，避免在未知界面连续点击。
- 持久化 ID 使用稳定枚举名或字符串键，本地化只负责显示。

## 外部黑盒测试工具

`tools/BetterBTD.GameDriver` 位于 BetterBTD 单进程运行时之外，直接观察真实 BTD6 窗口。它不引用 BetterBTD、`Fischless.GameCapture`、OCR 模板或运行时状态。BetterBTD 内部识别只能作为诊断信息；黑盒可见行为以 Game Driver 保存的真实游戏截图及其独立解释为准。

测试编排必须维护游戏输入所有权：前置和恢复阶段可由 Game Driver 控制游戏，脚本执行阶段只能由 BetterBTD 输入，Game Driver 只读观察。详细协议见 [独立 BTD6 Game Driver](game-driver.md)。

## 扩展入口

- 新脚本指令：更新脚本模型、编辑器选项、处理器、序列展示和格式文档。
- 新自动任务：增加 `AutoTaskKind`、请求构建、策略、脚本解析和页面配置。
- 新地图：遵循 [新增地图维护流程](map-update-workflow.md)。
- 新机器人动作：注册动作元数据和处理器，并同步 [HTTP 协议](robot-control-http-api.md)。
- 新识别规则：优先加入结构化配置或模板仓库，并补充可复现截图测试。

返回 [开发者文档](README.md) · [开发指南](development.md)
