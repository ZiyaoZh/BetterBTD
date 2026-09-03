# 导航线程与脚本 Worker 协同架构

本文记录自动任务运行时从“`AutoTaskRunner` 包裹脚本监控”迁移到“导航持续观察页面、脚本线程作为受控执行者”的目标架构。导航不拥有关卡结算状态，也不存在等待结算环节。

## 当前协调语义

- 页面状态、脚本 Worker 生命周期和关卡尝试上下文彼此独立；胜负、结算、奖励和弹窗都只是导航策略的页面输入。
- `AutoTaskRunner` 在整次自动任务开始时启动唯一的导航观察生产循环，并在任务退出时停止；普通导航、关卡控制器和运行页面都是该流的独立消费者。
- 同一关卡尝试只发送一次 `Start`。首次确认 `InLevel` 时启动；`Starting`、`Running` 或 `Completed` 与 `InLevel` 并存时均不执行导航输入。
- Worker 状态只对匹配当前运行 ID 的关卡尝试有效；新尝试发送 Start 前必须忽略上一轮遗留的 `Completed`、`Cancelled` 或 `Failed`。
- 活跃脚本遇到明确的非 `InLevel` 页面后进入 5 秒宽限；不同非关卡页面不会重置计时，只有重新确认 `InLevel` 才会重置。`Unknown` 既不证明离开关卡，也不清除已经开始的计时。
- 宽限到期后必须先等待 `PauseAcknowledged`，然后才能点击基准中心点 `(960, 540)` 并消费更新序号的观察。恢复 `InLevel` 后等待 `ResumeAcknowledged`；恢复超时则发送 `Cancel`，等待 Worker 终态和输入释放后把最新页面交回普通导航。
- `Start`、`Pause`、`Resume`、`Cancel` 都按运行 ID 和请求序号关联；Worker 的 Start 命令显式携带自动任务的游戏控制与输入租约上下文。

## 目标与不变量

- 导航观察者以固定间隔读取 `GameCaptureService` 的最新帧，生产带序号的不可变 `GameUiSnapshot`。
- 导航控制器持续读取当前页面并协调脚本恢复；具体页面动作和任务推进仍由导航策略负责。
- 脚本 Worker 只执行脚本步骤，拥有独立的脚本状态、截图/OCR 管线和识别间隔；不得判断关卡成功或失败，也不得处理导航弹窗。
- 运行页面、导航控制器和日志服务消费同一条导航观察快照流；运行页面不得自行开启第二个导航识别循环，也不得使用自动任务会话或脚本 Worker 的进度快照覆盖页面状态、金币、回合等导航观察数据。
- 导航观察和脚本观察可以共享最新帧缓存，但不得共享稳定化计数、当前 UI 状态或脚本等待结果等可变识别状态。
- 单次捕获或识别异常默认视为可恢复：导航观察者发布带诊断的 `Unknown` 快照并继续观察，不因瞬时故障直接中断任务；是否在连续异常后失败由导航控制器按任务上下文决定。
- 任意输入动作都必须经过输入仲裁。恢复流程输入前，脚本必须确认暂停；取消后必须等待脚本退出和按键释放。

## 运行时分层

```text
GameCaptureService (最新帧缓存)
        |                         |
        v                         v
NavigationObservation      ScriptObservationService
(固定间隔、完整 UI 快照)     (脚本专用间隔、金币/回合/OCR)
        |                         |
        v                         v
AutoTaskRunner (整次任务的生产循环生命周期)
        |
        +--> 普通导航策略
        +--> AutoTaskNavigationController <-> ScriptTaskFlowExecutor (Worker)
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

所有消费者只读快照；消费者不能推进识别稳定化状态，也不能再次截图代替导航观察者。观察生产循环由 `AutoTaskRunner` 持有并覆盖整次任务，不能随单段关卡脚本或导航控制器停止；消费者订阅保持有效直到消费者自身取消，以保证普通导航、关卡内观察和多关卡运行使用同一连续序列。Runner 启动任务时记录已有最新序号，只接收更大的序号，避免把上一任务残留快照作为本次首帧。

### 导航控制器

`AutoTaskNavigationController` 持续消费导航观察结果，只协调脚本启动和离关恢复，不启动或停止观察生产循环。脚本终态后的非关卡页面交回 `AutoTaskRunner`，由当前策略继续消费同一观察流并执行普通导航。

### 脚本 Worker 与脚本观察服务

`ScriptTaskFlowExecutor` 应收敛为纯脚本 Worker：执行步骤、在安全检查点响应暂停/恢复/取消、发布生命周期和进度事件。脚本专用观察服务拥有自己的取消令牌、识别间隔、缓存、超时和诊断；脚本等待金币/回合只等待该服务的结果。

## 双维度状态模型

关卡状态与脚本状态必须分开。建议初始枚举如下：

```csharp
public enum StageChallengeState
{
    Navigating, InLevel, OffLevelGrace, PausingForRecovery,
    Recovering, Resuming, NavigationFallback, Failed, Cancelled
}

public enum ScriptWorkerState
{
    NotStarted, Starting, Running, Pausing, Paused,
    CancellationRequested, Completed, Cancelled, Failed
}
```

每次转换都应记录原因、时间戳和导航快照序号。协调状态不编码脚本结果；`InLevel` 可与 Worker 的运行中、暂停或完成状态独立组合。

## 快照后的决策优先级

```text
外部取消
  -> 输入/捕获故障
  -> InLevel 与 Worker 状态决策
  -> 活跃脚本离关宽限/恢复
  -> Worker 终态后的普通导航交接
```

活跃脚本持续 5 秒未确认 `InLevel` 时，导航控制器发送带请求序号和原因的 `Pause`。等待 `PauseAcknowledged` 后才允许恢复点击；重新确认 `InLevel` 则恢复，否则超时取消并交回普通导航。

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

1. 首次确认 `InLevel`：发送一次 Start 并等待关联的 Started；同一尝试不重复启动。
2. Worker 活跃且仍为 `InLevel`：导航继续观察，不执行输入。
3. Worker 完成且仍为 `InLevel`：继续观察且不输入；之后的非关卡页面直接交回普通导航。
4. Worker 活跃且持续 5 秒未确认 `InLevel`：安全暂停并反复点击恢复坐标，每次点击后等待新观察。
5. 恢复成功：确认 `InLevel` 后恢复 Worker；恢复失败：取消并等待终态，再交回最新页面。

## 对现有组件的迁移约束

- `AutoTaskRunner` 负责整次任务的导航观察生命周期、会话、普通导航策略、导航控制器交接和归档；删除 `ExecuteScriptWithUiMonitoringAsync`、`ShouldMonitorStageScriptUi`、`ShouldInterruptStageScript` 等脚本包装逻辑。
- `GameUiStateService` 演化为导航观察实现，保留一次性 `CaptureSnapshotAsync` 作为底层 API，但业务层不再各自启动长期循环。
- `GameStageStateService` 保留脚本专用识别能力，不共享导航稳定化状态，不判断挑战结果。
- `GameControlLeaseCoordinator` 可作为底层租约实现；在其上增加 `GameInputArbiter`，表达导航主租约、脚本子租约和临时抢占。

## 验收测试范围

至少覆盖：首次进入仅启动一次、运行中和完成后的 `InLevel` 空操作、跨非关卡页面累计 5 秒、`Unknown` 不重置计时、暂停确认前禁止点击、点击后消费新观察、恢复后继续、超时取消并等待终态、租约上下文穿透，以及普通导航接管。真实 BTD6 弹窗、胜负和缩放场景仍需在集成环境中单独记录已验证与未验证范围。

返回 [自动任务架构](auto-task-architecture.md) · [阶段实施提示](../navigation-script-refactor-stages.md)
