# BetterBTD Tests

xUnit behavior, compatibility, protocol, and test-double coverage.

[Back to BetterBTD Source Index](../source-index.md)

## Related Indexes

- [BetterBTD Core and Helpers](./betterbtd-core.md)
- [BetterBTD Models](./betterbtd-models.md)
- [BetterBTD Services](./betterbtd-services.md)

## Directory Summary

| Directory | Files |
| --- | ---: |
| `BetterBTD.Tests` | 4 |
| `BetterBTD.Tests/AutoTasks` | 11 |
| `BetterBTD.Tests/Core/Simulator` | 1 |
| `BetterBTD.Tests/GameControl` | 1 |
| `BetterBTD.Tests/RobotControl` | 2 |
| `BetterBTD.Tests/ScriptExecution` | 3 |
| `BetterBTD.Tests/ScriptExecution/Handlers` | 6 |
| `BetterBTD.Tests/Services` | 18 |
| `BetterBTD.Tests/TestApi` | 4 |
| `BetterBTD.Tests/TestDoubles` | 4 |
| `BetterBTD.Tests/ViewModels` | 4 |

## File Inventory

Paths are relative to the repository root. Open the linked file and verify current behavior before editing.

### `BetterBTD.Tests`

| File | Description |
| --- | --- |
| [BetterBTD.Tests.csproj](../../../../../BetterBTD.Tests/BetterBTD.Tests.csproj) | xUnit behavior, regression, protocol, or test-double code |
| [GlobalUsings.cs](../../../../../BetterBTD.Tests/GlobalUsings.cs) | xUnit behavior, regression, protocol, or test-double code |
| [GlobalUsings.Services.cs](../../../../../BetterBTD.Tests/GlobalUsings.Services.cs) | xUnit behavior, regression, protocol, or test-double code |
| [XunitAssembly.cs](../../../../../BetterBTD.Tests/XunitAssembly.cs) | xUnit behavior, regression, protocol, or test-double code |

### `BetterBTD.Tests/AutoTasks`

| File | Description |
| --- | --- |
| [AutoTaskDependencyPreflightServiceTests.cs](../../../../../BetterBTD.Tests/AutoTasks/AutoTaskDependencyPreflightServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: AutoTaskDependencyPreflightServiceTests |
| [AutoTaskSkeletonTests.cs](../../../../../BetterBTD.Tests/AutoTasks/AutoTaskSkeletonTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: AutoTaskSkeletonTests, QueueGameUiStateService, RecordingGameUiActionExecutor, RecordingAutoTaskScriptResolver, RecordingAutoTaskScriptExecutor |
| [BlackBorderBadgeDetectionTests.cs](../../../../../BetterBTD.Tests/AutoTasks/BlackBorderBadgeDetectionTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: BlackBorderBadgeDetectionTests |
| [BlackBorderMapSearchStateMachineTests.cs](../../../../../BetterBTD.Tests/AutoTasks/BlackBorderMapSearchStateMachineTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: BlackBorderMapSearchStateMachineTests |
| [BlackBorderTaskCatalogTests.cs](../../../../../BetterBTD.Tests/AutoTasks/BlackBorderTaskCatalogTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: BlackBorderTaskCatalogTests |
| [CollectionAutoTaskStrategyTests.cs](../../../../../BetterBTD.Tests/AutoTasks/CollectionAutoTaskStrategyTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: CollectionAutoTaskStrategyTests |
| [GameUiDetectionConfigTests.cs](../../../../../BetterBTD.Tests/AutoTasks/GameUiDetectionConfigTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: GameUiDetectionConfigTests |
| [LoopStageAutoTaskStrategyTests.cs](../../../../../BetterBTD.Tests/AutoTasks/LoopStageAutoTaskStrategyTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: LoopStageAutoTaskStrategyTests |
| [ManagedAutoTaskScriptResolverTests.cs](../../../../../BetterBTD.Tests/AutoTasks/ManagedAutoTaskScriptResolverTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ManagedAutoTaskScriptResolverTests |
| [MapSearchFlowTests.cs](../../../../../BetterBTD.Tests/AutoTasks/MapSearchFlowTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: MapSearchFlowTests, StaticGameUiRecognizer |
| [RaceAutoTaskStrategyTests.cs](../../../../../BetterBTD.Tests/AutoTasks/RaceAutoTaskStrategyTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: RaceAutoTaskStrategyTests |

### `BetterBTD.Tests/Core/Simulator`

| File | Description |
| --- | --- |
| [KeyboardInputUtilitiesTests.cs](../../../../../BetterBTD.Tests/Core/Simulator/KeyboardInputUtilitiesTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: KeyboardInputUtilitiesTests, RecordingKeyboardSimulator, RecordingMouseSimulator, KeyboardCall |

### `BetterBTD.Tests/GameControl`

| File | Description |
| --- | --- |
| [GameControlLeaseCoordinatorTests.cs](../../../../../BetterBTD.Tests/GameControl/GameControlLeaseCoordinatorTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: GameControlLeaseCoordinatorTests |

### `BetterBTD.Tests/RobotControl`

| File | Description |
| --- | --- |
| [RobotTaskCoordinatorTests.cs](../../../../../BetterBTD.Tests/RobotControl/RobotTaskCoordinatorTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: RobotTaskCoordinatorTests, StaticGameUiStateService, TestRobotAction, BlockingRobotAction, MatchingUiAutomationRule |
| [RobotTaskRuntimeTests.cs](../../../../../BetterBTD.Tests/RobotControl/RobotTaskRuntimeTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: RobotTaskRuntimeTests, StaticGameUiStateService, DelayedCancellationRobotAction |

### `BetterBTD.Tests/ScriptExecution`

| File | Description |
| --- | --- |
| [ScriptExecutionIntervalStrategyTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/ScriptExecutionIntervalStrategyTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptExecutionIntervalStrategyTests |
| [ScriptKeyBindingPreflightValidatorTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/ScriptKeyBindingPreflightValidatorTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptKeyBindingPreflightValidatorTests |
| [ScriptTaskFlowExecutorGameControlTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/ScriptTaskFlowExecutorGameControlTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptTaskFlowExecutorGameControlTests |

### `BetterBTD.Tests/ScriptExecution/Handlers`

| File | Description |
| --- | --- |
| [MonkeyPanelInteractionHandlerTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/Handlers/MonkeyPanelInteractionHandlerTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: MonkeyPanelInteractionHandlerTests |
| [NextRoundInstructionHandlerTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/Handlers/NextRoundInstructionHandlerTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: NextRoundInstructionHandlerTests |
| [PlaceMonkeyInstructionHandlerTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/Handlers/PlaceMonkeyInstructionHandlerTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: PlaceMonkeyInstructionHandlerTests |
| [ScriptInstructionHandlerSupportTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/Handlers/ScriptInstructionHandlerSupportTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptInstructionHandlerSupportTests, ClickAwareGameStageStateService |
| [UpgradeMonkeyInstructionHandlerTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/Handlers/UpgradeMonkeyInstructionHandlerTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: UpgradeMonkeyInstructionHandlerTests |
| [WaitInstructionHandlerTests.cs](../../../../../BetterBTD.Tests/ScriptExecution/Handlers/WaitInstructionHandlerTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: WaitInstructionHandlerTests, RecordingCoordinateColorGameStageStateService |

### `BetterBTD.Tests/Services`

| File | Description |
| --- | --- |
| [ApplicationUpdateServiceTests.cs](../../../../../BetterBTD.Tests/Services/ApplicationUpdateServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ApplicationUpdateServiceTests |
| [Btd6SaveViewerServiceTests.cs](../../../../../BetterBTD.Tests/Services/Btd6SaveViewerServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: Btd6SaveViewerServiceTests |
| [ChildSessionRuntimeStateTests.cs](../../../../../BetterBTD.Tests/Services/ChildSessionRuntimeStateTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ChildSessionRuntimeStateTests |
| [GameElementCascadingItemsTests.cs](../../../../../BetterBTD.Tests/Services/GameElementCascadingItemsTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: GameElementCascadingItemsTests |
| [GameLaunchServiceTests.cs](../../../../../BetterBTD.Tests/Services/GameLaunchServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: GameLaunchServiceTests |
| [GameOcrIconMatcherTests.cs](../../../../../BetterBTD.Tests/Services/GameOcrIconMatcherTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: GameOcrIconMatcherTests |
| [HardwareInputSimulationServiceTests.cs](../../../../../BetterBTD.Tests/Services/HardwareInputSimulationServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: HardwareInputSimulationServiceTests |
| [LegacyScriptConversionServiceTests.cs](../../../../../BetterBTD.Tests/Services/LegacyScriptConversionServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: LegacyScriptConversionServiceTests |
| [ManagedScriptLibraryServiceTests.cs](../../../../../BetterBTD.Tests/Services/ManagedScriptLibraryServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ManagedScriptLibraryServiceTests |
| [ParagonStatsToolServiceTests.cs](../../../../../BetterBTD.Tests/Services/ParagonStatsToolServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ParagonStatsToolServiceTests |
| [ParagonToolServiceTests.cs](../../../../../BetterBTD.Tests/Services/ParagonToolServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ParagonToolServiceTests |
| [RoundCatalogServiceTests.cs](../../../../../BetterBTD.Tests/Services/RoundCatalogServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: RoundCatalogServiceTests |
| [ScriptDocumentServiceCompatibilityTests.cs](../../../../../BetterBTD.Tests/Services/ScriptDocumentServiceCompatibilityTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptDocumentServiceCompatibilityTests |
| [ScriptEditorInstructionServiceTests.cs](../../../../../BetterBTD.Tests/Services/ScriptEditorInstructionServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptEditorInstructionServiceTests |
| [ScriptEditorSequenceServiceTests.cs](../../../../../BetterBTD.Tests/Services/ScriptEditorSequenceServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptEditorSequenceServiceTests |
| [ScriptInputSimulationServiceTests.cs](../../../../../BetterBTD.Tests/Services/ScriptInputSimulationServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptInputSimulationServiceTests |
| [ScriptInstructionOptimizationServiceTests.cs](../../../../../BetterBTD.Tests/Services/ScriptInstructionOptimizationServiceTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptInstructionOptimizationServiceTests |
| [ScriptTagCatalogTests.cs](../../../../../BetterBTD.Tests/Services/ScriptTagCatalogTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptTagCatalogTests |

### `BetterBTD.Tests/TestApi`

| File | Description |
| --- | --- |
| [TestApiCoordinatorTests.cs](../../../../../BetterBTD.Tests/TestApi/TestApiCoordinatorTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: TestApiCoordinatorTests, FakeTestApiRuntimeEnvironment |
| [TestApiHttpServerTests.cs](../../../../../BetterBTD.Tests/TestApi/TestApiHttpServerTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: TestApiHttpServerTests, HealthOnlyRuntimeEnvironment, ExecuteOnlyController |
| [TestApiLaunchOptionsTests.cs](../../../../../BetterBTD.Tests/TestApi/TestApiLaunchOptionsTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: TestApiLaunchOptionsTests, TestApiTokenAuthenticatorTests |
| [TestApiRouteResolverTests.cs](../../../../../BetterBTD.Tests/TestApi/TestApiRouteResolverTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: TestApiRouteResolverTests |

### `BetterBTD.Tests/TestDoubles`

| File | Description |
| --- | --- |
| [FakeScriptRuntime.cs](../../../../../BetterBTD.Tests/TestDoubles/FakeScriptRuntime.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: NullScriptCaptureService, RecordingScriptInputService, RecordedClick, QueueGameStageStateService |
| [InputSimulationTestDoubles.cs](../../../../../BetterBTD.Tests/TestDoubles/InputSimulationTestDoubles.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: RecordingInputSimulationCommandDispatcher, FakeScriptInputSimulationEnvironment |
| [KeyBindingOverrideScope.cs](../../../../../BetterBTD.Tests/TestDoubles/KeyBindingOverrideScope.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: KeyBindingOverrideScope |
| [TestScriptExecutionContextFactory.cs](../../../../../BetterBTD.Tests/TestDoubles/TestScriptExecutionContextFactory.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: TestScriptExecutionContextFactory |

### `BetterBTD.Tests/ViewModels`

| File | Description |
| --- | --- |
| [CollectionScriptBindingWindowViewModelTests.cs](../../../../../BetterBTD.Tests/ViewModels/CollectionScriptBindingWindowViewModelTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: CollectionScriptBindingWindowViewModelTests |
| [MyScriptsPageViewModelTests.cs](../../../../../BetterBTD.Tests/ViewModels/MyScriptsPageViewModelTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: MyScriptsPageViewModelTests |
| [ScriptEditorPageViewModelTests.cs](../../../../../BetterBTD.Tests/ViewModels/ScriptEditorPageViewModelTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: ScriptEditorPageViewModelTests |
| [TaskRuntimeWindowViewModelTests.cs](../../../../../BetterBTD.Tests/ViewModels/TaskRuntimeWindowViewModelTests.cs) | xUnit behavior, regression, protocol, or test-double code; primary symbols: TaskRuntimeWindowViewModelTests, ManualTimeProvider |
