# 脚本执行引擎

脚本执行引擎将持久化的 `ScriptDocument` 转换为顺序任务流，并通过可替换的运行时服务操作真实游戏或测试替身。

## 执行链路

```text
ScriptDocumentService
        ↓
ScriptTaskFlowService
        ↓
ScriptTaskFlowExecutor
        ↓
ScriptInstructionHandlerRegistry
        ↓
具体指令处理器
        ↓
Capture / StageState / Input / Delay / Log
```

`ScriptTaskFlowExecutor` 保证单实例运行，校验截图器和目标窗口，遍历步骤并生成 `ScriptExecutionResult`。失败信息包含步骤索引、指令类型、检查点、尝试次数和消息。

## 运行时服务

`ScriptExecutionRuntimeServices` 封装：

- 游戏窗口和截图状态
- 关卡状态探测
- 鼠标与键盘输入
- 延时与取消
- 运行日志

处理器依赖接口而不是直接访问 WPF 页面，因此测试可以提供 Fake Capture、Fake Input 和预设状态快照。

## 会话与检查点

`ScriptExecutionSession` 保存：

- `Running`、`PauseRequested`、`Paused`、`Completed`、`Cancelled`、`Failed` 状态
- 当前步骤、指令、检查点和重试次数
- 已完成步骤数和最后完成索引
- 内存日志与日志文件路径

暂停在 `ReachCheckpointAsync` 等安全边界生效。处理器不应在一次鼠标按下与释放之间等待暂停，也不应绕过会话直接长时间阻塞。

## 指令处理器

每个 `ScriptCommandType` 对应一个 `IScriptInstructionHandler`。当前处理器覆盖：

- 放置、升级、卖出、切换目标和能力设置
- 英雄物品与主动技能
- 鼠标点击
- 下一回合与快进动作
- 时间、金钱、回合和颜色等待
- 修改猴子坐标
- 注释

处理器应采用“检查前置状态 → 执行动作 → 验证结果 → 有界重试”的结构。不能仅以“输入调用没有抛异常”作为游戏动作成功的证据。

## 对象状态

`ScriptExecutionState` 按 `bindingId` 维护每只猴子的运行时状态：

- 对象 ID 和选择代码
- 放置顺序
- 最后已知坐标
- 预期升级等级

放置处理器建立坐标，升级、卖出和目标类处理器复用该状态。处理器更新状态时应以已验证的游戏结果为依据。

## 坐标

`CoordinateTransformService` 使用 `1920 × 1080` 参考系，在脚本坐标、窗口客户区坐标和屏幕坐标之间换算。非 16:9 客户区只进行独立缩放，因此调用方必须保留比例检查和用户警告。

## 间隔策略

- `InstructionCustom`：使用每条指令自己的 `intervalToNextInstructionMs`。
- `CommonOperationInterval`：用运行选项中的统一间隔覆盖常规步骤。

自动任务启动脚本时使用统一操作间隔，以便由任务卡片控制整体节奏。

## 新增指令检查清单

1. 在脚本文档模型和枚举中增加持久化字段。
2. 更新编辑器指令模板、选项、属性面板和序列摘要。
3. 实现处理器并注册到 `ScriptInstructionHandlerRegistry`。
4. 补充成功、重试、取消和失败详情测试。
5. 更新 [脚本文件格式](../script-file-format.md) 和用户脚本指南。
6. 验证旧脚本缺少新字段时仍使用安全默认值。

返回 [开发者文档](../README.md) · [项目架构](../architecture.md)
