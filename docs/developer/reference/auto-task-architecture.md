# 自动任务架构

本文描述当前自动任务运行时的职责边界。用户配置方法见 [自动任务指南](../../user/auto-tasks.md)。

> 迁移说明：本文保留现有实现的背景和兼容约束。导航线程持续拥有界面状态机、脚本 Worker 独立受控运行的目标设计，以及后续分阶段实施要求，统一以[导航线程与脚本 Worker 协同架构](navigation-script-coordination.md)和[阶段实施提示](../navigation-script-refactor-stages.md)为准。

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
检测界面是否持续无进展，必要时恢复或失败
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

用户从任务运行窗口启动任务时，运行时会先枚举本次任务范围内所有已绑定或显式配置的脚本，并逐一完成任务级依赖预检。当前预检只检查脚本使用的快捷键是否已配置；任一脚本存在缺失快捷键时，任务不会启动捕获、获取游戏控制权或执行任何脚本。未绑定槽位仍由原有运行时脚本解析流程处理。

## 暂停、取消与结算

- `RequestPause` 同时转发给自动任务会话和正在运行的脚本执行器。
- 暂停发生在检查点，避免把流程冻结在半次点击或按键之间。
- 取消通过 `CancellationToken` 传播到捕获、延时、导航和脚本执行。
- 脚本运行期间，Runner 会继续监视结算类 UI，并把界面状态、金币、回合或升级面板等可展示语义变化发布到当前进度；检测到结算页时停止脚本并进入 `SettlingResult`。

## 界面卡死恢复

持续循环的自动任务（`Collection`、`GoldBalloon`、`BlackBorder`、`LoopStage` 和 `Race`）会监视游戏界面是否有进展。进度标识由规范化 UI 状态、自动任务阶段、已完成关卡数和粗粒度画面指纹组成，因此地图翻页等发生在同一规范化状态内的正常变化也会重置计时。

- 默认超时为 10 秒，可在“设置 > 自动任务设置 > 失败恢复”中配置为 1 至 300 秒。
- `Unknown` 参与卡死检测和恢复。
- `InLevel` 不计时并清除已有观察，因为关卡挑战可能长时间保持该状态。
- `Loading` 参与超时检测，但超时后不猜测点击而是直接失败。
- 暂停后恢复会清除已有观察，暂停时间不计入超时。

超时后，Runner 按顺序点击配置的 `1920 × 1080` 基准恢复坐标。默认序列为 `(960, 840)`、`(960, 760)`、`(1340, 850)`、`(850, 810)`、`(780, 730)`、`(1140, 730)`、`(80, 55)`。每次点击后都按配置的等待时间重新捕获界面；一旦点击前后的 64 位画面指纹汉明距离超过配置容差，立即退出恢复并回到正常决策。恢复成功不依赖规范化 UI 状态是否变化；任一快照缺少指纹时不能据此宣告恢复。全部坐标尝试后指纹仍无变化时，任务在 `StuckUiRecovery` 检查点失败，并保留最终快照和尝试次数。坐标列表为空时不执行猜测点击，直接失败。

失败恢复设置持久化在 `%LocalAppData%\BetterBTD\appsettings.json`。除卡死阈值、点击等待时间和恢复坐标顺序外，也可配置连续导航失败阈值和 64 位画面指纹差异容差。加载配置时，完整且顺序一致的旧版默认 5 点序列会迁移为当前默认 7 点序列；自定义序列和显式空列表保持不变。坐标会限制在 `0..1919` 和 `0..1079`，列表最多保留 20 项；每次自动任务启动时会从持久化配置创建独立的运行参数快照。

## 失败边界

- Runner 返回 `Failed` 前会通过 `IAutoTaskFailureArtifactWriter` 归档一次失败现场。默认实现从捕获服务克隆当前游戏帧，并在 `User\Logs\AutoTasks\Errors\<session>\<yyyyMMdd>\<timestamp_task>\` 中写入 `game.png` 和 `task.log`。
- 失败日志记录本地/UTC 时间、任务和脚本上下文、阶段、动作、检查点、界面状态、重试计数、错误消息与异常堆栈。截图不可用时仍写日志；归档异常不得覆盖原任务错误。
- `Cancelled` 表示用户或上层取消，不属于错误退出，不生成失败归档。
- 连续导航失败达到阈值后终止任务并保留最后快照。
- 可操作界面持续不变且全部卡死恢复坐标均无效时终止任务。
- 未解析到脚本时立即失败，不在未知关卡中继续点击。
- 任务级依赖预检失败时不进入运行状态，也不占用捕获和输入资源。
- `Unknown` 状态不触发任务特有动作，但持续无进展时允许执行通用卡死恢复坐标。
- 新增 UI 状态必须同步检查优先级，结算、弹窗和奖励页通常应高于普通菜单页。

## 测试重点

- Fake 识别与 Fake 输入下的导航序列。
- 每种策略对关键 UI 状态的决策。
- 绑定缺失、脚本删除和重复脚本 ID。
- 多脚本任务应覆盖全部脚本的依赖预检、重复文件去重和后续脚本缺失快捷键的阻断行为。
- 暂停、恢复、取消和脚本结算中断。
- 达到连续导航失败阈值后的结果与日志。
- 失败归档的时间目录、截图和日志内容，以及截图/写盘失败不改变原失败结果。
- `Unknown`、`InLevel`、`Loading` 和画面指纹变化下的卡死计时与恢复结果。
- 真实 `1920 × 1080` 中英文截图的规则回归。

返回 [开发者文档](../README.md) · [项目架构](../architecture.md)
