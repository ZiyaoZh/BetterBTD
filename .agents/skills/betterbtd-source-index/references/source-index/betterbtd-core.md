# BetterBTD Core and Helpers

UI-independent execution, automatic tasks, simulation, control leases, and helpers.

[Back to BetterBTD Source Index](../source-index.md)

## Related Indexes

- [BetterBTD Models](./betterbtd-models.md)
- [BetterBTD Services](./betterbtd-services.md)
- [BetterBTD Tests](./betterbtd-tests.md)

## Directory Summary

| Directory | Files |
| --- | ---: |
| `BetterBTD/Core/AutoTasks` | 6 |
| `BetterBTD/Core/AutoTasks/Runtime` | 1 |
| `BetterBTD/Core/AutoTasks/Strategies` | 8 |
| `BetterBTD/Core/Config` | 2 |
| `BetterBTD/Core/GameControl` | 1 |
| `BetterBTD/Core/RobotControl` | 2 |
| `BetterBTD/Core/ScriptExecution` | 10 |
| `BetterBTD/Core/ScriptExecution/Handlers` | 16 |
| `BetterBTD/Core/ScriptExecution/Handlers/Support` | 2 |
| `BetterBTD/Core/ScriptExecution/Runtime` | 1 |
| `BetterBTD/Core/Simulator` | 7 |
| `BetterBTD/Core/Simulator/Extensions` | 4 |
| `BetterBTD/Core/TestApi` | 1 |
| `BetterBTD/Helpers` | 16 |
| `BetterBTD/Helpers/Extensions` | 6 |
| `BetterBTD/Helpers/Http` | 2 |
| `BetterBTD/Helpers/Security` | 1 |

## File Inventory

Paths are relative to the repository root. Open the linked file and verify current behavior before editing.

### `BetterBTD/Core/AutoTasks`

| File | Description |
| --- | --- |
| [AutoTaskCoordinator.cs](../../../../../BetterBTD/Core/AutoTasks/AutoTaskCoordinator.cs) | Automatic-task coordination, sessions, and registry; primary symbols: AutoTaskCoordinator |
| [AutoTaskExecutionSession.cs](../../../../../BetterBTD/Core/AutoTasks/AutoTaskExecutionSession.cs) | Automatic-task coordination, sessions, and registry; primary symbols: AutoTaskExecutionSession |
| [AutoTaskRunner.cs](../../../../../BetterBTD/Core/AutoTasks/AutoTaskRunner.cs) | Automatic-task coordination, sessions, and registry; primary symbols: AutoTaskRunner |
| [AutoTaskStrategyRegistry.cs](../../../../../BetterBTD/Core/AutoTasks/AutoTaskStrategyRegistry.cs) | Automatic-task coordination, sessions, and registry; primary symbols: AutoTaskStrategyRegistry |
| [AutoTaskStuckUiTracker.cs](../../../../../BetterBTD/Core/AutoTasks/AutoTaskStuckUiTracker.cs) | Automatic-task coordination, sessions, and registry; primary symbols: AutoTaskStuckUiTracker, struct |
| [StageChallengeStateTransitions.cs](../../../../../BetterBTD/Core/AutoTasks/StageChallengeStateTransitions.cs) | Automatic-task coordination, sessions, and registry; primary symbols: StageChallengeStateTransitions |

### `BetterBTD/Core/AutoTasks/Runtime`

| File | Description |
| --- | --- |
| [AutoTaskRuntimeServices.cs](../../../../../BetterBTD/Core/AutoTasks/Runtime/AutoTaskRuntimeServices.cs) | Automatic-task coordination, sessions, and registry; primary symbols: GameUiRecognitionContext, IGameUiRecognizer, IGameUiStateService, INavigationObservationService, IGameUiNavigator |

### `BetterBTD/Core/AutoTasks/Strategies`

| File | Description |
| --- | --- |
| [BlackBorderAutoTaskStrategy.cs](../../../../../BetterBTD/Core/AutoTasks/Strategies/BlackBorderAutoTaskStrategy.cs) | Concrete automatic-task strategy; primary symbols: BlackBorderAutoTaskStrategy |
| [CollectionAutoTaskStrategy.cs](../../../../../BetterBTD/Core/AutoTasks/Strategies/CollectionAutoTaskStrategy.cs) | Concrete automatic-task strategy; primary symbols: CollectionAutoTaskStrategy |
| [CustomAutoTaskStrategy.cs](../../../../../BetterBTD/Core/AutoTasks/Strategies/CustomAutoTaskStrategy.cs) | Concrete automatic-task strategy; primary symbols: CustomAutoTaskStrategy |
| [GoldBalloonAutoTaskStrategy.cs](../../../../../BetterBTD/Core/AutoTasks/Strategies/GoldBalloonAutoTaskStrategy.cs) | Concrete automatic-task strategy; primary symbols: GoldBalloonAutoTaskStrategy |
| [LoopStageAutoTaskStrategy.cs](../../../../../BetterBTD/Core/AutoTasks/Strategies/LoopStageAutoTaskStrategy.cs) | Concrete automatic-task strategy; primary symbols: LoopStageAutoTaskStrategy |
| [OdysseyAutoTaskStrategy.cs](../../../../../BetterBTD/Core/AutoTasks/Strategies/OdysseyAutoTaskStrategy.cs) | Concrete automatic-task strategy; primary symbols: OdysseyAutoTaskStrategy |
| [RaceAutoTaskStrategy.cs](../../../../../BetterBTD/Core/AutoTasks/Strategies/RaceAutoTaskStrategy.cs) | Concrete automatic-task strategy; primary symbols: RaceAutoTaskStrategy |
| [StageNavigationAutoTaskStrategyBase.cs](../../../../../BetterBTD/Core/AutoTasks/Strategies/StageNavigationAutoTaskStrategyBase.cs) | Concrete automatic-task strategy; primary symbols: StageNavigationAutoTaskStrategyBase |

### `BetterBTD/Core/Config`

| File | Description |
| --- | --- |
| [HotKeyConfig.cs](../../../../../BetterBTD/Core/Config/HotKeyConfig.cs) | Core hot-key and key-binding configuration; primary symbols: HotKeyConfig |
| [KeyBindingsConfig.cs](../../../../../BetterBTD/Core/Config/KeyBindingsConfig.cs) | Core hot-key and key-binding configuration; primary symbols: KeyBindingsConfig, HotkeyBinding, TowerPlacementBindings, AbilityBindings, HeroInventoryBindings |

### `BetterBTD/Core/GameControl`

| File | Description |
| --- | --- |
| [GameControlLeaseCoordinator.cs](../../../../../BetterBTD/Core/GameControl/GameControlLeaseCoordinator.cs) | Shared game-control lease and input ownership; primary symbols: GameControlOwnerKind, GameControlLeaseCoordinator, GameControlLease, GameControlExecutionScope, GameControlLeaseContext |

### `BetterBTD/Core/RobotControl`

| File | Description |
| --- | --- |
| [RobotControlCore.cs](../../../../../BetterBTD/Core/RobotControl/RobotControlCore.cs) | Robot-control actions, registry, and coordination; primary symbols: RobotTaskConstants, RobotTaskRuntimeOptions, RobotActionContext, IRobotGameAction, IRobotUiAutomationRule |
| [RobotTaskCoordinator.cs](../../../../../BetterBTD/Core/RobotControl/RobotTaskCoordinator.cs) | Robot-control actions, registry, and coordination; primary symbols: RobotTaskCoordinator |

### `BetterBTD/Core/ScriptExecution`

| File | Description |
| --- | --- |
| [ScriptExecutionKeyBindingResolver.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptExecutionKeyBindingResolver.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptExecutionKeyBindingResolver |
| [ScriptExecutionOperations.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptExecutionOperations.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptRetryOptions, ScriptWaitOptions, ScriptExecutionException, ScriptExecutionOperations |
| [ScriptExecutionRuntimeContextBuilder.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptExecutionRuntimeContextBuilder.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: struct, ScriptExecutionRuntimeContextBuilder |
| [ScriptExecutionRuntimeLogging.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptExecutionRuntimeLogging.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptExecutionRuntimeLogger, ScriptExecutionPollingLogScope, ScriptExecutionRuntimeDiagnostics, PopWhenDisposed |
| [ScriptExecutionSession.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptExecutionSession.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptExecutionSession, AsyncManualResetEvent |
| [ScriptKeyBindingPreflightValidator.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptKeyBindingPreflightValidator.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptKeyBindingPreflightIssue, ScriptKeyBindingPreflightValidator, Requirement |
| [ScriptTaskFlowExecutor.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptTaskFlowExecutor.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptTaskFlowExecutor |
| [ScriptTaskFlowService.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptTaskFlowService.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptTaskFlowService |
| [ScriptTaskFlowWorker.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptTaskFlowWorker.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptTaskFlowWorker |
| [ScriptWorkerStateTransitions.cs](../../../../../BetterBTD/Core/ScriptExecution/ScriptWorkerStateTransitions.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: ScriptWorkerStateTransitions |

### `BetterBTD/Core/ScriptExecution/Handlers`

| File | Description |
| --- | --- |
| [ActivateAbilityInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/ActivateAbilityInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: ActivateAbilityInstructionHandler |
| [CommentInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/CommentInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: CommentInstructionHandler |
| [FreeplayBoundaryInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/FreeplayBoundaryInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: FreeplayBoundaryInstructionHandler |
| [IScriptInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/IScriptInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: IScriptInstructionHandler |
| [ModifyMonkeyCoordinateInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/ModifyMonkeyCoordinateInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: ModifyMonkeyCoordinateInstructionHandler |
| [MouseClickInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/MouseClickInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: MouseClickInstructionHandler |
| [NextRoundInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/NextRoundInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: NextRoundInstructionHandler |
| [PlaceHeroInventoryInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/PlaceHeroInventoryInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: PlaceHeroInventoryInstructionHandler |
| [PlaceMonkeyInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/PlaceMonkeyInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: PlaceMonkeyInstructionHandler |
| [ScriptInstructionHandlerBase.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/ScriptInstructionHandlerBase.cs) | Script instruction handlers and support code; primary symbols: ScriptInstructionHandlerBase |
| [ScriptInstructionHandlerRegistry.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/ScriptInstructionHandlerRegistry.cs) | Script instruction handlers and support code; primary symbols: ScriptInstructionHandlerRegistry |
| [SellMonkeyInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/SellMonkeyInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: SellMonkeyInstructionHandler |
| [SetMonkeyAbilityInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/SetMonkeyAbilityInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: SetMonkeyAbilityInstructionHandler |
| [SwitchMonkeyTargetInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/SwitchMonkeyTargetInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: SwitchMonkeyTargetInstructionHandler |
| [UpgradeMonkeyInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/UpgradeMonkeyInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: UpgradeMonkeyInstructionHandler |
| [WaitInstructionHandler.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/WaitInstructionHandler.cs) | Script instruction handlers and support code; primary symbols: WaitInstructionHandler |

### `BetterBTD/Core/ScriptExecution/Handlers/Support`

| File | Description |
| --- | --- |
| [ScriptInstructionHandlerSupport.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/Support/ScriptInstructionHandlerSupport.cs) | Script instruction handlers and support code; primary symbols: ScriptInstructionHandlerSupport |
| [UpgradePanelSide.cs](../../../../../BetterBTD/Core/ScriptExecution/Handlers/Support/UpgradePanelSide.cs) | Script instruction handlers and support code; primary symbols: UpgradePanelSide |

### `BetterBTD/Core/ScriptExecution/Runtime`

| File | Description |
| --- | --- |
| [ScriptExecutionRuntimeServices.cs](../../../../../BetterBTD/Core/ScriptExecution/Runtime/ScriptExecutionRuntimeServices.cs) | UI-independent script execution, sessions, and scheduling; primary symbols: IScriptCaptureService, IScriptInputService, IGameStageStateService, IScriptObservationService, IScriptTaskFlowExecutionEngine |

### `BetterBTD/Core/Simulator`

| File | Description |
| --- | --- |
| [InputSimulationCommand.cs](../../../../../BetterBTD/Core/Simulator/InputSimulationCommand.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: InputSimulationCommandType, InputSimulationCommand |
| [InputSimulationCommandBuilder.cs](../../../../../BetterBTD/Core/Simulator/InputSimulationCommandBuilder.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: InputSimulationCommandBuilder |
| [InputSimulationCommandDispatcher.cs](../../../../../BetterBTD/Core/Simulator/InputSimulationCommandDispatcher.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: IInputSimulationCommandDispatcher, InputSimulationCommandDispatcher |
| [KeyboardInputUtilities.cs](../../../../../BetterBTD/Core/Simulator/KeyboardInputUtilities.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: KeyboardInputUtilities |
| [MouseEventSimulator.cs](../../../../../BetterBTD/Core/Simulator/MouseEventSimulator.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: MouseEventSimulator |
| [PostMessageSimulator.cs](../../../../../BetterBTD/Core/Simulator/PostMessageSimulator.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: PostMessageSimulator |
| [Simulation.cs](../../../../../BetterBTD/Core/Simulator/Simulation.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: Simulation |

### `BetterBTD/Core/Simulator/Extensions`

| File | Description |
| --- | --- |
| [Enums.cs](../../../../../BetterBTD/Core/Simulator/Extensions/Enums.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: KeyType, BTDActions |
| [InputSimulatorExtension.cs](../../../../../BetterBTD/Core/Simulator/Extensions/InputSimulatorExtension.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: InputSimulatorExtension |
| [PostMessageSimulatorExtension.cs](../../../../../BetterBTD/Core/Simulator/Extensions/PostMessageSimulatorExtension.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: PostMessageSimulatorExtension |
| [SimulateKeyHelper.cs](../../../../../BetterBTD/Core/Simulator/Extensions/SimulateKeyHelper.cs) | Windows mouse/keyboard simulation and message dispatch; primary symbols: SimulateKeyHelper |

### `BetterBTD/Core/TestApi`

| File | Description |
| --- | --- |
| [TestApiCoordinator.cs](../../../../../BetterBTD/Core/TestApi/TestApiCoordinator.cs) | Internal black-box test-control coordinator; primary symbols: ITestApiController, ITestApiRuntimeEnvironment, TestApiCoordinator, OperationState, TestApiRequestException |

### `BetterBTD/Helpers`

| File | Description |
| --- | --- |
| [Base64Helper.cs](../../../../../BetterBTD/Helpers/Base64Helper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: Base64Helper |
| [DirectoryHelper.cs](../../../../../BetterBTD/Helpers/DirectoryHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: DirectoryHelper |
| [DpiHelper.cs](../../../../../BetterBTD/Helpers/DpiHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: DpiHelper, DpiScaleF |
| [ExpandObjectConverter.cs](../../../../../BetterBTD/Helpers/ExpandObjectConverter.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: ExpandObjectConverter |
| [GameElementCascadingItems.cs](../../../../../BetterBTD/Helpers/GameElementCascadingItems.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: GameElementCascadingItems |
| [MathHelper.cs](../../../../../BetterBTD/Helpers/MathHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: MathHelper |
| [NativeWindowHelper.cs](../../../../../BetterBTD/Helpers/NativeWindowHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: NativeWindowHelper, bool, RECT, POINT, struct |
| [OsVersionHelper.cs](../../../../../BetterBTD/Helpers/OsVersionHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: OsVersionHelper |
| [PrimaryScreen.cs](../../../../../BetterBTD/Helpers/PrimaryScreen.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: PrimaryScreen |
| [RegexHelper.cs](../../../../../BetterBTD/Helpers/RegexHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: RegexHelper |
| [ResourceHelper.cs](../../../../../BetterBTD/Helpers/ResourceHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: ResourceHelper |
| [RuntimeHelper.cs](../../../../../BetterBTD/Helpers/RuntimeHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters |
| [SemaphoreSlimParallel.cs](../../../../../BetterBTD/Helpers/SemaphoreSlimParallel.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: SemaphoreSlimParallel |
| [SpeedTimer.cs](../../../../../BetterBTD/Helpers/SpeedTimer.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: SpeedTimer |
| [StringUtils.cs](../../../../../BetterBTD/Helpers/StringUtils.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: StringUtils |
| [UserDataPathHelper.cs](../../../../../BetterBTD/Helpers/UserDataPathHelper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: UserDataPathHelper |

### `BetterBTD/Helpers/Extensions`

| File | Description |
| --- | --- |
| [BitmapExtension.cs](../../../../../BetterBTD/Helpers/Extensions/BitmapExtension.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: BitmapExtension |
| [ClickExtension.cs](../../../../../BetterBTD/Helpers/Extensions/ClickExtension.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: ClickExtension |
| [MatExtension.cs](../../../../../BetterBTD/Helpers/Extensions/MatExtension.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: MatExtension |
| [PointExtension.cs](../../../../../BetterBTD/Helpers/Extensions/PointExtension.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: PointExtension |
| [RectCutExtension.cs](../../../../../BetterBTD/Helpers/Extensions/RectCutExtension.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: RectCutExtension |
| [RectExtension.cs](../../../../../BetterBTD/Helpers/Extensions/RectExtension.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: RectExtension |

### `BetterBTD/Helpers/Http`

| File | Description |
| --- | --- |
| [HttpClientFactory.cs](../../../../../BetterBTD/Helpers/Http/HttpClientFactory.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: HttpClientFactory |
| [ProxySpeedTester.cs](../../../../../BetterBTD/Helpers/Http/ProxySpeedTester.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: ProxySpeedTester |

### `BetterBTD/Helpers/Security`

| File | Description |
| --- | --- |
| [MD5Helper.cs](../../../../../BetterBTD/Helpers/Security/MD5Helper.cs) | Cross-layer helpers, extensions, paths, and platform adapters; primary symbols: MD5Helper |
