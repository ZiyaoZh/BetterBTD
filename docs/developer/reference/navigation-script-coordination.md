# 导航线程与脚本 Worker 协同架构

本文记录自动任务运行时从“`AutoTaskRunner` 包裹脚本监控”迁移到“导航线程拥有挑战状态机、脚本线程作为受控执行者”的目标架构。本文是后续代码改造的设计基线；现有实现与本文不一致的部分，应在阶段文档中明确迁移状态，不要通过继续扩展 `ExecuteScriptWithUiMonitoringAsync` 来掩盖边界问题。

## 目标与不变量

- 导航观察者以固定间隔读取 `GameCaptureService` 的最新帧，生产带序号的不可变 `GameUiSnapshot`。
- 导航控制器是界面状态、关卡挑战阶段、弹窗、胜负和结算的唯一权威。
- 脚本 Worker 只执行脚本步骤，拥有独立的脚本状态、截图/OCR 管线和识别间隔；不得判断关卡成功或失败，也不得处理导航弹窗。
- 运行页面、导航控制器和日志服务消费同一条导航观察快照流；运行页面不得自行开启第二个导航识别循环。
- 导航观察和脚本观察可以共享最新帧缓存，但不得共享稳定化计数、当前 UI 状态或脚本等待结果等可变识别状态。
- 单次捕获或识别异常默认视为可恢复：导航观察者发布带诊断的 `Unknown` 快照并继续观察，不因瞬时故障直接中断任务；是否在连续异常后失败由导航控制器按任务上下文决定。
- 任意输入动作都必须经过输入仲裁。导航抢占脚本输入前，脚本必须确认暂停；胜负/结算时必须先取消并等待脚本退出、释放按键。

## 运行时分层

```text
GameCaptureService (最新帧缓存)
        |                         |
        v                         v
NavigationObservation      ScriptObservationService
(固定间隔、完整 UI 快照)     (脚本专用间隔、金币/回合/OCR)
        |                         |
        v                         v
AutoTaskNavigationController <-> ScriptTaskFlowExecutor (Worker)
        |
        +--> AutoTaskSession / 运行页面 / 日志
```

### 导航观察者

导航观察者是单一生产者循环，负责捕获、识别、稳定化、序号和诊断。发布前必须复制快照并冻结 `Facts` 映射；多个订阅者接收同一个观察对象，后加入的订阅者先收到当前最新观察。建议公开：

```csharp
public sealed record NavigationObservation(
    long Sequence,
    DateTimeOffset CapturedAt,
    GameUiSnapshot Snapshot);
```

所有消费者只读快照；消费者不能推进识别稳定化状态，也不能再次截图代替导航观察者。

### 导航控制器

建议新增 `AutoTaskNavigationController`（或等价的 `StageChallengeCoordinator`）。它持续消费导航观察结果，按优先级处理取消、捕获故障、胜负/结算、弹窗、普通导航以及脚本启动/等待，并向脚本 Worker 发送命令。

### 脚本 Worker 与脚本观察服务

`ScriptTaskFlowExecutor` 应收敛为纯脚本 Worker：执行步骤、在安全检查点响应暂停/恢复/取消、发布生命周期和进度事件。脚本专用观察服务拥有自己的取消令牌、识别间隔、缓存、超时和诊断；脚本等待金币/回合只等待该服务的结果。

## 双维度状态模型

关卡状态与脚本状态必须分开。建议初始枚举如下：

```csharp
public enum StageChallengeState
{
    Preparing, EnteringStage, InStageBeforeScript, ScriptRunning,
    ScriptCompletedWaitingForResult, ResultDetected, HandlingPopup,
    HandlingVictory, HandlingDefeat, Completed, Failed, Cancelled
}

public enum ScriptWorkerState
{
    NotStarted, Starting, Running, Pausing, Paused,
    CancellationRequested, Completed, Cancelled, Failed
}
```

每次转换都应记录原因、时间戳和导航快照序号。下列组合是合法且必须可表达的：脚本完成但仍等待结果、处理弹窗时脚本已暂停、处理胜负时脚本正在取消。

## 快照后的决策优先级

```text
外部取消
  -> 输入/捕获故障
  -> 失败/胜利/结算
  -> 必须处理的弹窗
  -> 加载或不可操作状态
  -> 是否启动脚本
  -> 是否等待脚本完成
  -> 普通导航
```

弹窗出现时，导航控制器发送带请求序号和原因的 `Pause`，等待 `PauseAcknowledged` 后通过输入仲裁取得导航权限并处理弹窗，再重新观察界面决定恢复或终止。失败、胜利和结算界面不恢复脚本：发送 `Cancel`，等待 Worker 退出并确认输入释放后再执行结果处理。

## 命令、事件与输入仲裁

导航到脚本使用 `Channel<ScriptWorkerCommand>` 或等价的异步命令通道：

```text
Start | Pause | Resume | Cancel
```

命令至少包含运行 ID、请求序号、触发原因、取消令牌和是否等待确认。脚本到导航使用事件通道：

```text
Started | ProgressChanged | PauseAcknowledged | ResumeAcknowledged
Completed | Cancelled | Failed
```

事件包含运行 ID、当前/最后完成步骤、状态、时间戳、错误和脚本识别诊断。导航持有挑战级主输入租约，脚本只能使用导航授予的子租约；未收到暂停确认前，导航不得点击。

## 关键生命周期

1. `InStageBeforeScript`：导航持续观察，确认关卡已进入且无阻塞弹窗后启动 Worker。
2. `ScriptRunning`：导航继续识别和发布快照；脚本观察服务按自己的间隔执行 OCR 和步骤所需识别。
3. `ScriptCompletedWaitingForResult`：Worker 已完成，但导航继续等待 Victory/Defeat/Settlement，脚本完成不等于挑战完成。
4. 运行中发现胜负：导航取消 Worker，等待真正退出和按键释放，再进入结果处理。
5. 运行中发现普通弹窗：导航暂停 Worker，处理弹窗并重新观察，确认安全后再恢复或改变阶段。

## 对现有组件的迁移约束

- `AutoTaskRunner` 最终只负责创建会话、启动导航控制器、等待最终结果和归档；删除 `ExecuteScriptWithUiMonitoringAsync`、`ShouldMonitorStageScriptUi`、`ShouldInterruptStageScript` 等脚本包装逻辑。
- `GameUiStateService` 演化为导航观察实现，保留一次性 `CaptureSnapshotAsync` 作为底层 API，但业务层不再各自启动长期循环。
- `GameStageStateService` 保留脚本专用识别能力，不共享导航稳定化状态，不判断挑战结果。
- `GameControlLeaseCoordinator` 可作为底层租约实现；在其上增加 `GameInputArbiter`，表达导航主租约、脚本子租约和临时抢占。

## 验收测试范围

至少覆盖：脚本启动前导航、脚本等待不阻塞导航、脚本完成后等待结果、运行中胜负取消、普通弹窗暂停/恢复、导航或脚本观察异常、外部取消、暂停/取消确认超时、输入释放失败、多个快照消费者共享同一序号，以及导航/脚本识别间隔互不影响。真实 BTD6 弹窗、胜负和缩放场景仍需在集成环境中单独记录已验证与未验证范围。

返回 [自动任务架构](auto-task-architecture.md) · [阶段实施提示](../navigation-script-refactor-stages.md)
