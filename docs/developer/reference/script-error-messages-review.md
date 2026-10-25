# 脚本失败提示文案待审核

以下为本次新增或调整的用户提示。`{0}`、`{1}` 等为运行时参数；步骤编号从 1 开始。参数中的已知猴塔、英雄、升级路径和等待模式使用当前软件语言；未知值及用户对象 ID 保留原值。

示例：

> 执行失败：第 3 步「放置猴子」。\
> 已尝试调整位置，仍无法在 (960, 540) 附近放置“飞镖猴”。\
> 尝试次数：3。

运行日志保留原始诊断消息。本次不翻译全部运行日志，也不涉及脚本文件打开、导入和保存错误。

| 资源键（省略 Editor.Runtime.） | 中文 | English |
| --- | --- | --- |
| Error.RetryFailed | 操作重试 {0} 次后仍未成功。 | The operation failed after {0} attempts. |
| Error.WaitTimedOut | 等待超时（{0} 毫秒）。 | The wait timed out after {0} ms. |
| Error.SnapshotUnavailable | 无法获取当前关卡画面。 | Game stage snapshot is unavailable. |
| Error.UnsupportedActivatedAbility | 不支持的主动技能“{0}”。 | Unsupported activated ability '{0}'. |
| Error.MissingCoordinateTarget | 修改坐标指令未指定目标猴子。 | Modify coordinate instruction is missing the target monkey binding ID. |
| Error.TargetNotFound | 未找到目标猴子“{0}”的运行状态。 | Target monkey binding '{0}' does not exist in runtime state. |
| Error.UnsupportedNextRound | 不支持的下一回合操作“{0}”。 | Unsupported next round action '{0}'. |
| Error.UnsupportedHeroInventory | 不支持的英雄物品“{0}”。 | Unsupported hero inventory '{0}'. |
| Error.HeroPlacementNotConfirmed | 在 {0} 点击放置英雄后，游戏仍处于放置模式。 | Hero placement at {0} did not exit placement mode after the click. |
| Error.HeroPlacementActivationFailed | 未能在 {1} 毫秒内进入“{0}”的英雄放置模式。 | Failed to activate hero placement mode for '{0}' within {1} ms. |
| Error.MissingSellTarget | 出售猴子指令未指定目标猴子。 | Sell monkey instruction is missing the target monkey binding ID. |
| Error.TargetCoordinateUnknown | 目标猴子“{0}”没有已知坐标。 | Target monkey '{0}' does not have a known runtime coordinate. |
| Error.MissingAbilityTarget | 设置猴子能力指令未指定目标猴子。 | Set monkey ability instruction is missing the target monkey binding ID. |
| Error.UnsupportedMonkeyAbility | 不支持的猴子能力“{0}”。 | Unsupported monkey ability '{0}'. |
| Error.MissingSwitchTarget | 切换目标指令未指定目标猴子。 | Switch target instruction is missing the target monkey binding ID. |
| Error.UnsupportedSwitchDirection | 不支持的目标切换方向“{0}”。 | Unsupported switch direction '{0}'. |
| Error.MissingUpgradeTarget | 升级指令未指定目标猴子。 | Upgrade instruction is missing the target monkey binding ID. |
| Error.UpgradeLevelCap | 无法将“{0}”路径升级至 {1} 级，最高为 5 级。 | Cannot upgrade '{0}' to target level {1} because level 5 is the maximum. |
| Error.UnsupportedUpgradePath | 不支持的升级路径“{0}”。 | Unsupported upgrade path '{0}'. |
| Error.UpgradePanelNotDetected | 未能识别所选猴子的升级面板。 | Failed to detect the upgrade panel for the selected monkey. |
| Error.UpgradeLevelUnreadable | 无法读取“{0}”路径的当前等级。 | Failed to read the current '{0}' path level. |
| Error.InvalidWaitColor | 等待颜色“{0}”无效，请使用 #RRGGBB 格式。 | Unsupported wait color '{0}'. Expected format '#RRGGBB'. |
| Error.UnsupportedWaitMode | 不支持的等待模式“{0}”。 | Unsupported wait mode '{0}'. |
| Error.UpgradePanelOpenFailed | 未能打开 {0} 附近猴子的升级面板。 | Failed to open the upgrade panel near {0}. |
| Error.UpgradePanelNotDetectedAtCoordinate | 未能识别 {0} 附近猴子的升级面板。 | Failed to detect the upgrade panel near {0}. |
| Error.PlacementOffsetFailed | 已尝试调整位置，仍无法在 {1} 附近放置“{0}”。 | Failed to place '{0}' near {1} after offset search. |
| Error.PlacementFailed | 无法在 {1} 放置“{0}”（未启用放置失败调整）。 | Failed to place '{0}' at {1} without failure adjustment. |
| Error.UnsupportedCommand | 第 {1} 步使用了不支持的指令类型“{0}”。 | Step {1} uses unsupported script command type '{0}'. |
| Error.InputScopeUnavailable | 无法取得游戏输入控制权，请等待当前操作结束后重试。 | Unable to acquire the script input scope from the navigation controller. |
| Error.CaptureNotRunning | 截图服务尚未启动，请先启动游戏捕获后再执行脚本。 | Game capture service must be running before executing a script task flow. |
| Error.TargetWindowUnavailable | 目标游戏窗口不可用，请确认游戏已启动并重新选择捕获窗口。 | Target game window is not available for script execution. |
| Error.AlreadyRunning | 已有脚本正在运行，请等待执行结束或停止当前脚本。 | Another script task flow is already running. |
| Error.UnsupportedTowerPlacement | 不支持的猴塔放置类型“{0}”。 | Unsupported tower placement selection '{0}'. |
| Error.UnsupportedPlacementSelection | 不支持的放置对象“{0}”。 | Unsupported placement selection code '{0}'. |
| Error.KeyBindingNotConfigured | 脚本所需的快捷键尚未配置，请在设置中完成快捷键配置。 | A key binding required by the script is not configured. Configure it in Settings. |
| FailedStep | 执行失败：第 {0} 步「{1}」。<br>{2} | Execution failed at step {0} ({1}).<br>{2} |
| FailureAttempts | 尝试次数：{0}。 | Attempts: {0}. |
| UnknownInstruction | 未知指令 | Unknown instruction |
| BuildFailed | 构建执行任务失败。<br><br>{0} | Failed to build the execution task.<br><br>{0} |
| Error.Unexpected | 发生未预期的错误，请查看运行日志中的诊断信息。 | An unexpected error occurred. See the runtime log for diagnostic details. |
| Error.CaptureWindowNotFound | 未找到目标窗口。请先启动游戏，或在开始页面手动选择捕获窗口。 | Target window not found. Start the game or select a capture window on the Start page. |
| Error.CaptureNamedWindowNotFound | 未找到目标窗口“{0}”。请先启动游戏，或在开始页面手动选择捕获窗口。 | Target window '{0}' not found. Start the game or select a capture window on the Start page. |
