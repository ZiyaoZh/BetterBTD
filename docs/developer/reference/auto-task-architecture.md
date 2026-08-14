# 自动任务架构

本文描述当前自动任务运行时的职责边界。用户配置方法见 [自动任务指南](../../user/auto-tasks.md)。

## 设计目标

- 将画面识别、通用导航、任务策略和脚本执行分开。
- 所有点击都在执行后重新识别状态，不假设页面一定跳转成功。
- 自动任务与单脚本执行共享捕获和输入资源，并保持全局互斥。
- 任务运行支持停止、暂停检查点、阶段状态和当前动作状态。
- 地图、难度、模式与脚本槽位使用稳定 ID，不依赖本地化文字。

## 组件

| 层 | 主要类型 | 职责 |
| --- | --- | --- |
| 协调 | `AutoTaskCoordinator` | 拒绝并发任务，管理当前运行实例 |
| 生命周期 | `AutoTaskRunner`、`AutoTaskExecutionSession` | 驱动主循环、阶段、暂停、取消和进度 |
| 策略 | `IAutoTaskStrategy`、`AutoTaskStrategyRegistry` | 根据任务类型和当前快照决定等待、导航、执行脚本、完成或失败 |
| 识别 | `GameUiStateService`、`GameUiDetectionRuleEvaluator` | 截图并生成 `GameUiSnapshot` |
| 导航 | `GameUiNavigator` | 将目标关卡和当前 UI 状态转换为下一导航步骤 |
| 动作 | `GameUiActionExecutor`、各任务 ActionHandler | 执行点击、返回、领奖和任务特有动作 |
| 脚本 | `IAutoTaskScriptResolver`、`IAutoTaskScriptExecutor` | 解析脚本槽位并复用脚本执行器 |

## 主循环

```text
捕获当前 UI
      ↓
策略生成 AutoTaskDecision
      ↓
Wait / Navigate / StartScript / Complete / Fail
      ↓
动作后等待并重新捕获
```

`AutoTaskPhase` 表达任务进度：

```text
PreparingStage
→ NavigatingToStage
→ WaitingForLevelLoad
→ ExecutingScript
→ SettlingResult
→ AdvancingObjective
→ Completed / Failed
```

阶段用于表达粗粒度任务进度，不替代实时 UI 识别。`AutoTaskActivityKind` 由 Runner 发布，表达捕获、等待、导航、解析脚本、执行脚本和处理结算等当前动作；界面不得解析内部检查点或消息来推断动作。程序恢复循环时始终以最新画面为依据。

## UI 快照与导航

`GameUiSnapshot` 包含：

- `State`：规范化 UI 状态，例如主菜单、难度选择、关卡内、胜利或奖励页。
- `Confidence`：识别置信度。
- `StageState`：关卡内金币、回合、升级面板等状态。
- `Facts`：地图、英雄或任务处理器需要的附加事实。
- `Summary`：运行窗口和日志使用的摘要。

识别规则默认存放在 `User\AutoTasks\game_ui_detection_rules.json`。规则升级由 `GameUiDetectionConfigService` 归一化，内置模板和代码探测处理不适合纯坐标规则的场景。

`GameUiNavigator` 只负责通用页面转移。收集、金气球、黑框、竞速和奥德赛的特殊页面由对应 `IGameUiTaskActionHandler` 扩展。

## 策略

当前策略包括：

- `CollectionAutoTaskStrategy`
- `GoldBalloonAutoTaskStrategy`
- `BlackBorderAutoTaskStrategy`
- `LoopStageAutoTaskStrategy`
- `RaceAutoTaskStrategy`
- `OdysseyAutoTaskStrategy`
- `CustomAutoTaskStrategy`

策略不直接操作鼠标。它只读取 `AutoTaskRuntimeState` 与快照，并返回 `AutoTaskDecision`。这使策略测试可以替换识别、输入和脚本执行服务。

## 脚本解析

自动任务通过 `AutoTaskScriptQuery` 请求脚本。解析顺序由任务和请求决定，可能包括：

- 显式文件路径
- 多关卡脚本路径列表
- 托管脚本槽位 ID
- 地图、难度、模式和变体组成的绑定

脚本 ID 与绑定由 `ManagedScriptLibraryService` 管理。标签只提供说明和筛选，不应作为唯一绑定键。

## 暂停、取消与结算

- `RequestPause` 同时转发给自动任务会话和正在运行的脚本执行器。
- 暂停发生在检查点，避免把流程冻结在半次点击或按键之间。
- 取消通过 `CancellationToken` 传播到捕获、延时、导航和脚本执行。
- 脚本运行期间，Runner 会继续监视结算类 UI，并把界面状态、金币、回合或升级面板等可展示语义变化发布到当前进度；检测到结算页时停止脚本并进入 `SettlingResult`。

## 失败边界

- 连续导航失败达到阈值后终止任务并保留最后快照。
- 未解析到脚本时立即失败，不在未知关卡中继续点击。
- `Unknown` 状态应等待、记录或回退，不应触发任务特有的猜测动作。
- 新增 UI 状态必须同步检查优先级，结算、弹窗和奖励页通常应高于普通菜单页。

## 测试重点

- Fake 识别与 Fake 输入下的导航序列。
- 每种策略对关键 UI 状态的决策。
- 绑定缺失、脚本删除和重复脚本 ID。
- 暂停、恢复、取消和脚本结算中断。
- 达到连续导航失败阈值后的结果与日志。
- 真实 `1920 × 1080` 中英文截图的规则回归。

返回 [开发者文档](../README.md) · [项目架构](../architecture.md)
