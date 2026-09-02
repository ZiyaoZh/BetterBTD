# BetterBTD Services

Capture, recognition, persistence, settings, editor, task adapters, and protocols.

[Back to BetterBTD Source Index](../source-index.md)

## Related Indexes

- [BetterBTD Core and Helpers](./betterbtd-core.md)
- [BetterBTD Models](./betterbtd-models.md)
- [BetterBTD Presentation](./betterbtd-presentation.md)
- [BetterBTD Tests](./betterbtd-tests.md)

## Directory Summary

| Directory | Files |
| --- | ---: |
| `BetterBTD/Services/ChildSession` | 7 |
| `BetterBTD/Services/Diagnostics` | 2 |
| `BetterBTD/Services/Editor` | 3 |
| `BetterBTD/Services/MyScripts` | 9 |
| `BetterBTD/Services/Settings` | 3 |
| `BetterBTD/Services/Shared` | 3 |
| `BetterBTD/Services/Shell/Localization` | 14 |
| `BetterBTD/Services/Start` | 1 |
| `BetterBTD/Services/Start/Capture` | 4 |
| `BetterBTD/Services/Tasks/AutoTasks` | 18 |
| `BetterBTD/Services/Tasks/CaptureAnalysis` | 5 |
| `BetterBTD/Services/Tasks/Input` | 3 |
| `BetterBTD/Services/Tasks/RobotControl` | 2 |
| `BetterBTD/Services/Tasks/ScriptExecution` | 1 |
| `BetterBTD/Services/Tasks/TestApi` | 4 |
| `BetterBTD/Services/Tools` | 8 |
| `BetterBTD/Services/Updates` | 1 |

## File Inventory

Paths are relative to the repository root. Open the linked file and verify current behavior before editing.

### `BetterBTD/Services/ChildSession`

| File | Description |
| --- | --- |
| [ChildSessionConnectionFailedEventArgs.cs](../../../../../BetterBTD/Services/ChildSession/ChildSessionConnectionFailedEventArgs.cs) | Application services and infrastructure; primary symbols: ChildSessionConnectionFailedEventArgs |
| [ChildSessionControlChannel.cs](../../../../../BetterBTD/Services/ChildSession/ChildSessionControlChannel.cs) | Application services and infrastructure; primary symbols: ChildSessionControlServer, ChildSessionControlClient |
| [ChildSessionNativeMethods.cs](../../../../../BetterBTD/Services/ChildSession/ChildSessionNativeMethods.cs) | Application services and infrastructure; primary symbols: ChildSessionNativeMethods |
| [ChildSessionProcessLauncher.cs](../../../../../BetterBTD/Services/ChildSession/ChildSessionProcessLauncher.cs) | Application services and infrastructure; primary symbols: ChildSessionProcessLauncher, ProcessLaunchInfo |
| [ChildSessionRuntimeState.cs](../../../../../BetterBTD/Services/ChildSession/ChildSessionRuntimeState.cs) | Application services and infrastructure; primary symbols: ChildSessionRuntimeState |
| [ChildSessionService.cs](../../../../../BetterBTD/Services/ChildSession/ChildSessionService.cs) | Application services and infrastructure; primary symbols: ChildSessionService |
| [InstanceLaunchOptions.cs](../../../../../BetterBTD/Services/ChildSession/InstanceLaunchOptions.cs) | Application services and infrastructure; primary symbols: BetterBtdInstanceRole, InstanceLaunchOptions |

### `BetterBTD/Services/Diagnostics`

| File | Description |
| --- | --- |
| [DiagnosticsFileLogWriter.cs](../../../../../BetterBTD/Services/Diagnostics/DiagnosticsFileLogWriter.cs) | Capture and runtime diagnostics; primary symbols: DiagnosticsFileLogWriter, DiagnosticsLogFilePathFactory |
| [GameCaptureDiagnosticsService.cs](../../../../../BetterBTD/Services/Diagnostics/GameCaptureDiagnosticsService.cs) | Capture and runtime diagnostics; primary symbols: GameCaptureDiagnosticsService |

### `BetterBTD/Services/Editor`

| File | Description |
| --- | --- |
| [ScriptEditorInstructionService.cs](../../../../../BetterBTD/Services/Editor/ScriptEditorInstructionService.cs) | Script-editor instruction, option, and sequence services; primary symbols: ScriptEditorInstructionService |
| [ScriptEditorOptionService.cs](../../../../../BetterBTD/Services/Editor/ScriptEditorOptionService.cs) | Script-editor instruction, option, and sequence services; primary symbols: ScriptEditorOptionService, ScriptEditorParameterOptions, ScriptEditorMetadataOptions |
| [ScriptEditorSequenceService.cs](../../../../../BetterBTD/Services/Editor/ScriptEditorSequenceService.cs) | Script-editor instruction, option, and sequence services; primary symbols: ScriptEditorSequenceService |

### `BetterBTD/Services/MyScripts`

| File | Description |
| --- | --- |
| [BlackBorderScriptSubscriptionService.cs](../../../../../BetterBTD/Services/MyScripts/BlackBorderScriptSubscriptionService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: BlackBorderScriptSubscriptionService |
| [CollectionScriptSubscriptionService.cs](../../../../../BetterBTD/Services/MyScripts/CollectionScriptSubscriptionService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: CollectionScriptSubscriptionService |
| [GoldBalloonScriptSubscriptionService.cs](../../../../../BetterBTD/Services/MyScripts/GoldBalloonScriptSubscriptionService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: GoldBalloonScriptSubscriptionService |
| [LegacyScriptConversionService.cs](../../../../../BetterBTD/Services/MyScripts/LegacyScriptConversionService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: LegacyScriptConversionService, LegacyConversionContext, LegacyScriptConversionResult, LegacyMonkeyBindingState |
| [LegacyScriptDocumentService.cs](../../../../../BetterBTD/Services/MyScripts/LegacyScriptDocumentService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: LegacyScriptDocumentService |
| [ManagedScriptLibraryService.cs](../../../../../BetterBTD/Services/MyScripts/ManagedScriptLibraryService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: ManagedScriptLibraryService, is, in |
| [ManagedScriptSlotCatalogService.cs](../../../../../BetterBTD/Services/MyScripts/ManagedScriptSlotCatalogService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: ManagedScriptSlotCatalogService |
| [ScriptDocumentService.cs](../../../../../BetterBTD/Services/MyScripts/ScriptDocumentService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: ScriptDocumentService, ScriptDocumentSourceKind, ScriptDocumentLoadResult |
| [ScriptInstructionOptimizationService.cs](../../../../../BetterBTD/Services/MyScripts/ScriptInstructionOptimizationService.cs) | Script documents, library, compatibility conversion, and bindings; primary symbols: ScriptInstructionOptimizationService |

### `BetterBTD/Services/Settings`

| File | Description |
| --- | --- |
| [ConfigurationService.cs](../../../../../BetterBTD/Services/Settings/ConfigurationService.cs) | Configuration, theme, and device settings; primary symbols: ConfigurationService |
| [DeviceInfoService.cs](../../../../../BetterBTD/Services/Settings/DeviceInfoService.cs) | Configuration, theme, and device settings; primary symbols: DeviceInfoService |
| [ThemeService.cs](../../../../../BetterBTD/Services/Settings/ThemeService.cs) | Configuration, theme, and device settings; primary symbols: ThemeService |

### `BetterBTD/Services/Shared`

| File | Description |
| --- | --- |
| [AppDialogService.cs](../../../../../BetterBTD/Services/Shared/AppDialogService.cs) | Services shared across pages and task flows; primary symbols: AppDialogResult, AppDialogRequest, AppDialogService |
| [ImportProgressDialogService.cs](../../../../../BetterBTD/Services/Shared/ImportProgressDialogService.cs) | Services shared across pages and task flows; primary symbols: ImportProgressDialogRequest, ImportProgressDialogHandle, ImportProgressDialogService |
| [RoundCatalogService.cs](../../../../../BetterBTD/Services/Shared/RoundCatalogService.cs) | Services shared across pages and task flows; primary symbols: RoundCatalogService, struct |

### `BetterBTD/Services/Shell/Localization`

| File | Description |
| --- | --- |
| [LocalizationService.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.CaptureTest.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.CaptureTest.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.ChildSession.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.ChildSession.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.Editor.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.Editor.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.Editor.PlaceMonkey.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.Editor.PlaceMonkey.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.GameElements.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.GameElements.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.Library.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.Library.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.Shell.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.Shell.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.Start.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.Start.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.Tasks.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.Tasks.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.TextEditor.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.TextEditor.cs) | Localization resources and display text; primary symbols: LocalizationService |
| [LocalizationService.Resources.Tools.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Resources.Tools.cs) | Localization resources and display text; primary symbols: LocalizationService, targets |
| [LocalizationService.Settings.cs](../../../../../BetterBTD/Services/Shell/Localization/LocalizationService.Settings.cs) | Localization resources and display text; primary symbols: LocalizationService, language |

### `BetterBTD/Services/Start`

| File | Description |
| --- | --- |
| [GameLaunchService.cs](../../../../../BetterBTD/Services/Start/GameLaunchService.cs) | Startup flow and start-page services; primary symbols: GameLaunchResult, GameLaunchService |

### `BetterBTD/Services/Start/Capture`

| File | Description |
| --- | --- |
| [CaptureTestStageStateDisplayService.cs](../../../../../BetterBTD/Services/Start/Capture/CaptureTestStageStateDisplayService.cs) | Target-window discovery, capture sessions, and capture diagnostics; primary symbols: CaptureTestStageStateDisplayService |
| [GameCaptureService.cs](../../../../../BetterBTD/Services/Start/Capture/GameCaptureService.cs) | Target-window discovery, capture sessions, and capture diagnostics; primary symbols: GameCaptureService |
| [GameWindowInfoService.cs](../../../../../BetterBTD/Services/Start/Capture/GameWindowInfoService.cs) | Target-window discovery, capture sessions, and capture diagnostics; primary symbols: GameWindowInfoService |
| [MaskWindowService.cs](../../../../../BetterBTD/Services/Start/Capture/MaskWindowService.cs) | Target-window discovery, capture sessions, and capture diagnostics; primary symbols: MaskWindowService |

### `BetterBTD/Services/Tasks/AutoTasks`

| File | Description |
| --- | --- |
| [AutoTaskGameUiActionHandlerBase.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/AutoTaskGameUiActionHandlerBase.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: AutoTaskGameUiActionHandlerBase |
| [AutoTaskRuntimeAdapters.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/AutoTaskRuntimeAdapters.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: ManagedAutoTaskScriptResolver, ScriptTaskFlowAutoTaskScriptExecutorAdapter, AutoTaskRuntimeServiceFactory |
| [AutoTaskRuntimeScriptPreviewService.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/AutoTaskRuntimeScriptPreviewService.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: AutoTaskRuntimeScriptPreviewService, AutoTaskRuntimeScriptPreview |
| [BlackBorderBadgeDetection.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/BlackBorderBadgeDetection.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: BlackBorderBadgeDetection, struct, BlackBorderBadgeState |
| [BlackBorderGameUiActionHandler.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/BlackBorderGameUiActionHandler.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: BlackBorderGameUiActionHandler |
| [BlackBorderMapSearchStateMachine.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/BlackBorderMapSearchStateMachine.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: BlackBorderMapSearchStateMachine, struct |
| [CollectionGameUiActionHandler.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/CollectionGameUiActionHandler.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: CollectionGameUiActionHandler |
| [GameUiActionExecutor.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/GameUiActionExecutor.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: GameUiActionExecutor |
| [GameUiDetectionConfigService.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/GameUiDetectionConfigService.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: GameUiDetectionConfigService |
| [GameUiDetectionRuleEvaluator.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/GameUiDetectionRuleEvaluator.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: GameUiDetectionRuleEvaluator, struct |
| [GameUiNavigator.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/GameUiNavigator.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: GameUiNavigator |
| [GameUiStateService.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/GameUiStateService.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: GameUiStateService, ConfiguredGameUiRecognizer, UnknownGameUiRecognizer |
| [GoldBalloonGameUiActionHandler.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/GoldBalloonGameUiActionHandler.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: GoldBalloonGameUiActionHandler |
| [IGameUiTaskActionHandler.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/IGameUiTaskActionHandler.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: IGameUiTaskActionHandler |
| [LoopStageGameUiActionHandler.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/LoopStageGameUiActionHandler.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: LoopStageGameUiActionHandler |
| [OdysseyGameUiActionHandler.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/OdysseyGameUiActionHandler.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: OdysseyGameUiActionHandler |
| [RaceGameUiActionHandler.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/RaceGameUiActionHandler.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: RaceGameUiActionHandler |
| [UnimplementedGameUiElementLocator.cs](../../../../../BetterBTD/Services/Tasks/AutoTasks/UnimplementedGameUiElementLocator.cs) | Automatic-task UI state, navigation, actions, and adapters; primary symbols: UnimplementedGameUiElementLocator |

### `BetterBTD/Services/Tasks/CaptureAnalysis`

| File | Description |
| --- | --- |
| [GameOcrSupport.cs](../../../../../BetterBTD/Services/Tasks/CaptureAnalysis/GameOcrSupport.cs) | Screenshot analysis, OCR, matching, and game-state recognition; primary symbols: GameOcrSupport, GameOcrIconMatcher, OcrValueType, TemplateResolution, UiNavigationButtonType |
| [GameStageChallengeOcrService.cs](../../../../../BetterBTD/Services/Tasks/CaptureAnalysis/GameStageChallengeOcrService.cs) | Screenshot analysis, OCR, matching, and game-state recognition; primary symbols: GameStageChallengeOcrService, OcrCandidate, ThresholdRecognitionResult |
| [GameStageStateService.cs](../../../../../BetterBTD/Services/Tasks/CaptureAnalysis/GameStageStateService.cs) | Screenshot analysis, OCR, matching, and game-state recognition; primary symbols: GameStageStateService, struct |
| [GameUiNavigationOcrService.cs](../../../../../BetterBTD/Services/Tasks/CaptureAnalysis/GameUiNavigationOcrService.cs) | Screenshot analysis, OCR, matching, and game-state recognition; primary symbols: GameUiNavigationOcrService |
| [TemplateMatchService.cs](../../../../../BetterBTD/Services/Tasks/CaptureAnalysis/TemplateMatchService.cs) | Screenshot analysis, OCR, matching, and game-state recognition; primary symbols: TemplateMatchService |

### `BetterBTD/Services/Tasks/Input`

| File | Description |
| --- | --- |
| [CoordinateTransformService.cs](../../../../../BetterBTD/Services/Tasks/Input/CoordinateTransformService.cs) | Script input, hardware input, and coordinate transforms; primary symbols: CoordinateTransformService |
| [HardwareInputSimulationService.cs](../../../../../BetterBTD/Services/Tasks/Input/HardwareInputSimulationService.cs) | Script input, hardware input, and coordinate transforms; primary symbols: HardwareInputSimulationService, DeviceEntry, EventHandleBuffer, KeyboardInputData, MouseInputData |
| [ScriptInputSimulationService.cs](../../../../../BetterBTD/Services/Tasks/Input/ScriptInputSimulationService.cs) | Script input, hardware input, and coordinate transforms; primary symbols: ScriptInputSimulationService, IScriptInputSimulationEnvironment, ScriptInputSimulationEnvironment |

### `BetterBTD/Services/Tasks/RobotControl`

| File | Description |
| --- | --- |
| [RobotTaskHttpServer.cs](../../../../../BetterBTD/Services/Tasks/RobotControl/RobotTaskHttpServer.cs) | Robot-control service adapters; primary symbols: RobotTaskHttpServer, RobotActionHttpRequestPayload |
| [RobotTaskRuntime.cs](../../../../../BetterBTD/Services/Tasks/RobotControl/RobotTaskRuntime.cs) | Robot-control service adapters; primary symbols: RobotTaskRuntime |

### `BetterBTD/Services/Tasks/ScriptExecution`

| File | Description |
| --- | --- |
| [ScriptExecutionRuntimeAdapters.cs](../../../../../BetterBTD/Services/Tasks/ScriptExecution/ScriptExecutionRuntimeAdapters.cs) | Services adapted for the core script executor; primary symbols: ScriptExecutionRuntimeServiceFactory, ScriptCaptureServiceAdapter, ScriptInputServiceAdapter |

### `BetterBTD/Services/Tasks/TestApi`

| File | Description |
| --- | --- |
| [TestApiHttpServer.cs](../../../../../BetterBTD/Services/Tasks/TestApi/TestApiHttpServer.cs) | Test API HTTP transport and service adapters; primary symbols: TestApiHttpServer, TestApiRoute, TestApiRouteResolver |
| [TestApiLaunchOptions.cs](../../../../../BetterBTD/Services/Tasks/TestApi/TestApiLaunchOptions.cs) | Test API HTTP transport and service adapters; primary symbols: TestApiLaunchOptions, TestApiListenUrl, TestApiTokenAuthenticator |
| [TestApiRuntime.cs](../../../../../BetterBTD/Services/Tasks/TestApi/TestApiRuntime.cs) | Test API HTTP transport and service adapters; primary symbols: TestApiRuntime |
| [TestApiRuntimeEnvironment.cs](../../../../../BetterBTD/Services/Tasks/TestApi/TestApiRuntimeEnvironment.cs) | Test API HTTP transport and service adapters; primary symbols: TestApiRuntimeEnvironment |

### `BetterBTD/Services/Tools`

| File | Description |
| --- | --- |
| [Btd6SaveViewerService.cs](../../../../../BetterBTD/Services/Tools/Btd6SaveViewerService.cs) | Round, hero, collection, save, and other tool services; primary symbols: Btd6SaveViewerService |
| [HeroToolService.cs](../../../../../BetterBTD/Services/Tools/HeroToolService.cs) | Round, hero, collection, save, and other tool services; primary symbols: HeroToolService |
| [ParagonStatsToolService.cs](../../../../../BetterBTD/Services/Tools/ParagonStatsToolService.cs) | Round, hero, collection, save, and other tool services; primary symbols: ParagonStatsToolService, ParagonStatsToolResult |
| [ParagonToolCatalog.cs](../../../../../BetterBTD/Services/Tools/ParagonToolCatalog.cs) | Round, hero, collection, save, and other tool services; primary symbols: ParagonMonkeyDefinition, ParagonToolCatalog |
| [ParagonToolService.cs](../../../../../BetterBTD/Services/Tools/ParagonToolService.cs) | Round, hero, collection, save, and other tool services; primary symbols: ParagonToolService, ParagonDegreeToolResult |
| [PlacementAssistService.cs](../../../../../BetterBTD/Services/Tools/PlacementAssistService.cs) | Round, hero, collection, save, and other tool services; primary symbols: PlacementAssistService, IntPtr, Kbdllhookstruct |
| [RoundToolService.cs](../../../../../BetterBTD/Services/Tools/RoundToolService.cs) | Round, hero, collection, save, and other tool services; primary symbols: RoundToolService |
| [ToolsOptionService.cs](../../../../../BetterBTD/Services/Tools/ToolsOptionService.cs) | Round, hero, collection, save, and other tool services; primary symbols: ToolsOptionService |

### `BetterBTD/Services/Updates`

| File | Description |
| --- | --- |
| [ApplicationUpdateService.cs](../../../../../BetterBTD/Services/Updates/ApplicationUpdateService.cs) | Application update checks and downloads; primary symbols: ApplicationUpdateService, ApplicationReleaseInfo, ApplicationUpdateCheckResult, GitHubReleaseResponse |
