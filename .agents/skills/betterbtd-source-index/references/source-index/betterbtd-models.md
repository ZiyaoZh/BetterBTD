# BetterBTD Models

Configuration, game elements, scripts, task contracts, and runtime DTOs.

[Back to BetterBTD Source Index](../source-index.md)

## Related Indexes

- [BetterBTD Core and Helpers](./betterbtd-core.md)
- [BetterBTD Services](./betterbtd-services.md)
- [BetterBTD Tests](./betterbtd-tests.md)

## Directory Summary

| Directory | Files |
| --- | ---: |
| `BetterBTD/Models` | 18 |
| `BetterBTD/Models/AutoTasks` | 9 |
| `BetterBTD/Models/GameElements` | 6 |
| `BetterBTD/Models/MyScripts` | 5 |
| `BetterBTD/Models/RobotControl` | 1 |
| `BetterBTD/Models/Rounds` | 1 |
| `BetterBTD/Models/ScriptEditor` | 5 |
| `BetterBTD/Models/ScriptExecution` | 2 |
| `BetterBTD/Models/TestApi` | 1 |
| `BetterBTD/Models/Tools` | 3 |

## File Inventory

Paths are relative to the repository root. Open the linked file and verify current behavior before editing.

### `BetterBTD/Models`

| File | Description |
| --- | --- |
| [AppConfiguration.cs](../../../../../BetterBTD/Models/AppConfiguration.cs) | Application, task, input, and tool data models; primary symbols: AppConfiguration |
| [AutoTaskConfig.cs](../../../../../BetterBTD/Models/AutoTaskConfig.cs) | Application, task, input, and tool data models; primary symbols: AutoTaskConfig |
| [CaptureTestOverlayLayout.cs](../../../../../BetterBTD/Models/CaptureTestOverlayLayout.cs) | Application, task, input, and tool data models; primary symbols: CaptureTestOverlayLayout, CaptureTestOverlayPointGroup |
| [CaptureTestStageStateDisplayModel.cs](../../../../../BetterBTD/Models/CaptureTestStageStateDisplayModel.cs) | Application, task, input, and tool data models; primary symbols: CaptureTestStageStateDisplayModel |
| [DeviceDisplayInfo.cs](../../../../../BetterBTD/Models/DeviceDisplayInfo.cs) | Application, task, input, and tool data models; primary symbols: struct |
| [GameCaptureOptions.cs](../../../../../BetterBTD/Models/GameCaptureOptions.cs) | Application, task, input, and tool data models; primary symbols: class |
| [GameWindowInfo.cs](../../../../../BetterBTD/Models/GameWindowInfo.cs) | Application, task, input, and tool data models; primary symbols: struct |
| [HotKey.cs](../../../../../BetterBTD/Models/HotKey.cs) | Application, task, input, and tool data models; primary symbols: struct |
| [HotKeyTypeEnum.cs](../../../../../BetterBTD/Models/HotKeyTypeEnum.cs) | Application, task, input, and tool data models; primary symbols: HotKeyTypeEnum, HotKeyTypeEnumExtension |
| [KeyBindingSettingItem.cs](../../../../../BetterBTD/Models/KeyBindingSettingItem.cs) | Application, task, input, and tool data models; primary symbols: KeyBindingSettingItem |
| [KeyboardMouseSimulationMode.cs](../../../../../BetterBTD/Models/KeyboardMouseSimulationMode.cs) | Application, task, input, and tool data models; primary symbols: KeyboardMouseSimulationMode, KeyboardMouseSimulationModeExtensions |
| [LanguageOption.cs](../../../../../BetterBTD/Models/LanguageOption.cs) | Application, task, input, and tool data models; primary symbols: LanguageOption |
| [MapTemplateMatchResult.cs](../../../../../BetterBTD/Models/MapTemplateMatchResult.cs) | Application, task, input, and tool data models; primary symbols: MapTemplateMatchResult |
| [NavigationItem.cs](../../../../../BetterBTD/Models/NavigationItem.cs) | Application, task, input, and tool data models; primary symbols: NavigationItem |
| [TaskModule.cs](../../../../../BetterBTD/Models/TaskModule.cs) | Application, task, input, and tool data models; primary symbols: TaskModule |
| [TemplateMatchInfo.cs](../../../../../BetterBTD/Models/TemplateMatchInfo.cs) | Application, task, input, and tool data models; primary symbols: struct |
| [TemplateMatchOptions.cs](../../../../../BetterBTD/Models/TemplateMatchOptions.cs) | Application, task, input, and tool data models; primary symbols: struct |
| [ThemeOption.cs](../../../../../BetterBTD/Models/ThemeOption.cs) | Application, task, input, and tool data models; primary symbols: ThemeOption |

### `BetterBTD/Models/AutoTasks`

| File | Description |
| --- | --- |
| [AutoTaskExecutionModels.cs](../../../../../BetterBTD/Models/AutoTasks/AutoTaskExecutionModels.cs) | Automatic-task configuration and runtime models; primary symbols: AutoTaskKind, LoopStageRunMode, AutoTaskRunState, AutoTaskPhase, AutoTaskActivityKind |
| [BlackBorderAutoTaskModels.cs](../../../../../BetterBTD/Models/AutoTasks/BlackBorderAutoTaskModels.cs) | Automatic-task configuration and runtime models; primary symbols: BlackBorderAutoTaskScriptRunState, BlackBorderAutoTaskStageTask, BlackBorderAutoTaskScriptContext, BlackBorderAutoTaskStateKeys |
| [CollectionAutoTaskModels.cs](../../../../../BetterBTD/Models/AutoTasks/CollectionAutoTaskModels.cs) | Automatic-task configuration and runtime models; primary symbols: CollectionAutoTaskScriptRunState, CollectionAutoTaskScriptContext, CollectionAutoTaskStateKeys |
| [GameUiDetectionModels.cs](../../../../../BetterBTD/Models/AutoTasks/GameUiDetectionModels.cs) | Automatic-task configuration and runtime models; primary symbols: GameUiColorComparisonOperator, GameUiDetectionConfig, GameUiDetectionRule, GameUiColorCondition |
| [GoldBalloonAutoTaskModels.cs](../../../../../BetterBTD/Models/AutoTasks/GoldBalloonAutoTaskModels.cs) | Automatic-task configuration and runtime models; primary symbols: GoldBalloonAutoTaskScriptRunState, GoldBalloonAutoTaskScriptContext, GoldBalloonAutoTaskStateKeys |
| [LoopStageAutoTaskModels.cs](../../../../../BetterBTD/Models/AutoTasks/LoopStageAutoTaskModels.cs) | Automatic-task configuration and runtime models; primary symbols: LoopStageScriptRunState, LoopStageAutoTaskScriptContext, LoopStageAutoTaskStateKeys, LoopStageRoundProgressTracker |
| [NavigationCoordinationModels.cs](../../../../../BetterBTD/Models/AutoTasks/NavigationCoordinationModels.cs) | Automatic-task configuration and runtime models; primary symbols: StageChallengeState, NavigationObservation, StageChallengeStateTransition |
| [OdysseyAutoTaskModels.cs](../../../../../BetterBTD/Models/AutoTasks/OdysseyAutoTaskModels.cs) | Automatic-task configuration and runtime models; primary symbols: OdysseyAutoTaskScriptRunState, OdysseyAutoTaskStateKeys |
| [RaceAutoTaskModels.cs](../../../../../BetterBTD/Models/AutoTasks/RaceAutoTaskModels.cs) | Automatic-task configuration and runtime models; primary symbols: RaceAutoTaskScriptRunState, RaceAutoTaskStateKeys |

### `BetterBTD/Models/GameElements`

| File | Description |
| --- | --- |
| [ActivatedAbility.cs](../../../../../BetterBTD/Models/GameElements/ActivatedAbility.cs) | Stable game-element, map, hero, and tower identifiers; primary symbols: ActivatedAbilityType |
| [GameElementCatalog.cs](../../../../../BetterBTD/Models/GameElements/GameElementCatalog.cs) | Stable game-element, map, hero, and tower identifiers; primary symbols: MonkeyTowerDefinition, HeroDefinition, MapDefinition, InventoryDefinition, ActivatedAbilityDefinition |
| [HeroType.cs](../../../../../BetterBTD/Models/GameElements/HeroType.cs) | Stable game-element, map, hero, and tower identifiers; primary symbols: HeroType |
| [Inventory.cs](../../../../../BetterBTD/Models/GameElements/Inventory.cs) | Stable game-element, map, hero, and tower identifiers; primary symbols: InventoryType |
| [MapDefinitions.cs](../../../../../BetterBTD/Models/GameElements/MapDefinitions.cs) | Stable game-element, map, hero, and tower identifiers; primary symbols: MapDifficultyTier, GameMapType, StageDifficulty, StageMode |
| [MonkeyTowerType.cs](../../../../../BetterBTD/Models/GameElements/MonkeyTowerType.cs) | Stable game-element, map, hero, and tower identifiers; primary symbols: MonkeyTowerCategory, MonkeyTowerType |

### `BetterBTD/Models/MyScripts`

| File | Description |
| --- | --- |
| [BlackBorderScriptSubscriptionModels.cs](../../../../../BetterBTD/Models/MyScripts/BlackBorderScriptSubscriptionModels.cs) | Script-library and script-document models; primary symbols: BlackBorderScriptSubscriptionDocument, BlackBorderSubscriptionDescriptor |
| [CollectionScriptSubscriptionModels.cs](../../../../../BetterBTD/Models/MyScripts/CollectionScriptSubscriptionModels.cs) | Script-library and script-document models; primary symbols: CollectionScriptSubscriptionDocument, CollectionScriptSubscriptionScriptDocument |
| [GoldBalloonScriptSubscriptionModels.cs](../../../../../BetterBTD/Models/MyScripts/GoldBalloonScriptSubscriptionModels.cs) | Script-library and script-document models; primary symbols: GoldBalloonScriptSubscriptionDocument |
| [ManagedScriptLibraryModels.cs](../../../../../BetterBTD/Models/MyScripts/ManagedScriptLibraryModels.cs) | Script-library and script-document models; primary symbols: ManagedScriptLibraryDocument, ManagedScriptTaskBindingDocument, ManagedScriptAssetRecord, ManagedScriptSlotBindingRecord, ManagedScriptLibrarySnapshot |
| [SubscriptionImportProgress.cs](../../../../../BetterBTD/Models/MyScripts/SubscriptionImportProgress.cs) | Script-library and script-document models; primary symbols: SubscriptionImportProgress |

### `BetterBTD/Models/RobotControl`

| File | Description |
| --- | --- |
| [RobotControlModels.cs](../../../../../BetterBTD/Models/RobotControl/RobotControlModels.cs) | Application, task, input, and tool data models; primary symbols: RobotTaskRunState, RobotActionExecutionStatus, RobotActionParameterType, RobotActionErrorCodes, RobotActionParameterDescriptor |

### `BetterBTD/Models/Rounds`

| File | Description |
| --- | --- |
| [RoundCatalogModels.cs](../../../../../BetterBTD/Models/Rounds/RoundCatalogModels.cs) | Application, task, input, and tool data models; primary symbols: RoundBloonType, RoundCatalog, RoundDefinition, RoundBloonEntry, RoundRangeSummary |

### `BetterBTD/Models/ScriptEditor`

| File | Description |
| --- | --- |
| [LegacyScriptModels.cs](../../../../../BetterBTD/Models/ScriptEditor/LegacyScriptModels.cs) | Script-editor input and presentation models; primary symbols: LegacyScriptFormat, LegacyActionType, LegacyUpgradeType, LegacyTargetType, LegacyMonkeyFunctionType |
| [ScriptDocumentModels.cs](../../../../../BetterBTD/Models/ScriptEditor/ScriptDocumentModels.cs) | Script-editor input and presentation models; primary symbols: ScriptDocumentFormat, ScriptDocument, ScriptMetadataDocument, ScriptMonkeyObjectDocument, ScriptInstructionDocument |
| [ScriptInstructionModels.cs](../../../../../BetterBTD/Models/ScriptEditor/ScriptInstructionModels.cs) | Script-editor input and presentation models; primary symbols: ScriptCommandType, ScriptCommandTypeExtensions, UpgradePathType, SwitchDirectionType, MonkeyAbilityType |
| [ScriptTagCatalog.cs](../../../../../BetterBTD/Models/ScriptEditor/ScriptTagCatalog.cs) | Script-editor input and presentation models; primary symbols: ScriptTagDefinition, ScriptTagCatalog |
| [ScriptUpgradeLevelState.cs](../../../../../BetterBTD/Models/ScriptEditor/ScriptUpgradeLevelState.cs) | Script-editor input and presentation models; primary symbols: struct |

### `BetterBTD/Models/ScriptExecution`

| File | Description |
| --- | --- |
| [ScriptExecutionModels.cs](../../../../../BetterBTD/Models/ScriptExecution/ScriptExecutionModels.cs) | Script instruction, log, and session models; primary symbols: ScriptExecutionStatus, ScriptExecutionRunState, ScriptExecutionOperationIntervalStrategy, ScriptExecutionWindowSettings, ScriptExecutionOptions |
| [ScriptWorkerModels.cs](../../../../../BetterBTD/Models/ScriptExecution/ScriptWorkerModels.cs) | Script instruction, log, and session models; primary symbols: ScriptWorkerState, ScriptWorkerCommandKind, ScriptWorkerEventKind, ScriptWorkerStartRequest, ScriptWorkerCommand |

### `BetterBTD/Models/TestApi`

| File | Description |
| --- | --- |
| [TestApiModels.cs](../../../../../BetterBTD/Models/TestApi/TestApiModels.cs) | Test API request, response, and operation models; primary symbols: TestApiConstants, TestApiErrorCodes, TestApiOperationStatus, TestApiHealthResponse, TestApiHealthDiagnostics |

### `BetterBTD/Models/Tools`

| File | Description |
| --- | --- |
| [Btd6SaveDocument.cs](../../../../../BetterBTD/Models/Tools/Btd6SaveDocument.cs) | Application, task, input, and tool data models; primary symbols: Btd6SaveDocument, Btd6SaveSummaryItem |
| [ToolCalculationRequests.cs](../../../../../BetterBTD/Models/Tools/ToolCalculationRequests.cs) | Application, task, input, and tool data models; primary symbols: RoundToolRequest, HeroToolRequest, ParagonToolRequest, ParagonStatsToolRequest |
| [ToolOptionRefreshResult.cs](../../../../../BetterBTD/Models/Tools/ToolOptionRefreshResult.cs) | Application, task, input, and tool data models; primary symbols: ToolOptionRefreshResult |
