using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Helpers;
using BetterBTD.Models;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;
using BetterBTD.Views.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using Microsoft.Win32;
using Vanara.PInvoke;
using Wpf.Ui.Violeta.Controls;

namespace BetterBTD.ViewModels;

public sealed class ScriptEditorPageViewModel : ObservableObject, IDropTarget
{
    private enum CoordinateSelectionTarget
    {
        None,
        Position,
        Ability,
        WaitColor
    }

    private const string ScriptFileExtension = ".btd";
    private const string ScriptOpenFileDialogFilter = "BetterBTD Script (*.btd;*.btd6;*.json)|*.btd;*.btd6;*.json|BetterBTD Script (*.btd)|*.btd|Legacy BTD6 Script (*.btd6)|*.btd6|JSON File (*.json)|*.json|All Files (*.*)|*.*";
    private const string ScriptSaveFileDialogFilter = "BetterBTD Script (*.btd)|*.btd|JSON File (*.json)|*.json|All Files (*.*)|*.*";
    private static readonly Color CoordinateSelectionActiveColor = Color.FromRgb(87, 242, 135);
    private static readonly Color CoordinateSelectionInactiveColor = Color.FromRgb(255, 176, 64);
    private static readonly Color CoordinateSelectionLabelBackgroundColor = Color.FromArgb(220, 16, 24, 39);

    private readonly LocalizationService _localizationService;
    private readonly AppDialogService _appDialogService;
    private readonly ConfigurationService _configurationService;
    private readonly GameCaptureService _gameCaptureService;
    private readonly MaskWindowService _maskWindowService;
    private readonly CoordinateTransformService _coordinateTransformService;
    private readonly ScriptDocumentService _scriptDocumentService;
    private readonly ScriptEditorInstructionService _scriptEditorInstructionService;
    private readonly ScriptInstructionOptimizationService _scriptInstructionOptimizationService;
    private readonly ScriptEditorSequenceService _scriptEditorSequenceService;
    private readonly ScriptEditorOptionService _scriptEditorOptionService;
    private readonly ScriptTaskFlowService _scriptTaskFlowService;
    private readonly ScriptTaskFlowExecutor _scriptTaskFlowExecutor;
    private readonly ScriptInputSimulationService _scriptInputSimulationService;
    private readonly ManagedScriptLibraryService _managedScriptLibraryService;
    private readonly List<ScriptInstructionInstance> _clipboardSequenceInstructions = [];
    private readonly Stack<List<ScriptInstructionInstance>> _undoHistory = [];
    private readonly Stack<List<ScriptInstructionInstance>> _redoHistory = [];
    private readonly DispatcherTimer _coordinateSelectionTimer;
    private readonly string _emptyWorkspaceSnapshot;

    private string _scriptText = string.Empty;
    private string _scriptVersion = ScriptDocumentFormat.DefaultScriptVersion;
    private string _scriptDescription = string.Empty;
    private string _currentScriptFilePath = string.Empty;
    private GameMapType _selectedMap = GameMapType.MonkeyMeadow;
    private LanguageOption? _selectedDifficultyOption;
    private LanguageOption? _selectedModeOption;
    private LanguageOption? _selectedHeroOption;
    private LanguageOption? _selectedTagOption;
    private string _pendingTagInput = string.Empty;
    private ScriptInstructionTemplate? _selectedLibraryInstruction;
    private ScriptInstructionInstance? _selectedSequenceInstruction;
    private bool _isRestoringHistory;
    private bool _suppressHistoryTracking;
    private bool _isUpdatingSequenceInternals;
    private bool _pendingMonkeyObjectOptionsRebuild;
    private CoordinateSelectionTarget _activeCoordinateSelectionTarget;
    private ScriptInstructionInstance? _activeCoordinateSelectionInstruction;
    private Guid? _coordinateSelectionAnchorId;
    private bool _wasRightMouseButtonDown;
    private string _persistedWorkspaceSnapshot = string.Empty;
    private string _scriptExecutionStatus = string.Empty;
    private bool _isScriptExecutionRunning;
    private List<ScriptInstructionInstance> _sequenceSnapshot = [];
    private CancellationTokenSource? _scriptExecutionCancellationTokenSource;
    private ScriptExecutionWindow? _scriptExecutionWindow;
    private string _managedScriptId = string.Empty;
    private string _managedScriptDisplayName = string.Empty;
    private string _scriptId = Guid.NewGuid().ToString("N");

    public ScriptEditorPageViewModel(
        LocalizationService localizationService,
        ManagedScriptLibraryService? managedScriptLibraryService = null)
    {
        _localizationService = localizationService;
        _appDialogService = AppDialogService.Instance;
        _configurationService = ConfigurationService.Instance;
        _gameCaptureService = GameCaptureService.Instance;
        _maskWindowService = MaskWindowService.Instance;
        _coordinateTransformService = CoordinateTransformService.Instance;
        _scriptDocumentService = ScriptDocumentService.Instance;
        _scriptEditorInstructionService = ScriptEditorInstructionService.Instance;
        _scriptInstructionOptimizationService = ScriptInstructionOptimizationService.Instance;
        _scriptEditorSequenceService = ScriptEditorSequenceService.Instance;
        _scriptEditorOptionService = ScriptEditorOptionService.Instance;
        _scriptTaskFlowService = ScriptTaskFlowService.Instance;
        _scriptTaskFlowExecutor = ScriptTaskFlowExecutor.Instance;
        _scriptInputSimulationService = ScriptInputSimulationService.Instance;
        _managedScriptLibraryService = managedScriptLibraryService ?? ManagedScriptLibraryService.Instance;
        _coordinateSelectionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _coordinateSelectionTimer.Tick += OnCoordinateSelectionTimerTick;
        _localizationService.LanguageChanged += (_, _) =>
        {
            BuildMetadataOptions();
            BuildScriptParameterOptions();
            UpdateInstructionLocalization();
            RebuildMonkeyObjectOptions();
            RaiseLocalizedProperties();
        };

        InstructionSequence.CollectionChanged += OnInstructionSequenceChanged;

        AddInstructionToSequenceCommand = new RelayCommand<ScriptInstructionTemplate?>(AddInstructionToSequence);
        DeleteSelectedSequenceInstructionsCommand = new RelayCommand<IList?>(DeleteSelectedSequenceInstructions, CanDeleteSelectedSequenceInstructions);
        CutSelectedSequenceInstructionsCommand = new RelayCommand<IList?>(CutSelectedSequenceInstructions, CanCutSelectedSequenceInstructions);
        CopySelectedSequenceInstructionsCommand = new RelayCommand<IList?>(CopySelectedSequenceInstructions, CanCopySelectedSequenceInstructions);
        PasteSequenceInstructionsCommand = new RelayCommand<IList?>(PasteSequenceInstructions, CanPasteSequenceInstructions);
        UndoSequenceCommand = new RelayCommand(UndoSequence, CanUndoSequence);
        RedoSequenceCommand = new RelayCommand(RedoSequence, CanRedoSequence);
        OpenScriptFileCommand = new RelayCommand(OpenScriptFile);
        SaveScriptFileCommand = new RelayCommand(SaveScriptFile);
        SaveScriptFileAsCommand = new RelayCommand(SaveScriptFileAs);
        CreateNewScriptFileCommand = new RelayCommand(CreateNewScriptFile);
        OpenTagEditorCommand = new RelayCommand(OpenTagEditor);
        AddScriptTagCommand = new RelayCommand(AddScriptTag);
        RemoveScriptTagCommand = new RelayCommand<string?>(RemoveScriptTag);
        StartCoordinateSelectionCommand = new RelayCommand<string?>(StartCoordinateSelection);
        CancelCoordinateSelectionCommand = new RelayCommand<string?>(_ => CancelCoordinateSelection());
        RunScriptCommand = new AsyncRelayCommand(RunScriptAsync);
        SelectedTagOptions.CollectionChanged += OnSelectedTagOptionsChanged;

        BuildMetadataOptions();
        BuildInstructionLibrary();
        BuildScriptParameterOptions();
        UpdateInstructionLocalization();
        RebuildMonkeyObjectOptions();
        _sequenceSnapshot = CaptureSequenceSnapshot();
        _emptyWorkspaceSnapshot = CaptureWorkspaceSnapshot();
        MarkWorkspaceAsPersisted();
        RefreshHistoryCommandState();
    }

    public int ScriptCount { get; } = 34;
    public int SharedCount { get; } = 12;

    public IRelayCommand<ScriptInstructionTemplate?> AddInstructionToSequenceCommand { get; }
    public IRelayCommand<IList?> DeleteSelectedSequenceInstructionsCommand { get; }
    public IRelayCommand<IList?> CutSelectedSequenceInstructionsCommand { get; }
    public IRelayCommand<IList?> CopySelectedSequenceInstructionsCommand { get; }
    public IRelayCommand<IList?> PasteSequenceInstructionsCommand { get; }
    public IRelayCommand UndoSequenceCommand { get; }
    public IRelayCommand RedoSequenceCommand { get; }
    public IRelayCommand OpenScriptFileCommand { get; }
    public IRelayCommand SaveScriptFileCommand { get; }
    public IRelayCommand SaveScriptFileAsCommand { get; }
    public IRelayCommand CreateNewScriptFileCommand { get; }
    public IRelayCommand OpenTagEditorCommand { get; }
    public IRelayCommand AddScriptTagCommand { get; }
    public IRelayCommand<string?> RemoveScriptTagCommand { get; }
    public IRelayCommand<string?> StartCoordinateSelectionCommand { get; }
    public IRelayCommand<string?> CancelCoordinateSelectionCommand { get; }
    public IAsyncRelayCommand RunScriptCommand { get; }

    public ObservableCollection<LanguageOption> DifficultyOptions { get; } = [];
    public ObservableCollection<LanguageOption> ModeOptions { get; } = [];
    public ObservableCollection<LanguageOption> HeroOptions { get; } = [];
    public ObservableCollection<LanguageOption> TagOptions { get; } = [];
    public ObservableCollection<LanguageOption> SelectedTagOptions { get; } = [];
    public ObservableCollection<LanguageOption> MonkeyObjectOptions { get; } = [];
    public ObservableCollection<LanguageOption> UpgradePathOptions { get; } = [];
    public ObservableCollection<LanguageOption> SwitchDirectionOptions { get; } = [];
    public ObservableCollection<LanguageOption> MonkeyAbilityOptions { get; } = [];
    public ObservableCollection<LanguageOption> InventoryOptions { get; } = [];
    public ObservableCollection<LanguageOption> ActivatedAbilityOptions { get; } = [];
    public ObservableCollection<LanguageOption> NextRoundActionOptions { get; } = [];
    public ObservableCollection<LanguageOption> WaitModeOptions { get; } = [];

    public ObservableCollection<ScriptInstructionTemplate> InstructionLibrary { get; } = [];
    public ObservableCollection<ScriptInstructionInstance> InstructionSequence { get; } = [];

    public IReadOnlyList<ICascadingItem> MapItems => GameElementCascadingItems.MapItems;
    public IReadOnlyList<ICascadingItem> MonkeyTowerItems => GameElementCascadingItems.MonkeyTowerItems;

    public string WorkspaceTitle => _localizationService.T("Editor.Workspace");
    public string WorkspaceDescription => _localizationService.T("Editor.Description");
    public string PreviewHint => _localizationService.T("Editor.Preview");

    public string ScriptText
    {
        get => _scriptText;
        set => SetProperty(ref _scriptText, value);
    }

    public string ScriptVersion
    {
        get => _scriptVersion;
        set => SetProperty(ref _scriptVersion, NormalizeScriptVersion(value));
    }

    public string ScriptDescription
    {
        get => _scriptDescription;
        set => SetProperty(ref _scriptDescription, value?.Trim() ?? string.Empty);
    }

    public string CurrentScriptFilePath
    {
        get => _currentScriptFilePath;
        private set
        {
            if (!SetProperty(ref _currentScriptFilePath, value?.Trim() ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(CurrentFileText));
        }
    }

    public GameMapType SelectedMap
    {
        get => _selectedMap;
        set => SetProperty(ref _selectedMap, value);
    }

    public LanguageOption? SelectedDifficultyOption
    {
        get => _selectedDifficultyOption;
        set => SetProperty(ref _selectedDifficultyOption, value);
    }

    public LanguageOption? SelectedModeOption
    {
        get => _selectedModeOption;
        set => SetProperty(ref _selectedModeOption, value);
    }

    public LanguageOption? SelectedHeroOption
    {
        get => _selectedHeroOption;
        set => SetProperty(ref _selectedHeroOption, value);
    }

    public LanguageOption? SelectedTagOption
    {
        get => _selectedTagOption;
        set
        {
            if (!SetProperty(ref _selectedTagOption, value) || value is null)
            {
                return;
            }

            PendingTagInput = value.DisplayName;
        }
    }

    public string PendingTagInput
    {
        get => _pendingTagInput;
        set => SetProperty(ref _pendingTagInput, value?.Trim() ?? string.Empty);
    }

    public ScriptInstructionTemplate? SelectedLibraryInstruction
    {
        get => _selectedLibraryInstruction;
        set => SetProperty(ref _selectedLibraryInstruction, value);
    }

    public ScriptInstructionInstance? SelectedSequenceInstruction
    {
        get => _selectedSequenceInstruction;
        set
        {
            if (!SetProperty(ref _selectedSequenceInstruction, value))
            {
                return;
            }

            if (!ReferenceEquals(_activeCoordinateSelectionInstruction, value))
            {
                CancelCoordinateSelection();
            }

            OnPropertyChanged(nameof(HasSelectedSequenceInstruction));
            OnPropertyChanged(nameof(ShowPropertiesEmptyState));
            OnPropertyChanged(nameof(ShowNonExecutableInstructionHint));
            OnPropertyChanged(nameof(ShowAdvancedProperties));
            OnPropertyChanged(nameof(ShowMouseClickAdvancedProperties));
            OnPropertyChanged(nameof(ShowPlaceMonkeyAdvancedProperties));
            OnPropertyChanged(nameof(ShowNextRoundAdvancedProperties));
            OnPropertyChanged(nameof(ShowUpgradeMonkeyAdvancedProperties));
            OnPropertyChanged(nameof(ShowMonkeyPanelAdvancedProperties));
            OnPropertyChanged(nameof(ShowSellMonkeyAdvancedProperties));
        }
    }

    public bool HasSelectedSequenceInstruction => SelectedSequenceInstruction is not null;

    public bool ShowPropertiesEmptyState => SelectedSequenceInstruction is null;

    public bool ShowNonExecutableInstructionHint => SelectedSequenceInstruction is { IsExecutable: false };

    public bool ShowAdvancedProperties => SelectedSequenceInstruction?.Type is not ScriptCommandType.ModifyMonkeyCoordinate and not ScriptCommandType.Comment;

    public bool ShowMouseClickAdvancedProperties => SelectedSequenceInstruction?.Type == ScriptCommandType.MouseClick;

    public bool ShowPlaceMonkeyAdvancedProperties => SelectedSequenceInstruction?.Type == ScriptCommandType.PlaceMonkey;

    public bool ShowNextRoundAdvancedProperties => SelectedSequenceInstruction?.Type == ScriptCommandType.NextRound;

    public bool ShowUpgradeMonkeyAdvancedProperties => SelectedSequenceInstruction?.Type == ScriptCommandType.UpgradeMonkey;

    public bool ShowMonkeyPanelAdvancedProperties => SelectedSequenceInstruction?.Type is ScriptCommandType.SwitchMonkeyTarget or ScriptCommandType.SetMonkeyAbility or ScriptCommandType.SellMonkey;

    public bool ShowSellMonkeyAdvancedProperties => SelectedSequenceInstruction?.Type == ScriptCommandType.SellMonkey;

    public bool ShowSequenceEmptyState => InstructionSequence.Count == 0;

    public string StatsTitle => _localizationService.T("Editor.Stats");
    public string TotalScriptsText => string.Format(_localizationService.T("Editor.TotalScripts"), ScriptCount);
    public string SharedScriptsText => string.Format(_localizationService.T("Editor.SharedScripts"), SharedCount);
    public string StatusTitle => _localizationService.T("Editor.State");
    public string DraftState => _localizationService.T("Editor.DraftState");

    public string FileOpenText => _localizationService.T("Editor.File.Open");
    public string FileSaveText => _localizationService.T("Editor.File.Save");
    public string FileSaveAsText => _localizationService.T("Editor.File.SaveAs");
    public string FileNewText => _localizationService.T("Editor.File.New");
    public string FileCloseText => _localizationService.T("Editor.File.Close");
    public string CurrentFileText => BuildCurrentFileText();

    public string MetadataMapText => _localizationService.T("Editor.Metadata.Map");
    public string MetadataDifficultyText => _localizationService.T("Editor.Metadata.Difficulty");
    public string MetadataModeText => _localizationService.T("Editor.Metadata.Mode");
    public string MetadataHeroText => _localizationService.T("Editor.Metadata.Hero");
    public string MetadataTagText => _localizationService.T("Editor.Metadata.Tag");
    public string MetadataTagAddText => _localizationService.T("Editor.Metadata.Tag.Add");
    public string MetadataTagHintText => _localizationService.T("Editor.Metadata.Tag.Hint");
    public string MetadataTagEmptyText => _localizationService.T("Editor.Metadata.Tag.Empty");
    public string MetadataMapPlaceholderText => _localizationService.T("Editor.Metadata.Map.Placeholder");
    public string TagEditorWindowTitle => _localizationService.T("Editor.Metadata.Tag.WindowTitle");
    public string SelectedTagsSummaryText => BuildSelectedTagsSummaryText();

    public string DebugOpenRuntimeText => _localizationService.LanguageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase)
        ? "Open Runtime"
        : "打开运行界面";
    public string DebugRunText => _localizationService.T("Editor.Debug.Run");
    public string DebugStepText => _localizationService.T("Editor.Debug.Step");
    public string DebugStopText => _localizationService.T("Editor.Debug.Stop");
    public string DebugValidateText => _localizationService.T("Editor.Debug.Validate");
    public string ScriptExecutionStatus => _scriptExecutionStatus;
    public bool IsScriptExecutionRunning
    {
        get => _isScriptExecutionRunning;
        private set
        {
            if (!SetProperty(ref _isScriptExecutionRunning, value))
            {
                return;
            }

            RunScriptCommand.NotifyCanExecuteChanged();
        }
    }

    public string LibraryTitle => _localizationService.T("Editor.Panel.Library.Title");
    public string SequenceTitle => _localizationService.T("Editor.Panel.Sequence.Title");
    public string PropertiesTitle => _localizationService.T("Editor.Panel.Properties.Title");
    public string SequenceEmptyText => _localizationService.T("Editor.Panel.Sequence.Empty");
    public string PropertiesEmptyText => _localizationService.T("Editor.Panel.Properties.Empty");
    public string PropertyMonkeyTowerText => _localizationService.T("Editor.Property.MonkeyTower");
    public string PropertyCoordinateText => _localizationService.T("Editor.Property.Coordinate");
    public string CoordinateSelectButtonText => _localizationService.T("Editor.Property.SelectCoordinateButton");
    public string CoordinateCancelButtonText => _localizationService.T("Editor.Property.CancelCoordinateButton");
    public string PropertyTargetMonkeyText => _localizationService.T("Editor.Property.TargetMonkey");
    public string PropertyUpgradePathText => _localizationService.T("Editor.Property.UpgradePath");
    public string PropertyUpgradeCountText => _localizationService.T("Editor.Property.UpgradeCount");
    public string PropertySwitchDirectionText => _localizationService.T("Editor.Property.SwitchDirection");
    public string PropertySwitchCountText => _localizationService.T("Editor.Property.SwitchCount");
    public string PropertyAbilityText => _localizationService.T("Editor.Property.Ability");
    public string PropertyNeedAbilityCoordinateText => _localizationService.T("Editor.Property.NeedAbilityCoordinate");
    public string PropertyInventoryText => _localizationService.T("Editor.Property.Inventory");
    public string PropertyActivatedAbilityText => _localizationService.T("Editor.Property.ActivatedAbility");
    public string PropertyNeedCoordinateText => _localizationService.T("Editor.Property.NeedCoordinate");
    public string PropertyNextRoundActionText => _localizationService.T("Editor.Property.NextRoundAction");
    public string PropertyNextRoundSendCountText => _localizationService.T("Editor.Property.NextRound.SendCount");
    public string PropertyNextRoundOperationIntervalMillisecondsText => _localizationService.T("Editor.Property.NextRoundOperationIntervalMilliseconds");
    public string PropertyWaitModeText => _localizationService.T("Editor.Property.WaitMode");
    public string PropertyWaitTimeMillisecondsText => _localizationService.T("Editor.Property.WaitTimeMilliseconds");
    public string PropertyWaitGoldAmountText => _localizationService.T("Editor.Property.WaitGoldAmount");
    public string PropertyWaitRoundCountText => _localizationService.T("Editor.Property.WaitRoundCount");
    public string PropertyWaitColorHexText => _localizationService.T("Editor.Property.WaitColorHex");
    public string PropertyWaitColorToleranceText => _localizationService.T("Editor.Property.WaitColorTolerance");
    public string PropertyClickCountText => _localizationService.T("Editor.Property.ClickCount");
    public string PropertyCommentContentText => _localizationService.T("Editor.Property.CommentContent");
    public string PropertyAdvancedText => _localizationService.T("Editor.Property.Advanced");
    public string PropertyPlacementDetectionText => _localizationService.T("Editor.Property.PlacementDetection");
    public string PropertyPlacementFailureAdjustmentText => _localizationService.T("Editor.Property.PlacementFailureAdjustment");
    public string PropertyPlacementAttemptIntervalMillisecondsText => _localizationService.T("Editor.Property.PlacementAttemptIntervalMilliseconds");
    public string PropertyPlacementAdjustmentAttemptIntervalMillisecondsText => _localizationService.T("Editor.Property.PlacementAdjustmentAttemptIntervalMilliseconds");
    public string PropertyUpgradeDetectionText => _localizationService.T("Editor.Property.UpgradeDetection");
    public string PropertyUpgradeOperationIntervalMillisecondsText => _localizationService.T("Editor.Property.UpgradeOperationIntervalMilliseconds");
    public string PropertyMonkeyPanelDetectionText => _localizationService.T("Editor.Property.MonkeyPanelDetection");
    public string PropertyMonkeyPanelOperationIntervalMillisecondsText => _localizationService.T("Editor.Property.MonkeyPanelOperationIntervalMilliseconds");
    public string PropertySellDetectionText => _localizationService.T("Editor.Property.SellDetection");
    public string PropertyClickIntervalMillisecondsText => _localizationService.T("Editor.Property.ClickIntervalMilliseconds");
    public string PropertyIntervalToNextInstructionText => _localizationService.T("Editor.Property.IntervalToNextInstruction");
    public string NonExecutableInstructionHintText => _localizationService.T("Editor.Property.NonExecutableHint");
    public string PropertyNotesText => _localizationService.T("Editor.Property.Notes");
    public string DeleteSelectedInstructionText => _localizationService.T("Editor.Command.DeleteSelected");
    public string CutSelectedInstructionText => _localizationService.T("Editor.Command.CutSelected");
    public string CopySelectedInstructionText => _localizationService.T("Editor.Command.CopySelected");
    public string PasteInstructionText => _localizationService.T("Editor.Command.Paste");
    public bool IsPositionCoordinateSelectionActive => IsCoordinateSelectionActive(CoordinateSelectionTarget.Position);
    public bool IsAbilityCoordinateSelectionActive => IsCoordinateSelectionActive(CoordinateSelectionTarget.Ability);
    public bool IsWaitColorCoordinateSelectionActive => IsCoordinateSelectionActive(CoordinateSelectionTarget.WaitColor);

    public ScriptDocument ExportScriptDocument()
    {
        return new ScriptDocument
        {
            Metadata = new ScriptMetadataDocument
            {
                ScriptId = _scriptId,
                ScriptVersion = NormalizeScriptVersion(ScriptVersion),
                Description = ScriptDescription,
                Map = SelectedMap.ToString(),
                Difficulty = SelectedDifficultyOption?.Code ?? StageDifficulty.Medium.ToString(),
                Mode = SelectedModeOption?.Code ?? StageMode.Standard.ToString(),
                Hero = SelectedHeroOption?.Code ?? HeroType.Quincy.ToString(),
                Tags = SelectedTagOptions.Select(x => x.Code).ToList()
            },
            MonkeyObjects = _scriptEditorInstructionService.BuildMonkeyObjectDocuments(InstructionSequence),
            Instructions = _scriptEditorInstructionService.BuildInstructionDocuments(InstructionSequence)
        };
    }

    public void ImportScriptDocument(ScriptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ApplyScriptMetadata(document.Metadata);

        var monkeyObjectsByBindingId = document.MonkeyObjects
            .Where(x => !string.IsNullOrWhiteSpace(x.BindingId))
            .GroupBy(x => x.BindingId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var templatesByType = InstructionLibrary.ToDictionary(x => x.Type);
        var instructions = document.Instructions
            .Select(x => _scriptEditorInstructionService.CreateInstructionInstanceFromDocument(
                x,
                monkeyObjectsByBindingId,
                templatesByType,
                MonkeyObjectOptions.FirstOrDefault()?.Code ?? string.Empty,
                InventoryOptions.FirstOrDefault()?.Code ?? string.Empty,
                ActivatedAbilityOptions.FirstOrDefault()?.Code ?? string.Empty))
            .ToList();

        ReplaceSequenceWithInstructions(instructions);
        MarkWorkspaceAsPersisted();
    }

    public void SaveScriptDocument(string filePath)
    {
        var previousFilePath = CurrentScriptFilePath;
        var previousManagedScriptId = _managedScriptId;
        var previousManagedScriptDisplayName = _managedScriptDisplayName;
        var isSaveAsToDifferentPath =
            !string.IsNullOrWhiteSpace(previousFilePath) &&
            !AreSameFilePath(filePath, previousFilePath);

        if (isSaveAsToDifferentPath)
        {
            AssignNewScriptId();
        }

        var optimizedDocument = _scriptInstructionOptimizationService.OptimizeDocument(ExportScriptDocument());
        _scriptDocumentService.Save(filePath, optimizedDocument);
        ImportScriptDocument(optimizedDocument);
        CurrentScriptFilePath = filePath;
        var managedScriptEntry = SyncManagedScriptAfterSave(
            filePath,
            previousFilePath,
            previousManagedScriptId,
            previousManagedScriptDisplayName);
        SetManagedScriptAssociation(managedScriptEntry);
    }

    public void AssignNewScriptId()
    {
        _scriptId = Guid.NewGuid().ToString("N");
    }

    public void LoadScriptDocument(string filePath)
    {
        var loadResult = _scriptDocumentService.LoadCompatible(filePath);
        ImportScriptDocument(loadResult.Document);
        CurrentScriptFilePath = filePath;
        RestoreManagedScriptAssociation(filePath);

        if (loadResult.SourceKind == ScriptDocumentSourceKind.LegacyBtd6)
        {
            ShowLegacyScriptImportedMessage(loadResult.Warnings);
        }
    }

    public void CreateNewScriptDocument()
    {
        ApplyScriptMetadata(new ScriptMetadataDocument());
        ReplaceSequenceWithInstructions([]);
        _clipboardSequenceInstructions.Clear();
        CurrentScriptFilePath = string.Empty;
        ClearManagedScriptAssociation();
        MarkWorkspaceAsPersisted();
    }

    public static bool TryRunScriptFromExternal(string filePath, LocalizationService localizationService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(localizationService);

        var viewModel = new ScriptEditorPageViewModel(localizationService);
        return viewModel.TryOpenScriptFromExternal(filePath, openRuntimeWindow: true);
    }

    private string BuildCurrentFileText()
    {
        var currentText = _localizationService.T("Editor.File.Current");
        if (string.IsNullOrWhiteSpace(CurrentScriptFilePath))
        {
            return currentText;
        }

        var displayName = string.IsNullOrWhiteSpace(CurrentScriptFilePath)
            ? BuildUntitledFileName()
            : Path.GetFileName(CurrentScriptFilePath);

        var separatorIndex = currentText.IndexOf('：');
        if (separatorIndex >= 0)
        {
            return $"{currentText[..(separatorIndex + 1)]} {displayName}";
        }

        separatorIndex = currentText.IndexOf(':');
        if (separatorIndex >= 0)
        {
            return $"{currentText[..(separatorIndex + 1)]} {displayName}";
        }

        return $"{currentText} {displayName}";
    }

    private string BuildSelectedTagsSummaryText()
    {
        if (SelectedTagOptions.Count == 0)
        {
            return MetadataTagEmptyText;
        }

        return string.Join(" | ", SelectedTagOptions.Select(x => x.DisplayName));
    }

    private void OpenTagEditor()
    {
        var window = new ScriptTagEditorWindow(this);
        var owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(x => x.IsActive)
            ?? Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        _ = window.ShowDialog();
    }

    private void AddScriptTag()
    {
        var storedTag = ScriptTagCatalog.ResolveStoredValue(PendingTagInput);
        if (string.IsNullOrWhiteSpace(storedTag) ||
            SelectedTagOptions.Any(x => string.Equals(x.Code, storedTag, StringComparison.OrdinalIgnoreCase)))
        {
            ClearPendingTagInput();
            return;
        }

        SelectedTagOptions.Add(CreateTagOption(storedTag));
        ClearPendingTagInput();
    }

    private void RemoveScriptTag(string? storedTag)
    {
        if (string.IsNullOrWhiteSpace(storedTag))
        {
            return;
        }

        var option = SelectedTagOptions.FirstOrDefault(x => string.Equals(x.Code, storedTag, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            SelectedTagOptions.Remove(option);
        }
    }

    public bool TryOpenScriptFromExternal(string filePath, bool openRuntimeWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!ConfirmUnsavedChanges("Editor.File.UnsavedChanges.OpenPrompt"))
        {
            return false;
        }

        try
        {
            LoadScriptDocument(filePath);

            if (openRuntimeWindow)
            {
                OpenScriptExecutionWindow();
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowMessageDialog(
                _localizationService.T("Editor.File.OpenError.Title"),
                string.Format(_localizationService.T("Editor.File.OpenError.Message"), ex.Message));
            return false;
        }
    }

    private void ClearPendingTagInput()
    {
        SelectedTagOption = null;
        PendingTagInput = string.Empty;
    }

    private void SetSelectedTags(IEnumerable<string>? storedTags)
    {
        ReplaceCollection(
            SelectedTagOptions,
            ScriptTagCatalog.NormalizeStoredTags(storedTags)
                .Select(CreateTagOption));
    }

    private void OnSelectedTagOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedTagsSummaryText));
    }

    private static LanguageOption CreateTagOption(string storedTag)
    {
        return new LanguageOption
        {
            Code = storedTag,
            DisplayName = ScriptTagCatalog.GetDisplayName(storedTag)
        };
    }

    private static string BuildUntitledFileName()
    {
        return $"Untitled Script{ScriptFileExtension}";
    }

    private void MarkWorkspaceAsPersisted()
    {
        _persistedWorkspaceSnapshot = CaptureWorkspaceSnapshot();
    }

    private string CaptureWorkspaceSnapshot()
    {
        return JsonSerializer.Serialize(ExportScriptDocument());
    }

    private bool HasUnsavedChanges()
    {
        return !string.Equals(CaptureWorkspaceSnapshot(), _persistedWorkspaceSnapshot, StringComparison.Ordinal);
    }

    private bool IsWorkspaceEmpty()
    {
        return string.Equals(CaptureWorkspaceSnapshot(), _emptyWorkspaceSnapshot, StringComparison.Ordinal);
    }

    private bool ShouldPromptForUnsavedChanges()
    {
        return !IsWorkspaceEmpty() && HasUnsavedChanges();
    }

    private bool ConfirmUnsavedChanges(string promptKey)
    {
        if (!ShouldPromptForUnsavedChanges())
        {
            return true;
        }

        var result = _appDialogService.Show(new AppDialogRequest
        {
            Title = _localizationService.T("Editor.File.UnsavedChanges.Title"),
            Message = _localizationService.T(promptKey),
            PrimaryButtonText = FileSaveText,
            SecondaryButtonText = _localizationService.T("Editor.Dialog.DontSave"),
            CloseButtonText = _localizationService.T("Editor.Dialog.Cancel")
        });

        return result switch
        {
            AppDialogResult.Primary => TrySaveScriptFile(),
            AppDialogResult.Secondary => true,
            _ => false
        };
    }

    private void OpenScriptFile()
    {
        if (!ConfirmUnsavedChanges("Editor.File.UnsavedChanges.OpenPrompt"))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = ScriptOpenFileDialogFilter,
            DefaultExt = ScriptFileExtension,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            LoadScriptDocument(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowMessageDialog(
                _localizationService.T("Editor.File.OpenError.Title"),
                string.Format(_localizationService.T("Editor.File.OpenError.Message"), ex.Message));
        }
    }

    private void SaveScriptFile()
    {
        _ = TrySaveScriptFile();
    }

    private void SaveScriptFileAs()
    {
        _ = TrySaveScriptFileAs();
    }

    private bool TrySaveScriptFile()
    {
        if (string.IsNullOrWhiteSpace(CurrentScriptFilePath))
        {
            return TrySaveScriptFileAs();
        }

        if (string.Equals(Path.GetExtension(CurrentScriptFilePath), LegacyScriptFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return TrySaveScriptFileAs();
        }

        try
        {
            SaveScriptDocument(CurrentScriptFilePath);
            return true;
        }
        catch (Exception ex)
        {
            ShowSaveError(ex);
            return false;
        }
    }

    private bool TrySaveScriptFileAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = ScriptSaveFileDialogFilter,
            DefaultExt = ScriptFileExtension,
            AddExtension = true,
            FileName = BuildDefaultSaveFileName()
        };

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        try
        {
            SaveScriptDocument(dialog.FileName);
            return true;
        }
        catch (Exception ex)
        {
            ShowSaveError(ex);
            return false;
        }
    }

    private void CreateNewScriptFile()
    {
        if (!ConfirmUnsavedChanges("Editor.File.UnsavedChanges.NewPrompt"))
        {
            return;
        }

        CreateNewScriptDocument();
    }

    private async Task RunScriptAsync()
    {
        OpenScriptExecutionWindow();
        await Task.CompletedTask;
#if false
        if (IsScriptExecutionRunning)
        {
            return;
        }

        if (_scriptTaskFlowExecutor.IsRunning)
        {
            ShowMessageDialog(
                _localizationService.T("Editor.Debug.Run"),
                "已有脚本任务正在运行，当前编辑器不能启动新的执行。");
            return;
        }

        if (!InstructionSequence.Any())
        {
            ShowMessageDialog(
                _localizationService.T("Editor.Debug.Run"),
                "当前脚本没有任何指令。");
            return;
        }

        var executionSequenceSnapshot = CaptureSequenceSnapshot();
        ScriptTaskFlow taskFlow;
        try
        {
            taskFlow = _scriptTaskFlowService.Build(
                ExportScriptDocument(),
                string.IsNullOrWhiteSpace(CurrentScriptFilePath) ? "[Unsaved Script]" : CurrentScriptFilePath);
        }
        catch (Exception ex)
        {
            ShowMessageDialog(
                _localizationService.T("Editor.Debug.Run"),
                $"构建执行任务失败。\n\n{ex.Message}");
            return;
        }

        _scriptExecutionCancellationTokenSource?.Dispose();
        _scriptExecutionCancellationTokenSource = new CancellationTokenSource();
        IsScriptExecutionRunning = true;
        var runtimeWindowViewModel = new ScriptExecutionWindowViewModel(
            _localizationService,
            ResolveExecutionScriptDisplayName(),
            taskFlow.SourceFilePath,
            executionSequenceSnapshot,
            StopScriptExecution);
        var runtimeWindow = new ScriptExecutionWindow(runtimeWindowViewModel);
        if (Application.Current?.MainWindow is Window owner && owner != runtimeWindow)
        {
            runtimeWindow.Owner = owner;
        }

        runtimeWindow.Show();
        runtimeWindowViewModel.MarkStarting();

        EventHandler<ScriptExecutionProgressSnapshot> progressHandler = (_, snapshot) =>
            runtimeWindowViewModel.PostProgressSnapshot(snapshot);
        EventHandler<ScriptExecutionRuntimeLogEntry> runtimeLogHandler = (_, entry) =>
            runtimeWindowViewModel.PostRuntimeLogEntry(entry);

        _scriptTaskFlowExecutor.ProgressChanged += progressHandler;
        _scriptTaskFlowExecutor.RuntimeLogEmitted += runtimeLogHandler;

        try
        {
            var result = await _scriptTaskFlowExecutor
                .ExecuteAsync(taskFlow, cancellationToken: _scriptExecutionCancellationTokenSource.Token)
                .ConfigureAwait(true);

            runtimeWindowViewModel.ApplyResult(result);
        }
        catch (Exception ex)
        {
            runtimeWindowViewModel.ApplyUnexpectedException(ex);
        }
        finally
        {
            _scriptTaskFlowExecutor.ProgressChanged -= progressHandler;
            _scriptTaskFlowExecutor.RuntimeLogEmitted -= runtimeLogHandler;
            _scriptExecutionCancellationTokenSource?.Dispose();
            _scriptExecutionCancellationTokenSource = null;
            IsScriptExecutionRunning = false;
        }
#endif
    }

    private void StopScriptExecution()
    {
        _scriptExecutionCancellationTokenSource?.Cancel();
        _scriptInputSimulationService.ReleaseAllKeys();
    }

    private void ShowSaveError(Exception ex)
    {
        ShowMessageDialog(
            _localizationService.T("Editor.File.SaveError.Title"),
            string.Format(_localizationService.T("Editor.File.SaveError.Message"), ex.Message));
    }

    private void ShowLegacyScriptImportedMessage(IReadOnlyList<string> warnings)
    {
        var title = _localizationService.LanguageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase)
            ? "Legacy Script Imported"
            : "已导入旧版脚本";
        var intro = _localizationService.LanguageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase)
            ? "The .btd6 script was converted into the current editor model. Saving will use the new format and prompt Save As."
            : ".btd6 脚本已转换为当前编辑器模型。后续保存会使用新格式，并自动进入另存为。";

        if (warnings.Count == 0)
        {
            ShowMessageDialog(title, intro);
            return;
        }

        var warningLines = string.Join(
            Environment.NewLine,
            warnings.Take(8).Select(x => $"- {x}"));
        var moreSuffix = warnings.Count > 8
            ? _localizationService.LanguageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase)
                ? $"{Environment.NewLine}...and {warnings.Count - 8} more warning(s)."
                : $"{Environment.NewLine}......其余 {warnings.Count - 8} 条警告未展开。"
            : string.Empty;

        ShowMessageDialog(
            title,
            $"{intro}{Environment.NewLine}{Environment.NewLine}{warningLines}{moreSuffix}");
    }

    private void ShowMessageDialog(string title, string message)
    {
        _ = _appDialogService.Show(new AppDialogRequest
        {
            Title = title,
            Message = message,
            PrimaryButtonText = _localizationService.T("Editor.Dialog.Ok")
        });
    }

    private bool CanRunScript()
    {
        return !IsScriptExecutionRunning;
    }

    private void ApplyExecutionResult(ScriptExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Status)
        {
            case BetterBTD.Models.ScriptExecution.ScriptExecutionStatus.Completed:
                SetScriptExecutionStatus(
                    $"执行完成：已完成 {result.ExecutedStepCount} 步，最后一步索引 {result.LastCompletedStepIndex}。");
                break;
            case BetterBTD.Models.ScriptExecution.ScriptExecutionStatus.Cancelled:
                SetScriptExecutionStatus(
                    $"执行已取消：已完成 {result.ExecutedStepCount} 步，最后一步索引 {result.LastCompletedStepIndex}。");
                break;
            case BetterBTD.Models.ScriptExecution.ScriptExecutionStatus.Failed:
                var failure = result.Failure;
                var message = failure is null
                    ? $"执行失败：{result.Exception?.Message ?? "未知错误"}"
                    : $"执行失败：第 {failure.StepIndex} 步 {failure.CommandType}，检查点 {failure.Checkpoint}，尝试 {failure.Attempt}，{failure.Message}";
                SetScriptExecutionStatus(message);
                ShowMessageDialog(_localizationService.T("Editor.Debug.Run"), message);
                break;
        }
    }

    private static string BuildExecutionStatusText(ScriptExecutionProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var stepText = snapshot.CurrentStepIndex >= 0
            ? $"第 {snapshot.CurrentStepIndex} 步"
            : "未进入步骤";
        var commandText = string.IsNullOrWhiteSpace(snapshot.CurrentCommandType)
            ? "无指令"
            : snapshot.CurrentCommandType;
        var checkpointText = string.IsNullOrWhiteSpace(snapshot.CurrentCheckpoint)
            ? "无检查点"
            : snapshot.CurrentCheckpoint;
        var attemptText = snapshot.CurrentAttempt > 0
            ? $"，尝试 {snapshot.CurrentAttempt}"
            : string.Empty;
        var messageText = string.IsNullOrWhiteSpace(snapshot.Message)
            ? string.Empty
            : $"，{snapshot.Message}";

        return $"执行状态：{snapshot.RunState}，{stepText}，{commandText}，{checkpointText}{attemptText}{messageText}";
    }

    private void SetScriptExecutionStatus(string value)
    {
        if (SetProperty(ref _scriptExecutionStatus, value ?? string.Empty))
        {
            OnPropertyChanged(nameof(ScriptExecutionStatus));
        }
    }

    private string ResolveExecutionScriptDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(_managedScriptDisplayName))
        {
            return _managedScriptDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(CurrentScriptFilePath))
        {
            return Path.GetFileNameWithoutExtension(CurrentScriptFilePath);
        }

        return _localizationService.T("Editor.Runtime.UntitledScript");
    }

    private ManagedScriptAssetEntry SyncManagedScriptAfterSave(
        string filePath,
        string previousFilePath,
        string previousManagedScriptId,
        string previousManagedScriptDisplayName)
    {
        var isSaveAsToDifferentPath =
            !string.IsNullOrWhiteSpace(previousFilePath) &&
            !AreSameFilePath(filePath, previousFilePath);
        var shouldReuseManagedScriptId = !isSaveAsToDifferentPath;
        var shouldPreserveManagedDisplayName =
            shouldReuseManagedScriptId &&
            !string.IsNullOrWhiteSpace(previousFilePath) &&
            !string.IsNullOrWhiteSpace(previousManagedScriptId) &&
            !string.IsNullOrWhiteSpace(previousManagedScriptDisplayName) &&
            _managedScriptLibraryService.TryGetManagedScriptByStoredFilePath(previousFilePath, out _);
        var preferredDisplayName = shouldPreserveManagedDisplayName
            ? previousManagedScriptDisplayName
            : Path.GetFileNameWithoutExtension(filePath);

        return _managedScriptLibraryService.UpsertScript(
            filePath,
            shouldReuseManagedScriptId ? previousManagedScriptId : null,
            preferredDisplayName);
    }

    private void RestoreManagedScriptAssociation(string filePath)
    {
        if (_managedScriptLibraryService.TryGetManagedScriptByStoredFilePath(filePath, out var managedScriptEntry))
        {
            SetManagedScriptAssociation(managedScriptEntry);
            return;
        }

        ClearManagedScriptAssociation();
    }

    private void SetManagedScriptAssociation(ManagedScriptAssetEntry managedScriptEntry)
    {
        _managedScriptId = managedScriptEntry.ScriptId;
        _managedScriptDisplayName = managedScriptEntry.DisplayName;
    }

    private void ClearManagedScriptAssociation()
    {
        _managedScriptId = string.Empty;
        _managedScriptDisplayName = string.Empty;
    }

    private static bool AreSameFilePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private void OpenScriptExecutionWindow()
    {
        if (_scriptExecutionWindow is not null)
        {
            if (IsScriptExecutionRunning)
            {
                ActivateExecutionWindow(_scriptExecutionWindow);
                return;
            }

            _scriptExecutionWindow.Close();
        }

        var executionSequenceSnapshot = CaptureSequenceSnapshot();
        var scriptDocumentSnapshot = ExportScriptDocument();
        var sourceFilePath = string.IsNullOrWhiteSpace(CurrentScriptFilePath) ? "[Unsaved Script]" : CurrentScriptFilePath;

        var runtimeWindowViewModel = new ScriptExecutionWindowViewModel(
            _localizationService,
            ResolveExecutionScriptDisplayName(),
            sourceFilePath,
            executionSequenceSnapshot,
            (viewModel, startStepIndex) => StartScriptExecutionAsync(viewModel, scriptDocumentSnapshot, sourceFilePath, startStepIndex),
            StopScriptExecution,
            _configurationService.GetScriptExecutionWindowSettings(),
            _configurationService.SaveScriptExecutionWindowSettings);
        var runtimeWindow = new ScriptExecutionWindow(runtimeWindowViewModel);
        runtimeWindow.Closed += OnScriptExecutionWindowClosed;

        if (Application.Current?.MainWindow is Window owner && owner != runtimeWindow)
        {
            runtimeWindow.Owner = owner;
        }

        _scriptExecutionWindow = runtimeWindow;
        runtimeWindow.Show();
        ActivateExecutionWindow(runtimeWindow);
    }

    private async Task StartScriptExecutionAsync(
        ScriptExecutionWindowViewModel runtimeWindowViewModel,
        ScriptDocument scriptDocumentSnapshot,
        string sourceFilePath,
        int startStepIndex)
    {
        ArgumentNullException.ThrowIfNull(runtimeWindowViewModel);
        ArgumentNullException.ThrowIfNull(scriptDocumentSnapshot);

        if (IsScriptExecutionRunning)
        {
            return;
        }

        if (_scriptTaskFlowExecutor.IsRunning)
        {
            ShowMessageDialog(
                _localizationService.T("Editor.Debug.Run"),
                _localizationService.LanguageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase)
                    ? "Another script task is already running. A new execution cannot be started right now."
                    : "已有脚本任务正在运行，当前不能启动新的执行。");
            return;
        }

        ScriptTaskFlow taskFlow;
        try
        {
            EnsureCaptureServiceRunning();
            taskFlow = _scriptTaskFlowService.Build(scriptDocumentSnapshot, sourceFilePath);
            _scriptInputSimulationService.PrepareTargetWindowForInput();
        }
        catch (Exception ex)
        {
            ShowMessageDialog(
                _localizationService.T("Editor.Debug.Run"),
                _localizationService.LanguageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase)
                    ? $"Failed to build the execution task.\n\n{ex.Message}"
                    : $"构建执行任务失败。\n\n{ex.Message}");
            return;
        }

        _scriptExecutionCancellationTokenSource?.Dispose();
        _scriptExecutionCancellationTokenSource = new CancellationTokenSource();
        IsScriptExecutionRunning = true;
        runtimeWindowViewModel.MarkStarting(startStepIndex);

        EventHandler<ScriptExecutionProgressSnapshot> progressHandler = (_, snapshot) =>
            runtimeWindowViewModel.PostProgressSnapshot(snapshot);
        EventHandler<ScriptExecutionRuntimeLogEntry> runtimeLogHandler = (_, entry) =>
            runtimeWindowViewModel.PostRuntimeLogEntry(entry);

        _scriptTaskFlowExecutor.ProgressChanged += progressHandler;
        _scriptTaskFlowExecutor.RuntimeLogEmitted += runtimeLogHandler;

        try
        {
            var executionOptions = new ScriptExecutionOptions
            {
                StartStepIndex = startStepIndex,
                IntervalStrategy = runtimeWindowViewModel.SelectedIntervalStrategyValue,
                CommonOperationIntervalMs = runtimeWindowViewModel.CommonOperationIntervalMs
            };
            var cancellationToken = _scriptExecutionCancellationTokenSource.Token;
            var result = await Task
                .Run(() => _scriptTaskFlowExecutor.ExecuteAsync(taskFlow, executionOptions, cancellationToken))
                .ConfigureAwait(true);

            runtimeWindowViewModel.ApplyResult(result);
        }
        catch (Exception ex)
        {
            runtimeWindowViewModel.ApplyUnexpectedException(ex);
        }
        finally
        {
            _scriptTaskFlowExecutor.ProgressChanged -= progressHandler;
            _scriptTaskFlowExecutor.RuntimeLogEmitted -= runtimeLogHandler;
            _scriptExecutionCancellationTokenSource?.Dispose();
            _scriptExecutionCancellationTokenSource = null;
            IsScriptExecutionRunning = false;
        }
    }

    private void EnsureCaptureServiceRunning()
    {
        if (_gameCaptureService.IsRunning)
        {
            if (!_maskWindowService.IsRunning)
            {
                _maskWindowService.Start();
                _maskWindowService.RefreshNow();
            }

            return;
        }

        var captureOptions = BuildCaptureOptions();
        _gameCaptureService.Configure(captureOptions);

        if (!_gameCaptureService.TryStart(captureOptions, out _))
        {
            throw new InvalidOperationException(BuildCaptureStartupFailureMessage());
        }

        _maskWindowService.Start();
        _maskWindowService.RefreshNow();
    }

    private GameCaptureOptions BuildCaptureOptions()
    {
        var configuration = _configurationService.Current;
        var configuredCaptureMode = configuration.CaptureModeName;
        if (string.IsNullOrWhiteSpace(configuredCaptureMode) ||
            !_gameCaptureService.AvailableCaptureModes.Any(mode =>
                string.Equals(mode, configuredCaptureMode, StringComparison.OrdinalIgnoreCase)))
        {
            configuredCaptureMode = _gameCaptureService.AvailableCaptureModes.FirstOrDefault()
                ?? nameof(Fischless.GameCapture.CaptureModes.WindowsGraphicsCapture);
        }

        return new GameCaptureOptions
        {
            CaptureModeName = configuredCaptureMode,
            CaptureIntervalMs = Math.Clamp(configuration.CaptureIntervalMs <= 0 ? 50 : configuration.CaptureIntervalMs, 10, 2000),
            AutoFixWin11BitBlt = configuration.AutoFixWin11BitBlt
        };
    }

    private string BuildCaptureStartupFailureMessage()
    {
        var windowTitle = _gameCaptureService.TargetWindowTitle;
        return string.IsNullOrWhiteSpace(windowTitle)
            ? "未找到目标窗口。请先启动游戏，或在开始页面手动选择捕获窗口。"
            : $"未找到目标窗口“{windowTitle}”。请先启动游戏，或在开始页面手动选择捕获窗口。";
    }

    private void OnScriptExecutionWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not ScriptExecutionWindow window)
        {
            return;
        }

        window.Closed -= OnScriptExecutionWindowClosed;

        if (ReferenceEquals(_scriptExecutionWindow, window))
        {
            _scriptExecutionWindow = null;
        }
    }

    private static void ActivateExecutionWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        _ = window.Activate();
        _ = window.Focus();
    }

    private string BuildDefaultSaveFileName()
    {
        if (!string.IsNullOrWhiteSpace(CurrentScriptFilePath))
        {
            return Path.GetFileName(CurrentScriptFilePath);
        }

        return BuildUntitledFileName();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private static void ReplaceLanguageOptionsPreservingCodes(
        ObservableCollection<LanguageOption> collection,
        IReadOnlyList<LanguageOption> items)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);

        // Keep the collection populated while reordering so monkey target ComboBox bindings
        // do not see a transient empty ItemsSource and write back an empty selection.
        for (var targetIndex = 0; targetIndex < items.Count; targetIndex++)
        {
            var desired = items[targetIndex];
            var existingIndex = FindLanguageOptionIndex(collection, desired.Code, targetIndex);
            if (existingIndex < 0)
            {
                collection.Insert(targetIndex, desired);
                continue;
            }

            if (existingIndex != targetIndex)
            {
                collection.Move(existingIndex, targetIndex);
            }

            var current = collection[targetIndex];
            if (!string.Equals(current.Code, desired.Code, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.DisplayName, desired.DisplayName, StringComparison.Ordinal))
            {
                collection[targetIndex] = desired;
            }
        }

        while (collection.Count > items.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private static int FindLanguageOptionIndex(
        IReadOnlyList<LanguageOption> collection,
        string code,
        int startIndex)
    {
        for (var index = startIndex; index < collection.Count; index++)
        {
            if (string.Equals(collection[index].Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.TargetCollection != InstructionSequence)
        {
            dropInfo.Effects = System.Windows.DragDropEffects.None;
            return;
        }

        if (TryGetTemplates(dropInfo.Data).Count > 0)
        {
            dropInfo.Effects = System.Windows.DragDropEffects.Copy;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            return;
        }

        if (TryGetInstructionInstances(dropInfo.Data).Count > 0)
        {
            dropInfo.Effects = System.Windows.DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            return;
        }

        dropInfo.Effects = System.Windows.DragDropEffects.None;
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.TargetCollection is not IList targetCollection || targetCollection != InstructionSequence)
        {
            return;
        }

        var insertIndex = dropInfo.InsertIndex;
        var templates = TryGetTemplates(dropInfo.Data);
        if (templates.Count > 0)
        {
            ExecuteTrackedSequenceMutation(() =>
            {
                ScriptInstructionInstance? firstAdded = null;
                foreach (var template in templates)
                {
                    var instance = CreateInstructionInstance(template);
                    targetCollection.Insert(insertIndex, instance);
                    firstAdded ??= instance;
                    insertIndex++;
                }

                if (firstAdded is not null && templates.Count == 1)
                {
                    SelectedSequenceInstruction = firstAdded;
                }
            });

            return;
        }

        var instancesToMove = TryGetInstructionInstances(dropInfo.Data)
            .Where(InstructionSequence.Contains)
            .Distinct()
            .ToList();

        if (instancesToMove.Count == 0)
        {
            return;
        }

        var oldIndexes = instancesToMove
            .Select(InstructionSequence.IndexOf)
            .OrderBy(index => index)
            .ToList();

        if (oldIndexes.Count == 0)
        {
            return;
        }

        var orderedInstances = oldIndexes.Select(index => InstructionSequence[index]).ToList();
        var removedBeforeInsert = oldIndexes.Count(index => index < insertIndex);
        insertIndex -= removedBeforeInsert;

        var noPositionChange = oldIndexes
            .Select((index, offset) => index == insertIndex + offset)
            .All(matches => matches);

        if (noPositionChange)
        {
            return;
        }

        ExecuteTrackedSequenceMutation(() =>
        {
            for (var index = orderedInstances.Count - 1; index >= 0; index--)
            {
                InstructionSequence.Remove(orderedInstances[index]);
            }

            foreach (var instance in orderedInstances)
            {
                InstructionSequence.Insert(insertIndex, instance);
                insertIndex++;
            }

            if (orderedInstances.Count == 1)
            {
                SelectedSequenceInstruction = orderedInstances[0];
            }
        });
    }

    private void OnInstructionSequenceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ScriptInstructionInstance>())
            {
                item.PropertyChanged -= OnInstructionPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ScriptInstructionInstance>())
            {
                item.PropertyChanged += OnInstructionPropertyChanged;
            }
        }

        if (!_isRestoringHistory && !_suppressHistoryTracking)
        {
            PushUndoSnapshot();
            _sequenceSnapshot = CaptureSequenceSnapshot();
        }

        if (_isRestoringHistory || _suppressHistoryTracking)
        {
            _pendingMonkeyObjectOptionsRebuild = true;
            OnPropertyChanged(nameof(ShowSequenceEmptyState));
            RefreshHistoryCommandState();
            return;
        }

        OnPropertyChanged(nameof(ShowSequenceEmptyState));
        RebuildMonkeyObjectOptions();
        RefreshHistoryCommandState();
    }

    private void OnInstructionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ScriptInstructionInstance instruction)
        {
            return;
        }

        if (_isUpdatingSequenceInternals)
        {
            return;
        }

        if (ReferenceEquals(instruction, _activeCoordinateSelectionInstruction) &&
            !CanContinueCoordinateSelection(instruction, _activeCoordinateSelectionTarget))
        {
            CancelCoordinateSelection();
        }

        if (string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.TargetMonkeyBindingId), StringComparison.Ordinal))
        {
            SynchronizeTargetMonkeyObjectId(instruction);
        }

        if (instruction.Type == ScriptCommandType.PlaceMonkey &&
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.SelectedMonkeyTower), StringComparison.Ordinal))
        {
            if (!_isRestoringHistory && !_suppressHistoryTracking)
            {
                PushUndoSnapshot();
                _sequenceSnapshot = CaptureSequenceSnapshot();
            }

            RebuildMonkeyObjectOptions();
            RefreshHistoryCommandState();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.DisplayName), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.Description), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.MonkeyBindingId), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.MonkeyObjectId), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.TargetMonkeyObjectId), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.ShowUpgradePathSelector), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.ShowNextRoundSendCount), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.ShowWaitTimeMilliseconds), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.ShowWaitGoldAmount), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.ShowWaitRoundCount), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.ShowWaitCoordinateColor), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.ShowAbilityCoordinateInputs), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(ScriptInstructionInstance.ShowPlacementCoordinateInputs), StringComparison.Ordinal))
        {
            return;
        }

        if (!_isRestoringHistory && !_suppressHistoryTracking)
        {
            PushUndoSnapshot();
            _sequenceSnapshot = CaptureSequenceSnapshot();
            RefreshHistoryCommandState();
        }

        if (_scriptEditorSequenceService.ShouldRefreshAllInstructionDisplayNames(instruction, e.PropertyName))
        {
            _scriptEditorSequenceService.UpdateInstructionDisplayNames(InstructionSequence, _localizationService);
            return;
        }

        _scriptEditorSequenceService.UpdateInstructionDisplayName(instruction, InstructionSequence, _localizationService);
    }

    private static bool CanContinueCoordinateSelection(ScriptInstructionInstance instruction, CoordinateSelectionTarget target)
    {
        return target switch
        {
            CoordinateSelectionTarget.Position => instruction.Type is ScriptCommandType.PlaceMonkey or ScriptCommandType.MouseClick or ScriptCommandType.ModifyMonkeyCoordinate
                || (instruction.Type == ScriptCommandType.PlaceHeroInventory && instruction.ShowPlacementCoordinateInputs),
            CoordinateSelectionTarget.Ability => instruction.ShowAbilityCoordinateInputs,
            CoordinateSelectionTarget.WaitColor => instruction.ShowWaitCoordinateColor,
            _ => false
        };
    }

    private void RebuildMonkeyObjectOptions()
    {
        _isUpdatingSequenceInternals = true;
        try
        {
            var options = _scriptEditorSequenceService.RebuildMonkeyObjectOptions(InstructionSequence, _localizationService);
            ReplaceLanguageOptionsPreservingCodes(MonkeyObjectOptions, options);
        }
        finally
        {
            _isUpdatingSequenceInternals = false;
        }
    }

    private static string NormalizeScriptVersion(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? ScriptDocumentFormat.DefaultScriptVersion : value.Trim();
    }

    private string ResolveTargetMonkeyObjectKey(ScriptInstructionInstance instruction)
    {
        return _scriptEditorSequenceService.ResolveTargetMonkeyObjectKey(instruction, InstructionSequence);
    }

    private void SynchronizeTargetMonkeyObjectId(ScriptInstructionInstance instruction)
    {
        if (!_scriptEditorInstructionService.RequiresMonkeyObjectTarget(instruction))
        {
            return;
        }

        var targetObjectKey = ResolveTargetMonkeyObjectKey(instruction);
        if (string.Equals(instruction.TargetMonkeyObjectId, targetObjectKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var restoreUpdatingState = _isUpdatingSequenceInternals;
        _isUpdatingSequenceInternals = true;
        try
        {
            instruction.TargetMonkeyObjectId = targetObjectKey;
        }
        finally
        {
            _isUpdatingSequenceInternals = restoreUpdatingState;
        }
    }

    private void DeleteSelectedSequenceInstructions(IList? selectedItems)
    {
        var selectedInstructions = selectedItems?
            .OfType<ScriptInstructionInstance>()
            .Distinct()
            .Select(instruction => new
            {
                Instruction = instruction,
                Index = InstructionSequence.IndexOf(instruction)
            })
            .Where(item => item.Index >= 0)
            .OrderBy(item => item.Index)
            .ToList() ?? [];
        if (selectedInstructions.Count == 0)
        {
            return;
        }

        ExecuteTrackedSequenceMutation(() =>
        {
            var nextSelectionIndex = selectedInstructions[0].Index;

            foreach (var instruction in selectedInstructions)
            {
                InstructionSequence.Remove(instruction.Instruction);
            }

            if (InstructionSequence.Count == 0)
            {
                SelectedSequenceInstruction = null;
                return;
            }

            var resolvedSelectionIndex = Math.Min(nextSelectionIndex, InstructionSequence.Count - 1);
            SelectedSequenceInstruction = InstructionSequence[resolvedSelectionIndex];
        });
    }

    private static bool CanDeleteSelectedSequenceInstructions(IList? selectedItems)
    {
        return selectedItems?.OfType<ScriptInstructionInstance>().Any() == true;
    }

    private void AddInstructionToSequence(ScriptInstructionTemplate? template)
    {
        if (template is null)
        {
            return;
        }

        ExecuteTrackedSequenceMutation(() =>
        {
            var instance = CreateInstructionInstance(template);
            InstructionSequence.Add(instance);
            SelectedSequenceInstruction = instance;
        });
    }

    private ScriptInstructionInstance CreateInstructionInstance(ScriptInstructionTemplate template)
    {
        var instruction = _scriptEditorInstructionService.CreateInstructionInstance(
            template,
            MonkeyObjectOptions.FirstOrDefault()?.Code ?? string.Empty,
            InventoryOptions.FirstOrDefault()?.Code ?? string.Empty,
            ActivatedAbilityOptions.FirstOrDefault()?.Code ?? string.Empty);
        instruction.TargetMonkeyObjectId = ResolveTargetMonkeyObjectKey(instruction);
        _scriptEditorSequenceService.UpdateInstructionDisplayName(instruction, InstructionSequence, _localizationService);
        return instruction;
    }

    private void ApplyScriptMetadata(ScriptMetadataDocument? metadata)
    {
        metadata ??= new ScriptMetadataDocument();
        _scriptId = string.IsNullOrWhiteSpace(metadata.ScriptId)
            ? Guid.NewGuid().ToString("N")
            : metadata.ScriptId.Trim();

        ScriptVersion = NormalizeScriptVersion(metadata.ScriptVersion);
        ScriptDescription = metadata.Description;

        SelectedMap = Enum.TryParse<GameMapType>(metadata.Map, true, out var map)
            ? map
            : GameMapType.MonkeyMeadow;

        var difficultyCode = Enum.TryParse<StageDifficulty>(metadata.Difficulty, true, out var difficulty)
            ? difficulty.ToString()
            : StageDifficulty.Medium.ToString();
        var modeCode = Enum.TryParse<StageMode>(metadata.Mode, true, out var mode)
            ? mode.ToString()
            : StageMode.Standard.ToString();
        var heroCode = Enum.TryParse<HeroType>(metadata.Hero, true, out var hero)
            ? hero.ToString()
            : HeroType.Quincy.ToString();

        SelectedDifficultyOption = DifficultyOptions.FirstOrDefault(x => string.Equals(x.Code, difficultyCode, StringComparison.OrdinalIgnoreCase))
                                   ?? DifficultyOptions.FirstOrDefault();
        SelectedModeOption = ModeOptions.FirstOrDefault(x => string.Equals(x.Code, modeCode, StringComparison.OrdinalIgnoreCase))
                             ?? ModeOptions.FirstOrDefault();
        SelectedHeroOption = HeroOptions.FirstOrDefault(x => string.Equals(x.Code, heroCode, StringComparison.OrdinalIgnoreCase))
                             ?? HeroOptions.FirstOrDefault();
        SetSelectedTags(metadata.Tags);
    }

    private void ReplaceSequenceWithInstructions(IEnumerable<ScriptInstructionInstance> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        _undoHistory.Clear();
        _redoHistory.Clear();
        _suppressHistoryTracking = true;
        _pendingMonkeyObjectOptionsRebuild = false;
        try
        {
            InstructionSequence.Clear();
            foreach (var instruction in instructions)
            {
                InstructionSequence.Add(instruction);
            }

            SelectedSequenceInstruction = InstructionSequence.FirstOrDefault();
        }
        finally
        {
            _suppressHistoryTracking = false;
            FlushPendingMonkeyObjectOptionsRebuild();
        }

        _sequenceSnapshot = CaptureSequenceSnapshot();
        RefreshHistoryCommandState();
    }

    private static IReadOnlyList<ScriptInstructionTemplate> TryGetTemplates(object? data)
    {
        if (data is ScriptInstructionTemplate template)
        {
            return [template];
        }

        return data is IEnumerable enumerable
            ? enumerable.OfType<ScriptInstructionTemplate>().ToList()
            : [];
    }

    private static IReadOnlyList<ScriptInstructionInstance> TryGetInstructionInstances(object? data)
    {
        if (data is ScriptInstructionInstance instance)
        {
            return [instance];
        }

        return data is IEnumerable enumerable
            ? enumerable.OfType<ScriptInstructionInstance>().ToList()
            : [];
    }

    private void CutSelectedSequenceInstructions(IList? selectedItems)
    {
        if (!CanCutSelectedSequenceInstructions(selectedItems))
        {
            return;
        }

        CopySelectedSequenceInstructions(selectedItems);
        DeleteSelectedSequenceInstructions(selectedItems);
    }

    private static bool CanCutSelectedSequenceInstructions(IList? selectedItems)
    {
        return CanCopySelectedSequenceInstructions(selectedItems);
    }

    private void CopySelectedSequenceInstructions(IList? selectedItems)
    {
        var selectedInstructions = selectedItems?.OfType<ScriptInstructionInstance>().Distinct().ToList() ?? [];
        if (selectedInstructions.Count == 0)
        {
            return;
        }

        _clipboardSequenceInstructions.Clear();
        foreach (var instruction in selectedInstructions)
        {
            _clipboardSequenceInstructions.Add(_scriptEditorInstructionService.CloneInstructionInstance(instruction));
        }

        PasteSequenceInstructionsCommand.NotifyCanExecuteChanged();
    }

    private static bool CanCopySelectedSequenceInstructions(IList? selectedItems)
    {
        return selectedItems?.OfType<ScriptInstructionInstance>().Any() == true;
    }

    private void PasteSequenceInstructions(IList? selectedItems)
    {
        if (_clipboardSequenceInstructions.Count == 0)
        {
            return;
        }

        ExecuteTrackedSequenceMutation(() =>
        {
            var insertIndex = GetPasteInsertIndex(selectedItems);
            var pastedInstructions = _scriptEditorInstructionService.CloneInstructionsForPaste(_clipboardSequenceInstructions);
            ScriptInstructionInstance? firstPasted = null;

            foreach (var pastedInstruction in pastedInstructions)
            {
                InstructionSequence.Insert(insertIndex, pastedInstruction);
                firstPasted ??= pastedInstruction;
                insertIndex++;
            }

            if (firstPasted is not null)
            {
                SelectedSequenceInstruction = firstPasted;
            }
        });
    }

    private bool CanPasteSequenceInstructions(IList? _)
    {
        return _clipboardSequenceInstructions.Count > 0;
    }

    private void ExecuteTrackedSequenceMutation(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        if (_isRestoringHistory)
        {
            mutation();
            return;
        }

        PushUndoSnapshot();
        _suppressHistoryTracking = true;
        _pendingMonkeyObjectOptionsRebuild = false;
        try
        {
            mutation();
        }
        finally
        {
            _suppressHistoryTracking = false;
            FlushPendingMonkeyObjectOptionsRebuild();
            _sequenceSnapshot = CaptureSequenceSnapshot();
            RefreshHistoryCommandState();
        }
    }

    private void PushUndoSnapshot()
    {
        _undoHistory.Push(_scriptEditorInstructionService.CloneSnapshot(_sequenceSnapshot));
        _redoHistory.Clear();
    }

    private List<ScriptInstructionInstance> CaptureSequenceSnapshot()
    {
        return _scriptEditorInstructionService.CloneSnapshot(InstructionSequence);
    }

    private void UndoSequence()
    {
        if (_undoHistory.Count == 0)
        {
            return;
        }

        _isRestoringHistory = true;
        try
        {
            _redoHistory.Push(CaptureSequenceSnapshot());
            var snapshot = _undoHistory.Pop();
            RestoreSequenceSnapshot(snapshot);
            _sequenceSnapshot = CaptureSequenceSnapshot();
        }
        finally
        {
            _isRestoringHistory = false;
            RefreshHistoryCommandState();
        }
    }

    private void RedoSequence()
    {
        if (_redoHistory.Count == 0)
        {
            return;
        }

        _isRestoringHistory = true;
        try
        {
            _undoHistory.Push(CaptureSequenceSnapshot());
            var snapshot = _redoHistory.Pop();
            RestoreSequenceSnapshot(snapshot);
            _sequenceSnapshot = CaptureSequenceSnapshot();
        }
        finally
        {
            _isRestoringHistory = false;
            RefreshHistoryCommandState();
        }
    }

    private void RestoreSequenceSnapshot(IEnumerable<ScriptInstructionInstance> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _suppressHistoryTracking = true;
        _pendingMonkeyObjectOptionsRebuild = false;
        try
        {
            InstructionSequence.Clear();
            foreach (var instruction in _scriptEditorInstructionService.CloneSnapshot(snapshot))
            {
                InstructionSequence.Add(instruction);
            }

            SelectedSequenceInstruction = InstructionSequence.FirstOrDefault();
        }
        finally
        {
            _suppressHistoryTracking = false;
            FlushPendingMonkeyObjectOptionsRebuild();
        }
    }

    private void FlushPendingMonkeyObjectOptionsRebuild()
    {
        if (!_pendingMonkeyObjectOptionsRebuild)
        {
            return;
        }

        _pendingMonkeyObjectOptionsRebuild = false;
        RebuildMonkeyObjectOptions();
    }

    private bool CanUndoSequence()
    {
        return _undoHistory.Count > 0;
    }

    private bool CanRedoSequence()
    {
        return _redoHistory.Count > 0;
    }

    private void RefreshHistoryCommandState()
    {
        UndoSequenceCommand.NotifyCanExecuteChanged();
        RedoSequenceCommand.NotifyCanExecuteChanged();
    }

    private int GetPasteInsertIndex(IList? selectedItems)
    {
        var selectedInstructions = selectedItems?.OfType<ScriptInstructionInstance>()
            .Where(InstructionSequence.Contains)
            .Distinct()
            .ToList() ?? [];

        if (selectedInstructions.Count == 0)
        {
            return InstructionSequence.Count;
        }

        return selectedInstructions.Max(InstructionSequence.IndexOf) + 1;
    }

    private void BuildInstructionLibrary()
    {
        ReplaceCollection(InstructionLibrary, _scriptEditorInstructionService.CreateInstructionLibrary());
    }

    private void StartCoordinateSelection(string? target)
    {
        var selectionTarget = ParseCoordinateSelectionTarget(target);
        if (selectionTarget == CoordinateSelectionTarget.None || SelectedSequenceInstruction is null)
        {
            return;
        }

        if (!CanContinueCoordinateSelection(SelectedSequenceInstruction, selectionTarget))
        {
            return;
        }

        _activeCoordinateSelectionInstruction = SelectedSequenceInstruction;
        _activeCoordinateSelectionTarget = selectionTarget;
        _wasRightMouseButtonDown = NativeWindowHelper.IsRightMouseButtonDown();
        RaiseCoordinateSelectionStateProperties();
        UpdateCoordinateSelectionPreview();
        _coordinateSelectionTimer.Start();
    }

    private void CancelCoordinateSelection()
    {
        if (_activeCoordinateSelectionInstruction is null && _activeCoordinateSelectionTarget == CoordinateSelectionTarget.None)
        {
            return;
        }

        _coordinateSelectionTimer.Stop();
        _wasRightMouseButtonDown = false;
        ClearCoordinateSelectionPreview();
        _activeCoordinateSelectionInstruction = null;
        _activeCoordinateSelectionTarget = CoordinateSelectionTarget.None;
        RaiseCoordinateSelectionStateProperties();
    }

    private bool IsCoordinateSelectionActive(CoordinateSelectionTarget target)
    {
        return target != CoordinateSelectionTarget.None &&
               ReferenceEquals(_activeCoordinateSelectionInstruction, SelectedSequenceInstruction) &&
               _activeCoordinateSelectionTarget == target;
    }

    private static CoordinateSelectionTarget ParseCoordinateSelectionTarget(string? target)
    {
        return target?.Trim() switch
        {
            "Position" => CoordinateSelectionTarget.Position,
            "Ability" => CoordinateSelectionTarget.Ability,
            "WaitColor" => CoordinateSelectionTarget.WaitColor,
            _ => CoordinateSelectionTarget.None
        };
    }

    private void RaiseCoordinateSelectionStateProperties()
    {
        OnPropertyChanged(nameof(IsPositionCoordinateSelectionActive));
        OnPropertyChanged(nameof(IsAbilityCoordinateSelectionActive));
        OnPropertyChanged(nameof(IsWaitColorCoordinateSelectionActive));
    }

    private void OnCoordinateSelectionTimerTick(object? sender, EventArgs e)
    {
        UpdateCoordinateSelectionPreview();
    }

    private void UpdateCoordinateSelectionPreview()
    {
        if (_activeCoordinateSelectionInstruction is null ||
            _activeCoordinateSelectionTarget == CoordinateSelectionTarget.None)
        {
            _coordinateSelectionTimer.Stop();
            ClearCoordinateSelectionPreview();
            return;
        }

        if (!CanContinueCoordinateSelection(_activeCoordinateSelectionInstruction, _activeCoordinateSelectionTarget))
        {
            CancelCoordinateSelection();
            return;
        }

        if (!_maskWindowService.TryShowTargetOverlay(out var windowInfo))
        {
            return;
        }

        if (!NativeWindowHelper.TryGetCursorPosition(out var cursorPosition))
        {
            return;
        }

        var clientRelativePoint = windowInfo.ScreenToClient(cursorPosition);
        var referenceRelativePoint = _coordinateTransformService.ToReferenceCoordinateFromScreen(cursorPosition, windowInfo);
        var scriptRelativePoint = _coordinateTransformService.ToScriptCoordinate(referenceRelativePoint, windowInfo);
        var isInsideClientArea = _coordinateTransformService.GetReferenceBounds(windowInfo).Contains(cursorPosition);

        var sampledColorHex = TryGetScreenPixelColorHex(cursorPosition, out var screenPixelColorHex)
            ? screenPixelColorHex
            : "#000000";

        UpdateCoordinateSelectionAnchor(windowInfo, clientRelativePoint, scriptRelativePoint, sampledColorHex, isInsideClientArea);

        var isRightMouseButtonDown = NativeWindowHelper.IsRightMouseButtonDown();
        if (isRightMouseButtonDown && !_wasRightMouseButtonDown && isInsideClientArea)
        {
            CompleteCoordinateSelection(scriptRelativePoint.X, scriptRelativePoint.Y, sampledColorHex);
            return;
        }

        _wasRightMouseButtonDown = isRightMouseButtonDown;
    }

    private void UpdateCoordinateSelectionAnchor(GameWindowInfo windowInfo, Point clientRelativePoint, Point scriptRelativePoint, string sampledColorHex, bool isInsideTargetWindow)
    {
        var strokeColor = isInsideTargetWindow ? CoordinateSelectionActiveColor : CoordinateSelectionInactiveColor;
        var hasAspectRatioWarning = !_coordinateTransformService.HasReferenceAspectRatio(windowInfo);
        var labelOffsetX = clientRelativePoint.X > windowInfo.ClientWidth - (hasAspectRatioWarning ? 240d : 180d)
            ? (hasAspectRatioWarning ? -218d : -158d)
            : 14d;
        var labelOffsetY = clientRelativePoint.Y < 52 ? 14d : -46d;
        var label = string.Format(
            _localizationService.T("Editor.Property.CoordinateSelectionLabel"),
            scriptRelativePoint.X.ToString("0.##"),
            scriptRelativePoint.Y.ToString("0.##"),
            clientRelativePoint.X.ToString("0.##"),
            clientRelativePoint.Y.ToString("0.##"),
            sampledColorHex);

        if (hasAspectRatioWarning)
        {
            label = $"{label}\n{_localizationService.T("Editor.Property.CoordinateSelectionAspectWarning")}";
        }

        if (_coordinateSelectionAnchorId is null)
        {
            _coordinateSelectionAnchorId = _maskWindowService.RegisterAnchor(
                clientRelativePoint,
                strokeColor,
                strokeThickness: 2,
                crosshairLength: 12,
                gapRadius: 5,
                ringRadius: 16,
                label: label,
                labelForegroundColor: Colors.White,
                labelBackgroundColor: CoordinateSelectionLabelBackgroundColor);
        }

        _ = _maskWindowService.UpdateAnchor(_coordinateSelectionAnchorId.Value, anchor =>
        {
            anchor.Center = clientRelativePoint;
            anchor.StrokeColor = strokeColor;
            anchor.Label = label;
            anchor.LabelFontSize = 14;
            anchor.LabelForegroundColor = Colors.White;
            anchor.LabelBackgroundColor = CoordinateSelectionLabelBackgroundColor;
            anchor.LabelOffsetX = labelOffsetX;
            anchor.LabelOffsetY = labelOffsetY;
            anchor.LabelPadding = new Thickness(8, 5, 8, 5);
            anchor.RingRadius = 16;
            anchor.CrosshairLength = 12;
            anchor.GapRadius = 5;
        });
    }

    private void CompleteCoordinateSelection(double x, double y, string? sampledColorHex = null)
    {
        var instruction = _activeCoordinateSelectionInstruction;
        var selectionTarget = _activeCoordinateSelectionTarget;
        if (instruction is null || selectionTarget == CoordinateSelectionTarget.None)
        {
            CancelCoordinateSelection();
            return;
        }

        ExecuteTrackedSequenceMutation(() =>
        {
            switch (selectionTarget)
            {
                case CoordinateSelectionTarget.Position:
                    instruction.PositionX = x;
                    instruction.PositionY = y;
                    break;
                case CoordinateSelectionTarget.Ability:
                    instruction.AbilityCoordinateX = x;
                    instruction.AbilityCoordinateY = y;
                    break;
                case CoordinateSelectionTarget.WaitColor:
                    instruction.WaitColorCoordinateX = x;
                    instruction.WaitColorCoordinateY = y;
                    if (!string.IsNullOrWhiteSpace(sampledColorHex))
                    {
                        instruction.WaitColorHex = sampledColorHex;
                    }
                    break;
            }
        });

        CancelCoordinateSelection();
    }

    private static bool TryGetScreenPixelColorHex(Point screenPoint, out string colorHex)
    {
        colorHex = string.Empty;

        using User32.SafeReleaseHDC screenDc = User32.GetDC(IntPtr.Zero);
        if (screenDc.IsInvalid)
        {
            return false;
        }

        var colorRef = Gdi32.GetPixel(
            screenDc,
            (int)Math.Round(screenPoint.X),
            (int)Math.Round(screenPoint.Y));

        if ((uint)colorRef == uint.MaxValue)
        {
            return false;
        }

        colorHex = $"#{colorRef.R:X2}{colorRef.G:X2}{colorRef.B:X2}";
        return true;
    }

    private void ClearCoordinateSelectionPreview()
    {
        if (_coordinateSelectionAnchorId is not { } anchorId)
        {
            return;
        }

        _ = _maskWindowService.RemoveElement(anchorId);
        _coordinateSelectionAnchorId = null;
    }

    private void BuildScriptParameterOptions()
    {
        var options = _scriptEditorOptionService.CreateParameterOptions(_localizationService);
        ReplaceCollection(UpgradePathOptions, options.UpgradePathOptions);
        ReplaceCollection(SwitchDirectionOptions, options.SwitchDirectionOptions);
        ReplaceCollection(MonkeyAbilityOptions, options.MonkeyAbilityOptions);
        ReplaceCollection(InventoryOptions, options.InventoryOptions);
        ReplaceCollection(ActivatedAbilityOptions, options.ActivatedAbilityOptions);
        ReplaceCollection(NextRoundActionOptions, options.NextRoundActionOptions);
        ReplaceCollection(WaitModeOptions, options.WaitModeOptions);
    }

    private void BuildMetadataOptions()
    {
        var selectedDifficultyCode = SelectedDifficultyOption?.Code ?? StageDifficulty.Medium.ToString();
        var selectedModeCode = SelectedModeOption?.Code ?? StageMode.Standard.ToString();
        var selectedHeroCode = SelectedHeroOption?.Code ?? HeroType.Quincy.ToString();
        var selectedTags = SelectedTagOptions.Select(x => x.Code).ToList();
        var options = _scriptEditorOptionService.CreateMetadataOptions(_localizationService);
        ReplaceCollection(DifficultyOptions, options.DifficultyOptions);
        ReplaceCollection(ModeOptions, options.ModeOptions);
        ReplaceCollection(HeroOptions, options.HeroOptions);
        ReplaceCollection(TagOptions, options.TagOptions);

        SelectedDifficultyOption = DifficultyOptions.FirstOrDefault(x => x.Code == selectedDifficultyCode) ?? DifficultyOptions.FirstOrDefault();
        SelectedModeOption = ModeOptions.FirstOrDefault(x => x.Code == selectedModeCode) ?? ModeOptions.FirstOrDefault();
        SelectedHeroOption = HeroOptions.FirstOrDefault(x => x.Code == selectedHeroCode) ?? HeroOptions.FirstOrDefault();
        SetSelectedTags(selectedTags);
    }

    private void UpdateInstructionLocalization()
    {
        _scriptEditorSequenceService.UpdateInstructionLocalization(InstructionLibrary, InstructionSequence, _localizationService);
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(MapItems));
        OnPropertyChanged(nameof(MonkeyTowerItems));
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(WorkspaceDescription));
        OnPropertyChanged(nameof(PreviewHint));
        OnPropertyChanged(nameof(ScriptText));
        OnPropertyChanged(nameof(StatsTitle));
        OnPropertyChanged(nameof(TotalScriptsText));
        OnPropertyChanged(nameof(SharedScriptsText));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(DraftState));
        OnPropertyChanged(nameof(FileOpenText));
        OnPropertyChanged(nameof(FileSaveText));
        OnPropertyChanged(nameof(FileSaveAsText));
        OnPropertyChanged(nameof(FileNewText));
        OnPropertyChanged(nameof(FileCloseText));
        OnPropertyChanged(nameof(CurrentFileText));
        OnPropertyChanged(nameof(MetadataMapText));
        OnPropertyChanged(nameof(MetadataDifficultyText));
        OnPropertyChanged(nameof(MetadataModeText));
        OnPropertyChanged(nameof(MetadataHeroText));
        OnPropertyChanged(nameof(MetadataTagText));
        OnPropertyChanged(nameof(MetadataTagAddText));
        OnPropertyChanged(nameof(MetadataTagEmptyText));
        OnPropertyChanged(nameof(MetadataTagHintText));
        OnPropertyChanged(nameof(MetadataMapPlaceholderText));
        OnPropertyChanged(nameof(TagEditorWindowTitle));
        OnPropertyChanged(nameof(SelectedTagsSummaryText));
        OnPropertyChanged(nameof(DebugOpenRuntimeText));
        OnPropertyChanged(nameof(DebugRunText));
        OnPropertyChanged(nameof(DebugStepText));
        OnPropertyChanged(nameof(DebugStopText));
        OnPropertyChanged(nameof(DebugValidateText));
        OnPropertyChanged(nameof(LibraryTitle));
        OnPropertyChanged(nameof(SequenceTitle));
        OnPropertyChanged(nameof(PropertiesTitle));
        OnPropertyChanged(nameof(SequenceEmptyText));
        OnPropertyChanged(nameof(PropertiesEmptyText));
        OnPropertyChanged(nameof(PropertyMonkeyTowerText));
        OnPropertyChanged(nameof(PropertyCoordinateText));
        OnPropertyChanged(nameof(CoordinateSelectButtonText));
        OnPropertyChanged(nameof(CoordinateCancelButtonText));
        OnPropertyChanged(nameof(PropertyTargetMonkeyText));
        OnPropertyChanged(nameof(PropertyUpgradePathText));
        OnPropertyChanged(nameof(PropertyUpgradeCountText));
        OnPropertyChanged(nameof(PropertySwitchDirectionText));
        OnPropertyChanged(nameof(PropertySwitchCountText));
        OnPropertyChanged(nameof(PropertyAbilityText));
        OnPropertyChanged(nameof(PropertyNeedAbilityCoordinateText));
        OnPropertyChanged(nameof(PropertyInventoryText));
        OnPropertyChanged(nameof(PropertyActivatedAbilityText));
        OnPropertyChanged(nameof(PropertyNeedCoordinateText));
        OnPropertyChanged(nameof(PropertyNextRoundActionText));
        OnPropertyChanged(nameof(PropertyNextRoundSendCountText));
        OnPropertyChanged(nameof(PropertyNextRoundOperationIntervalMillisecondsText));
        OnPropertyChanged(nameof(PropertyWaitModeText));
        OnPropertyChanged(nameof(PropertyWaitTimeMillisecondsText));
        OnPropertyChanged(nameof(PropertyWaitGoldAmountText));
        OnPropertyChanged(nameof(PropertyWaitRoundCountText));
        OnPropertyChanged(nameof(PropertyWaitColorHexText));
        OnPropertyChanged(nameof(PropertyWaitColorToleranceText));
        OnPropertyChanged(nameof(PropertyClickCountText));
        OnPropertyChanged(nameof(PropertyCommentContentText));
        OnPropertyChanged(nameof(PropertyAdvancedText));
        OnPropertyChanged(nameof(PropertyPlacementDetectionText));
        OnPropertyChanged(nameof(PropertyPlacementFailureAdjustmentText));
        OnPropertyChanged(nameof(PropertyPlacementAttemptIntervalMillisecondsText));
        OnPropertyChanged(nameof(PropertyPlacementAdjustmentAttemptIntervalMillisecondsText));
        OnPropertyChanged(nameof(PropertyUpgradeDetectionText));
        OnPropertyChanged(nameof(PropertyUpgradeOperationIntervalMillisecondsText));
        OnPropertyChanged(nameof(PropertyMonkeyPanelDetectionText));
        OnPropertyChanged(nameof(PropertyMonkeyPanelOperationIntervalMillisecondsText));
        OnPropertyChanged(nameof(PropertySellDetectionText));
        OnPropertyChanged(nameof(PropertyClickIntervalMillisecondsText));
        OnPropertyChanged(nameof(PropertyIntervalToNextInstructionText));
        OnPropertyChanged(nameof(NonExecutableInstructionHintText));
        OnPropertyChanged(nameof(PropertyNotesText));
        OnPropertyChanged(nameof(DeleteSelectedInstructionText));
        OnPropertyChanged(nameof(CutSelectedInstructionText));
        OnPropertyChanged(nameof(CopySelectedInstructionText));
        OnPropertyChanged(nameof(PasteInstructionText));
    }
}
