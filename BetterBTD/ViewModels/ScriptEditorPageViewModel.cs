using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using BetterBTD.Helpers;
using BetterBTD.Models;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using Microsoft.Win32;
using Wpf.Ui.Violeta.Controls;

namespace BetterBTD.ViewModels;

public sealed class ScriptEditorPageViewModel : ObservableObject, IDropTarget
{
    private const string ScriptFileExtension = ".btd";
    private const string ScriptFileDialogFilter = "BetterBTD Script (*.btd)|*.btd|JSON File (*.json)|*.json|All Files (*.*)|*.*";

    private readonly LocalizationService _localizationService;
    private readonly AppDialogService _appDialogService;
    private readonly ScriptDocumentService _scriptDocumentService;
    private readonly List<ScriptInstructionInstance> _clipboardSequenceInstructions = [];
    private readonly Stack<List<ScriptInstructionInstance>> _undoHistory = [];
    private readonly Stack<List<ScriptInstructionInstance>> _redoHistory = [];
    private readonly string _emptyWorkspaceSnapshot;

    private string _scriptText = string.Empty;
    private string _scriptVersion = ScriptDocumentFormat.DefaultScriptVersion;
    private string _scriptCategory = ScriptDocumentCategories.Collection;
    private string _scriptName = string.Empty;
    private string _scriptDescription = string.Empty;
    private string _currentScriptFilePath = string.Empty;
    private GameMapType _selectedMap = GameMapType.MonkeyMeadow;
    private LanguageOption? _selectedDifficultyOption;
    private LanguageOption? _selectedModeOption;
    private LanguageOption? _selectedHeroOption;
    private ScriptInstructionTemplate? _selectedLibraryInstruction;
    private ScriptInstructionInstance? _selectedSequenceInstruction;
    private bool _isRestoringHistory;
    private bool _suppressHistoryTracking;
    private bool _isUpdatingSequenceInternals;
    private bool _pendingMonkeyObjectOptionsRebuild;
    private string _persistedWorkspaceSnapshot = string.Empty;
    private List<ScriptInstructionInstance> _sequenceSnapshot = [];

    public ScriptEditorPageViewModel(LocalizationService localizationService)
    {
        _localizationService = localizationService;
        _appDialogService = AppDialogService.Instance;
        _scriptDocumentService = ScriptDocumentService.Instance;
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
        CopySelectedSequenceInstructionsCommand = new RelayCommand<IList?>(CopySelectedSequenceInstructions, CanCopySelectedSequenceInstructions);
        PasteSequenceInstructionsCommand = new RelayCommand<IList?>(PasteSequenceInstructions, CanPasteSequenceInstructions);
        UndoSequenceCommand = new RelayCommand(UndoSequence, CanUndoSequence);
        RedoSequenceCommand = new RelayCommand(RedoSequence, CanRedoSequence);
        OpenScriptFileCommand = new RelayCommand(OpenScriptFile);
        SaveScriptFileCommand = new RelayCommand(SaveScriptFile);
        SaveScriptFileAsCommand = new RelayCommand(SaveScriptFileAs);
        CreateNewScriptFileCommand = new RelayCommand(CreateNewScriptFile);

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
    public IRelayCommand<IList?> CopySelectedSequenceInstructionsCommand { get; }
    public IRelayCommand<IList?> PasteSequenceInstructionsCommand { get; }
    public IRelayCommand UndoSequenceCommand { get; }
    public IRelayCommand RedoSequenceCommand { get; }
    public IRelayCommand OpenScriptFileCommand { get; }
    public IRelayCommand SaveScriptFileCommand { get; }
    public IRelayCommand SaveScriptFileAsCommand { get; }
    public IRelayCommand CreateNewScriptFileCommand { get; }

    public ObservableCollection<LanguageOption> DifficultyOptions { get; } = [];
    public ObservableCollection<LanguageOption> ModeOptions { get; } = [];
    public ObservableCollection<LanguageOption> HeroOptions { get; } = [];
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

    public string ScriptCategory
    {
        get => _scriptCategory;
        set => SetProperty(ref _scriptCategory, ScriptDocumentCategories.Normalize(value));
    }

    public string ScriptName
    {
        get => _scriptName;
        set => SetProperty(ref _scriptName, value?.Trim() ?? string.Empty);
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

            OnPropertyChanged(nameof(HasSelectedSequenceInstruction));
            OnPropertyChanged(nameof(ShowPropertiesEmptyState));
            OnPropertyChanged(nameof(ShowNonExecutableInstructionHint));
        }
    }

    public bool HasSelectedSequenceInstruction => SelectedSequenceInstruction is not null;

    public bool ShowPropertiesEmptyState => SelectedSequenceInstruction is null;

    public bool ShowNonExecutableInstructionHint => SelectedSequenceInstruction is { IsExecutable: false };

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
    public string MetadataMapPlaceholderText => _localizationService.T("Editor.Metadata.Map.Placeholder");

    public string ScriptCategoryAllText => _localizationService.T("Editor.Category.All");
    public string ScriptCategoryCollectionText => _localizationService.T("Editor.Category.Collection");
    public string ScriptCategoryBlackBorderText => _localizationService.T("Editor.Category.BlackBorder");
    public string ScriptCategoryRaceText => _localizationService.T("Editor.Category.Race");

    public string DebugRunText => _localizationService.T("Editor.Debug.Run");
    public string DebugStepText => _localizationService.T("Editor.Debug.Step");
    public string DebugStopText => _localizationService.T("Editor.Debug.Stop");
    public string DebugValidateText => _localizationService.T("Editor.Debug.Validate");

    public string LibraryTitle => _localizationService.T("Editor.Panel.Library.Title");
    public string SequenceTitle => _localizationService.T("Editor.Panel.Sequence.Title");
    public string PropertiesTitle => _localizationService.T("Editor.Panel.Properties.Title");
    public string SequenceEmptyText => _localizationService.T("Editor.Panel.Sequence.Empty");
    public string PropertiesEmptyText => _localizationService.T("Editor.Panel.Properties.Empty");
    public string PropertyMonkeyTowerText => _localizationService.T("Editor.Property.MonkeyTower");
    public string PropertyCoordinateText => _localizationService.T("Editor.Property.Coordinate");
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
    public string PropertyWaitModeText => _localizationService.T("Editor.Property.WaitMode");
    public string PropertyWaitTimeMillisecondsText => _localizationService.T("Editor.Property.WaitTimeMilliseconds");
    public string PropertyWaitGoldAmountText => _localizationService.T("Editor.Property.WaitGoldAmount");
    public string PropertyWaitRoundCountText => _localizationService.T("Editor.Property.WaitRoundCount");
    public string PropertyWaitColorHexText => _localizationService.T("Editor.Property.WaitColorHex");
    public string PropertyWaitColorToleranceText => _localizationService.T("Editor.Property.WaitColorTolerance");
    public string PropertyCommentContentText => _localizationService.T("Editor.Property.CommentContent");
    public string PropertyAdvancedText => _localizationService.T("Editor.Property.Advanced");
    public string PropertyIntervalToNextInstructionText => _localizationService.T("Editor.Property.IntervalToNextInstruction");
    public string NonExecutableInstructionHintText => _localizationService.T("Editor.Property.NonExecutableHint");
    public string PropertyNotesText => _localizationService.T("Editor.Property.Notes");
    public string DeleteSelectedInstructionText => _localizationService.T("Editor.Command.DeleteSelected");
    public string CopySelectedInstructionText => _localizationService.T("Editor.Command.CopySelected");
    public string PasteInstructionText => _localizationService.T("Editor.Command.Paste");

    public ScriptDocument ExportScriptDocument()
    {
        return new ScriptDocument
        {
            Metadata = new ScriptMetadataDocument
            {
                ScriptVersion = NormalizeScriptVersion(ScriptVersion),
                Category = ScriptDocumentCategories.Normalize(ScriptCategory),
                Name = ScriptName,
                Description = ScriptDescription,
                Map = SelectedMap.ToString(),
                Difficulty = SelectedDifficultyOption?.Code ?? StageDifficulty.Medium.ToString(),
                Mode = SelectedModeOption?.Code ?? StageMode.Standard.ToString(),
                Hero = SelectedHeroOption?.Code ?? HeroType.Quincy.ToString()
            },
            MonkeyObjects = BuildMonkeyObjectDocuments(),
            Instructions = BuildInstructionDocuments()
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
        var instructions = document.Instructions
            .Select(x => CreateInstructionInstanceFromDocument(x, monkeyObjectsByBindingId))
            .ToList();

        ReplaceSequenceWithInstructions(instructions);
        MarkWorkspaceAsPersisted();
    }

    public void SaveScriptDocument(string filePath)
    {
        _scriptDocumentService.Save(filePath, ExportScriptDocument());
        CurrentScriptFilePath = filePath;
        MarkWorkspaceAsPersisted();
    }

    public void LoadScriptDocument(string filePath)
    {
        var document = _scriptDocumentService.Load(filePath);
        ImportScriptDocument(document);
        CurrentScriptFilePath = filePath;
    }

    public void CreateNewScriptDocument()
    {
        ApplyScriptMetadata(new ScriptMetadataDocument());
        ReplaceSequenceWithInstructions([]);
        _clipboardSequenceInstructions.Clear();
        CurrentScriptFilePath = string.Empty;
        MarkWorkspaceAsPersisted();
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
            Filter = ScriptFileDialogFilter,
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
            Filter = ScriptFileDialogFilter,
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

    private void ShowSaveError(Exception ex)
    {
        ShowMessageDialog(
            _localizationService.T("Editor.File.SaveError.Title"),
            string.Format(_localizationService.T("Editor.File.SaveError.Message"), ex.Message));
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

    private string BuildDefaultSaveFileName()
    {
        if (!string.IsNullOrWhiteSpace(CurrentScriptFilePath))
        {
            return Path.GetFileName(CurrentScriptFilePath);
        }

        var baseName = string.IsNullOrWhiteSpace(ScriptName) ? "Untitled Script" : ScriptName;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalidChar, '_');
        }

        return $"{baseName}{ScriptFileExtension}";
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

        if (ShouldRefreshAllInstructionDisplayNames(instruction, e.PropertyName))
        {
            UpdateAllInstructionDisplayNames();
            return;
        }

        UpdateInstructionDisplayName(instruction);
    }

    private void RebuildMonkeyObjectOptions()
    {
        _isUpdatingSequenceInternals = true;
        try
        {
            var originalTargetBindingIds = InstructionSequence
                .Where(RequiresMonkeyObjectTarget)
                .ToDictionary(x => x, x => x.TargetMonkeyBindingId);
            var originalTargetObjectIds = InstructionSequence
                .Where(RequiresMonkeyObjectTarget)
                .ToDictionary(x => x, x => x.TargetMonkeyObjectId);

            var existingKeyCounts = InstructionSequence
                .Where(x => x.Type == ScriptCommandType.PlaceMonkey && !string.IsNullOrWhiteSpace(x.MonkeyObjectId))
                .GroupBy(x => x.MonkeyObjectId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

            var maxTowerIndexes = new Dictionary<MonkeyTowerType, int>();
            foreach (var key in existingKeyCounts.Keys)
            {
                if (!TryParseMonkeyObjectKey(key, out var towerType, out var index))
                {
                    continue;
                }

                maxTowerIndexes.TryGetValue(towerType, out var currentMaxIndex);
                maxTowerIndexes[towerType] = Math.Max(currentMaxIndex, index);
            }

            var bindingIdToObjectKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var objectKeyToBindingId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var options = new List<LanguageOption>();

            foreach (var instruction in InstructionSequence)
            {
                if (instruction.Type != ScriptCommandType.PlaceMonkey)
                {
                    continue;
                }

                var selectionCode = NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower);
                var originalKey = instruction.MonkeyObjectId;
                if (string.IsNullOrWhiteSpace(instruction.MonkeyBindingId))
                {
                    instruction.MonkeyBindingId = CreateMonkeyBindingId();
                }

                if (TryParseHeroSelection(selectionCode, out var heroType))
                {
                    var heroKey = BuildHeroObjectKey(heroType);
                    if (objectKeyToBindingId.TryGetValue(heroKey, out var existingHeroBindingId))
                    {
                        instruction.MonkeyBindingId = existingHeroBindingId;
                    }
                    else
                    {
                        objectKeyToBindingId[heroKey] = instruction.MonkeyBindingId;
                    }

                    instruction.MonkeyObjectId = heroKey;
                    bindingIdToObjectKey[instruction.MonkeyBindingId] = heroKey;

                    if (options.All(x => !string.Equals(x.Code, instruction.MonkeyBindingId, StringComparison.OrdinalIgnoreCase)))
                    {
                        options.Add(new LanguageOption
                        {
                            Code = instruction.MonkeyBindingId,
                            DisplayName = BuildHeroObjectDisplayName(heroType)
                        });
                    }

                    continue;
                }

                var towerType = TryParseTowerSelection(selectionCode, out var parsedTowerType)
                    ? parsedTowerType
                    : MonkeyTowerType.DartMonkey;
                var key = ResolveMonkeyObjectKey(originalKey, towerType, maxTowerIndexes, usedKeys);
                instruction.MonkeyObjectId = key;
                bindingIdToObjectKey[instruction.MonkeyBindingId] = key;
                objectKeyToBindingId.TryAdd(key, instruction.MonkeyBindingId);

                usedKeys.Add(key);

                options.Add(new LanguageOption
                {
                    Code = instruction.MonkeyBindingId,
                    DisplayName = BuildMonkeyObjectDisplayName(key)
                });
            }

            MonkeyObjectOptions.Clear();
            foreach (var option in options)
            {
                MonkeyObjectOptions.Add(option);
            }

            var availableBindingIds = options.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var instruction in InstructionSequence.Where(RequiresMonkeyObjectTarget))
            {
                var targetBindingId = originalTargetBindingIds.GetValueOrDefault(instruction) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(targetBindingId))
                {
                    var targetObjectId = originalTargetObjectIds.GetValueOrDefault(instruction) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(targetObjectId) &&
                        objectKeyToBindingId.TryGetValue(targetObjectId, out var resolvedBindingId))
                    {
                        targetBindingId = resolvedBindingId;
                    }
                }

                if (string.IsNullOrWhiteSpace(targetBindingId) ||
                    !availableBindingIds.Contains(targetBindingId))
                {
                    targetBindingId = string.Empty;
                }

                instruction.TargetMonkeyBindingId = targetBindingId;
                instruction.TargetMonkeyObjectId = bindingIdToObjectKey.GetValueOrDefault(targetBindingId, string.Empty);
            }
        }
        finally
        {
            _isUpdatingSequenceInternals = false;
        }

        UpdateAllInstructionDisplayNames();
    }

    private static string BuildMonkeyObjectKey(MonkeyTowerType towerType, int index)
    {
        return $"{towerType}:{index}";
    }

    private static string CreateMonkeyBindingId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string ResolveMonkeyObjectKey(
        string? currentKey,
        MonkeyTowerType towerType,
        IDictionary<MonkeyTowerType, int> maxTowerIndexes,
        ISet<string> usedKeys)
    {
        if (TryParseMonkeyObjectKey(currentKey, out var currentTowerType, out _) &&
            currentTowerType == towerType &&
            !usedKeys.Contains(currentKey!))
        {
            return currentKey!;
        }

        maxTowerIndexes.TryGetValue(towerType, out var currentMaxIndex);
        var nextIndex = currentMaxIndex + 1;
        maxTowerIndexes[towerType] = nextIndex;
        return BuildMonkeyObjectKey(towerType, nextIndex);
    }

    private static bool TryParseMonkeyObjectKey(string? objectKey, out MonkeyTowerType towerType, out int index)
    {
        towerType = default;
        index = 0;
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return false;
        }

        var separatorIndex = objectKey.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= objectKey.Length - 1)
        {
            return false;
        }

        var towerText = objectKey.Substring(0, separatorIndex);
        var indexText = objectKey.Substring(separatorIndex + 1);
        return Enum.TryParse(towerText, out towerType) &&
               int.TryParse(indexText, out index) &&
               index > 0;
    }

    private static string BuildHeroObjectKey(HeroType heroType)
    {
        return $"Hero:{heroType}";
    }

    private static string BuildTowerSelectionCode(MonkeyTowerType towerType)
    {
        return $"Tower:{towerType}";
    }

    private static bool IsInventorySelection(string value)
    {
        return Enum.TryParse<InventoryType>(value, out _);
    }

    private static bool IsActivatedAbilitySelection(string value)
    {
        return Enum.TryParse<ActivatedAbilityType>(value, out _);
    }

    private static string NormalizeScriptVersion(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? ScriptDocumentFormat.DefaultScriptVersion : value.Trim();
    }

    private static string NormalizePlaceSelectionCode(string? selectionCode)
    {
        if (string.IsNullOrWhiteSpace(selectionCode))
        {
            return BuildTowerSelectionCode(MonkeyTowerType.DartMonkey);
        }

        if (selectionCode.StartsWith("Tower:", StringComparison.OrdinalIgnoreCase) ||
            selectionCode.StartsWith("Hero:", StringComparison.OrdinalIgnoreCase))
        {
            return selectionCode;
        }

        return $"Tower:{selectionCode}";
    }

    private static bool TryParseTowerSelection(string selectionCode, out MonkeyTowerType towerType)
    {
        towerType = default;
        if (!selectionCode.StartsWith("Tower:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = selectionCode.Substring("Tower:".Length);
        return Enum.TryParse(raw, out towerType);
    }

    private static bool TryParseHeroSelection(string selectionCode, out HeroType heroType)
    {
        heroType = default;
        if (!selectionCode.StartsWith("Hero:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = selectionCode.Substring("Hero:".Length);
        return Enum.TryParse(raw, out heroType);
    }

    private string BuildMonkeyObjectDisplayName(MonkeyTowerType towerType, int index)
    {
        var tower = GameElementCatalog.MonkeyTowers.FirstOrDefault(x => x.Type == towerType);
        var towerName = tower is null ? towerType.ToString() : _localizationService.T(tower.NameKey);
        return $"{towerName}{index}";
    }

    private string BuildMonkeyObjectDisplayName(string objectKey)
    {
        if (TryParseMonkeyObjectKey(objectKey, out var towerType, out var index))
        {
            return BuildMonkeyObjectDisplayName(towerType, index);
        }

        if (TryParseHeroSelection(objectKey, out var heroType))
        {
            return BuildHeroObjectDisplayName(heroType);
        }

        return objectKey;
    }

    private string BuildHeroObjectDisplayName(HeroType heroType)
    {
        var hero = GameElementCatalog.Heroes.FirstOrDefault(x => x.Type == heroType);
        return hero is null ? heroType.ToString() : _localizationService.T(hero.NameKey);
    }

    private string ResolveTargetMonkeyObjectKey(ScriptInstructionInstance instruction)
    {
        if (!RequiresMonkeyObjectTarget(instruction))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            var placeInstruction = InstructionSequence.FirstOrDefault(x =>
                x.Type == ScriptCommandType.PlaceMonkey &&
                string.Equals(x.MonkeyBindingId, instruction.TargetMonkeyBindingId, StringComparison.OrdinalIgnoreCase));
            if (placeInstruction is not null && !string.IsNullOrWhiteSpace(placeInstruction.MonkeyObjectId))
            {
                return placeInstruction.MonkeyObjectId;
            }
        }

        return instruction.TargetMonkeyObjectId;
    }

    private void SynchronizeTargetMonkeyObjectId(ScriptInstructionInstance instruction)
    {
        if (!RequiresMonkeyObjectTarget(instruction))
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

    private static bool RequiresMonkeyObjectTarget(ScriptInstructionInstance instruction)
    {
        return instruction.Type is ScriptCommandType.UpgradeMonkey
            or ScriptCommandType.SwitchMonkeyTarget
            or ScriptCommandType.SetMonkeyAbility
            or ScriptCommandType.SellMonkey
            or ScriptCommandType.ModifyMonkeyCoordinate;
    }

    private void DeleteSelectedSequenceInstructions(IList? selectedItems)
    {
        var selectedInstructions = selectedItems?.OfType<ScriptInstructionInstance>().Distinct().ToList() ?? [];
        if (selectedInstructions.Count == 0)
        {
            return;
        }

        ExecuteTrackedSequenceMutation(() =>
        {
            foreach (var instruction in selectedInstructions)
            {
                InstructionSequence.Remove(instruction);
            }

            SelectedSequenceInstruction = InstructionSequence.FirstOrDefault();
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
        var instruction = new ScriptInstructionInstance(template.Type, template.NameKey, template.DescriptionKey)
        {
            DisplayName = template.DisplayName,
            Description = template.Description
        };

        if (RequiresMonkeyObjectTarget(instruction))
        {
            instruction.TargetMonkeyBindingId = MonkeyObjectOptions.FirstOrDefault()?.Code ?? string.Empty;
            instruction.TargetMonkeyObjectId = ResolveTargetMonkeyObjectKey(instruction);
        }

        if (instruction.Type == ScriptCommandType.PlaceHeroInventory)
        {
            instruction.SelectedInventoryItem = InventoryOptions.FirstOrDefault()?.Code ?? string.Empty;
        }

        if (instruction.Type == ScriptCommandType.ActivateAbility)
        {
            instruction.SelectedActivatedAbility = ActivatedAbilityOptions.FirstOrDefault()?.Code ?? string.Empty;
        }

        if (instruction.Type == ScriptCommandType.NextRound)
        {
            instruction.NextRoundAction = "PlayFastForward";
            instruction.NextRoundSendCount = 1;
        }

        if (instruction.Type == ScriptCommandType.Wait)
        {
            instruction.WaitMode = WaitModeType.Time.ToString();
            instruction.WaitTimeMilliseconds = 1000;
            instruction.WaitGoldAmount = 0;
            instruction.WaitRoundCount = 1;
            instruction.WaitColorHex = "#FFFFFF";
            instruction.WaitColorTolerance = 0;
        }

        UpdateInstructionDisplayName(instruction);
        return instruction;
    }

    private List<ScriptMonkeyObjectDocument> BuildMonkeyObjectDocuments()
    {
        var documents = new List<ScriptMonkeyObjectDocument>();
        var placementOrder = 0;

        foreach (var instruction in InstructionSequence.Where(x => x.Type == ScriptCommandType.PlaceMonkey))
        {
            placementOrder++;
            documents.Add(new ScriptMonkeyObjectDocument
            {
                BindingId = instruction.MonkeyBindingId,
                ObjectId = instruction.MonkeyObjectId,
                SelectionCode = NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower),
                PlacementOrder = placementOrder
            });
        }

        return documents;
    }

    private List<ScriptInstructionDocument> BuildInstructionDocuments()
    {
        return InstructionSequence.Select(instruction => new ScriptInstructionDocument
        {
            CommandType = instruction.Type.ToString(),
            SelectedMonkeyTower = NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower),
            MonkeyBindingId = instruction.MonkeyBindingId,
            MonkeyObjectId = instruction.MonkeyObjectId,
            TargetMonkeyBindingId = instruction.TargetMonkeyBindingId,
            TargetMonkeyObjectId = instruction.TargetMonkeyObjectId,
            SelectedInventoryItem = instruction.SelectedInventoryItem,
            SelectedActivatedAbility = instruction.SelectedActivatedAbility,
            NextRoundAction = instruction.NextRoundAction,
            WaitMode = instruction.WaitMode,
            NextRoundSendCount = instruction.NextRoundSendCount,
            WaitTimeMilliseconds = instruction.WaitTimeMilliseconds,
            WaitGoldAmount = instruction.WaitGoldAmount,
            WaitRoundCount = instruction.WaitRoundCount,
            PositionX = instruction.PositionX,
            PositionY = instruction.PositionY,
            WaitColorCoordinateX = instruction.WaitColorCoordinateX,
            WaitColorCoordinateY = instruction.WaitColorCoordinateY,
            UpgradePath = instruction.UpgradePath.ToString(),
            UpgradeCount = instruction.UpgradeCount,
            SwitchDirection = instruction.SwitchDirection.ToString(),
            SwitchCount = instruction.SwitchCount,
            SelectedAbility = instruction.SelectedAbility.ToString(),
            RequiresAbilityCoordinate = instruction.RequiresAbilityCoordinate,
            AbilityCoordinateX = instruction.AbilityCoordinateX,
            AbilityCoordinateY = instruction.AbilityCoordinateY,
            WaitColorHex = instruction.WaitColorHex,
            WaitColorTolerance = instruction.WaitColorTolerance,
            CommentContent = instruction.CommentContent,
            IntervalToNextInstructionMs = instruction.IntervalToNextInstructionMs,
            Notes = instruction.Notes
        }).ToList();
    }

    private void ApplyScriptMetadata(ScriptMetadataDocument? metadata)
    {
        metadata ??= new ScriptMetadataDocument();

        ScriptVersion = NormalizeScriptVersion(metadata.ScriptVersion);
        ScriptCategory = ScriptDocumentCategories.Normalize(metadata.Category);
        ScriptName = metadata.Name;
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
    }

    private ScriptInstructionInstance CreateInstructionInstanceFromDocument(
        ScriptInstructionDocument document,
        IReadOnlyDictionary<string, ScriptMonkeyObjectDocument> monkeyObjectsByBindingId)
    {
        if (!Enum.TryParse<ScriptCommandType>(document.CommandType, true, out var commandType))
        {
            throw new InvalidDataException($"Unsupported script command type '{document.CommandType}'.");
        }

        var template = InstructionLibrary.FirstOrDefault(x => x.Type == commandType)
                       ?? throw new InvalidDataException($"Missing instruction template for '{commandType}'.");
        var instruction = CreateInstructionInstance(template);
        var placeMonkeySnapshot = ResolveMonkeyObjectSnapshot(document.MonkeyBindingId, monkeyObjectsByBindingId);

        instruction.SelectedMonkeyTower = ResolveDocumentPlaceSelectionCode(document, placeMonkeySnapshot);
        instruction.MonkeyBindingId = document.MonkeyBindingId;
        instruction.MonkeyObjectId = ResolveDocumentMonkeyObjectId(document, placeMonkeySnapshot);
        instruction.TargetMonkeyBindingId = document.TargetMonkeyBindingId;
        instruction.TargetMonkeyObjectId = document.TargetMonkeyObjectId;
        if (!string.IsNullOrWhiteSpace(document.SelectedInventoryItem))
        {
            instruction.SelectedInventoryItem = document.SelectedInventoryItem;
        }

        if (!string.IsNullOrWhiteSpace(document.SelectedActivatedAbility))
        {
            instruction.SelectedActivatedAbility = document.SelectedActivatedAbility;
        }

        instruction.NextRoundAction = string.IsNullOrWhiteSpace(document.NextRoundAction)
            ? instruction.NextRoundAction
            : document.NextRoundAction;
        instruction.WaitMode = string.IsNullOrWhiteSpace(document.WaitMode)
            ? instruction.WaitMode
            : document.WaitMode;
        instruction.NextRoundSendCount = document.NextRoundSendCount <= 0 ? instruction.NextRoundSendCount : document.NextRoundSendCount;
        instruction.WaitTimeMilliseconds = document.WaitTimeMilliseconds < 0 ? instruction.WaitTimeMilliseconds : document.WaitTimeMilliseconds;
        instruction.WaitGoldAmount = document.WaitGoldAmount;
        instruction.WaitRoundCount = document.WaitRoundCount <= 0 ? instruction.WaitRoundCount : document.WaitRoundCount;
        instruction.PositionX = document.PositionX;
        instruction.PositionY = document.PositionY;
        instruction.WaitColorCoordinateX = document.WaitColorCoordinateX;
        instruction.WaitColorCoordinateY = document.WaitColorCoordinateY;
        instruction.UpgradePath = Enum.TryParse<UpgradePathType>(document.UpgradePath, true, out var upgradePath)
            ? upgradePath
            : instruction.UpgradePath;
        instruction.UpgradeCount = document.UpgradeCount <= 0 ? instruction.UpgradeCount : document.UpgradeCount;
        instruction.SwitchDirection = Enum.TryParse<SwitchDirectionType>(document.SwitchDirection, true, out var switchDirection)
            ? switchDirection
            : instruction.SwitchDirection;
        instruction.SwitchCount = document.SwitchCount <= 0 ? instruction.SwitchCount : document.SwitchCount;
        instruction.SelectedAbility = Enum.TryParse<MonkeyAbilityType>(document.SelectedAbility, true, out var ability)
            ? ability
            : instruction.SelectedAbility;
        instruction.RequiresAbilityCoordinate = document.RequiresAbilityCoordinate;
        instruction.AbilityCoordinateX = document.AbilityCoordinateX;
        instruction.AbilityCoordinateY = document.AbilityCoordinateY;
        instruction.WaitColorHex = string.IsNullOrWhiteSpace(document.WaitColorHex) ? instruction.WaitColorHex : document.WaitColorHex;
        instruction.WaitColorTolerance = document.WaitColorTolerance;
        instruction.CommentContent = document.CommentContent;
        instruction.IntervalToNextInstructionMs = document.IntervalToNextInstructionMs;
        instruction.Notes = document.Notes;

        UpdateInstructionDisplayName(instruction);
        return instruction;
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

    private static ScriptMonkeyObjectDocument? ResolveMonkeyObjectSnapshot(
        string bindingId,
        IReadOnlyDictionary<string, ScriptMonkeyObjectDocument> monkeyObjectsByBindingId)
    {
        return string.IsNullOrWhiteSpace(bindingId)
            ? null
            : monkeyObjectsByBindingId.GetValueOrDefault(bindingId);
    }

    private static string ResolveDocumentPlaceSelectionCode(
        ScriptInstructionDocument document,
        ScriptMonkeyObjectDocument? monkeyObject)
    {
        var selectionCode = string.IsNullOrWhiteSpace(document.SelectedMonkeyTower)
            ? monkeyObject?.SelectionCode
            : document.SelectedMonkeyTower;
        return NormalizePlaceSelectionCode(selectionCode);
    }

    private static string ResolveDocumentMonkeyObjectId(
        ScriptInstructionDocument document,
        ScriptMonkeyObjectDocument? monkeyObject)
    {
        if (!string.IsNullOrWhiteSpace(document.MonkeyObjectId))
        {
            return document.MonkeyObjectId;
        }

        return monkeyObject?.ObjectId ?? string.Empty;
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
            _clipboardSequenceInstructions.Add(CloneInstructionInstance(instruction));
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
            var pastedInstructions = CloneInstructionsForPaste(_clipboardSequenceInstructions);
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
        _undoHistory.Push(CloneSnapshot(_sequenceSnapshot));
        _redoHistory.Clear();
    }

    private static List<ScriptInstructionInstance> CloneSnapshot(IEnumerable<ScriptInstructionInstance> source)
    {
        return source.Select(CloneInstructionInstance).ToList();
    }

    private List<ScriptInstructionInstance> CaptureSequenceSnapshot()
    {
        return InstructionSequence.Select(CloneInstructionInstance).ToList();
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
            foreach (var instruction in snapshot.Select(CloneInstructionInstance))
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

    private static ScriptInstructionInstance CloneInstructionInstance(ScriptInstructionInstance source)
    {
        return new ScriptInstructionInstance(source.Type, source.NameKey, source.DescriptionKey)
        {
            DisplayName = source.DisplayName,
            Description = source.Description,
            SelectedMonkeyTower = NormalizePlaceSelectionCode(source.SelectedMonkeyTower),
            MonkeyBindingId = source.MonkeyBindingId,
            MonkeyObjectId = source.MonkeyObjectId,
            TargetMonkeyBindingId = source.TargetMonkeyBindingId,
            TargetMonkeyObjectId = source.TargetMonkeyObjectId,
            SelectedInventoryItem = source.SelectedInventoryItem,
            SelectedActivatedAbility = source.SelectedActivatedAbility,
            NextRoundAction = source.NextRoundAction,
            NextRoundSendCount = source.NextRoundSendCount,
            WaitMode = source.WaitMode,
            WaitTimeMilliseconds = source.WaitTimeMilliseconds,
            WaitGoldAmount = source.WaitGoldAmount,
            WaitRoundCount = source.WaitRoundCount,
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            WaitColorCoordinateX = source.WaitColorCoordinateX,
            WaitColorCoordinateY = source.WaitColorCoordinateY,
            UpgradePath = source.UpgradePath,
            UpgradeCount = source.UpgradeCount,
            SwitchDirection = source.SwitchDirection,
            SwitchCount = source.SwitchCount,
            SelectedAbility = source.SelectedAbility,
            RequiresAbilityCoordinate = source.RequiresAbilityCoordinate,
            AbilityCoordinateX = source.AbilityCoordinateX,
            AbilityCoordinateY = source.AbilityCoordinateY,
            WaitColorHex = source.WaitColorHex,
            WaitColorTolerance = source.WaitColorTolerance,
            CommentContent = source.CommentContent,
            IntervalToNextInstructionMs = source.IntervalToNextInstructionMs,
            Notes = source.Notes
        };
    }

    private static List<ScriptInstructionInstance> CloneInstructionsForPaste(IEnumerable<ScriptInstructionInstance> source)
    {
        var clonedInstructions = source.Select(CloneInstructionInstance).ToList();
        var pastedBindingIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instruction in clonedInstructions.Where(x => x.Type == ScriptCommandType.PlaceMonkey))
        {
            var originalBindingId = instruction.MonkeyBindingId;
            var newBindingId = CreateMonkeyBindingId();

            if (!string.IsNullOrWhiteSpace(originalBindingId) &&
                !pastedBindingIdMap.ContainsKey(originalBindingId))
            {
                pastedBindingIdMap[originalBindingId] = newBindingId;
            }

            instruction.MonkeyBindingId = newBindingId;
        }

        foreach (var instruction in clonedInstructions.Where(RequiresMonkeyObjectTarget))
        {
            if (!string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId) &&
                pastedBindingIdMap.TryGetValue(instruction.TargetMonkeyBindingId, out var remappedBindingId))
            {
                instruction.TargetMonkeyBindingId = remappedBindingId;
            }
        }

        return clonedInstructions;
    }

    private void BuildInstructionLibrary()
    {
        InstructionLibrary.Clear();
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.PlaceMonkey,
            NameKey = "Editor.Command.PlaceMonkey.Title",
            DescriptionKey = "Editor.Command.PlaceMonkey.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.UpgradeMonkey,
            NameKey = "Editor.Command.UpgradeMonkey.Title",
            DescriptionKey = "Editor.Command.UpgradeMonkey.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.SwitchMonkeyTarget,
            NameKey = "Editor.Command.SwitchMonkeyTarget.Title",
            DescriptionKey = "Editor.Command.SwitchMonkeyTarget.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.SetMonkeyAbility,
            NameKey = "Editor.Command.SetMonkeyAbility.Title",
            DescriptionKey = "Editor.Command.SetMonkeyAbility.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.SellMonkey,
            NameKey = "Editor.Command.SellMonkey.Title",
            DescriptionKey = "Editor.Command.SellMonkey.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.PlaceHeroInventory,
            NameKey = "Editor.Command.PlaceHeroInventory.Title",
            DescriptionKey = "Editor.Command.PlaceHeroInventory.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.ActivateAbility,
            NameKey = "Editor.Command.ActivateAbility.Title",
            DescriptionKey = "Editor.Command.ActivateAbility.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.NextRound,
            NameKey = "Editor.Command.NextRound.Title",
            DescriptionKey = "Editor.Command.NextRound.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.Wait,
            NameKey = "Editor.Command.Wait.Title",
            DescriptionKey = "Editor.Command.Wait.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.ModifyMonkeyCoordinate,
            NameKey = "Editor.Command.ModifyMonkeyCoordinate.Title",
            DescriptionKey = "Editor.Command.ModifyMonkeyCoordinate.Description"
        });
        InstructionLibrary.Add(new ScriptInstructionTemplate
        {
            Type = ScriptCommandType.Comment,
            NameKey = "Editor.Command.Comment.Title",
            DescriptionKey = "Editor.Command.Comment.Description"
        });
    }

    private void BuildScriptParameterOptions()
    {
        UpgradePathOptions.Clear();
        UpgradePathOptions.Add(new LanguageOption { Code = UpgradePathType.Top.ToString(), DisplayName = _localizationService.T("Editor.Property.UpgradePath.Top") });
        UpgradePathOptions.Add(new LanguageOption { Code = UpgradePathType.Middle.ToString(), DisplayName = _localizationService.T("Editor.Property.UpgradePath.Middle") });
        UpgradePathOptions.Add(new LanguageOption { Code = UpgradePathType.Bottom.ToString(), DisplayName = _localizationService.T("Editor.Property.UpgradePath.Bottom") });

        SwitchDirectionOptions.Clear();
        SwitchDirectionOptions.Add(new LanguageOption { Code = SwitchDirectionType.Right.ToString(), DisplayName = _localizationService.T("Editor.Property.SwitchDirection.Right") });
        SwitchDirectionOptions.Add(new LanguageOption { Code = SwitchDirectionType.Left.ToString(), DisplayName = _localizationService.T("Editor.Property.SwitchDirection.Left") });

        MonkeyAbilityOptions.Clear();
        MonkeyAbilityOptions.Add(new LanguageOption { Code = MonkeyAbilityType.Ability1.ToString(), DisplayName = _localizationService.T("Editor.Property.Ability1") });
        MonkeyAbilityOptions.Add(new LanguageOption { Code = MonkeyAbilityType.Ability2.ToString(), DisplayName = _localizationService.T("Editor.Property.Ability2") });

        InventoryOptions.Clear();
        foreach (var inventory in GameElementCatalog.InventoryItems)
        {
            InventoryOptions.Add(new LanguageOption { Code = inventory.Type.ToString(), DisplayName = _localizationService.T(inventory.NameKey) });
        }

        ActivatedAbilityOptions.Clear();
        foreach (var ability in GameElementCatalog.ActivatedAbilities)
        {
            ActivatedAbilityOptions.Add(new LanguageOption { Code = ability.Type.ToString(), DisplayName = _localizationService.T(ability.NameKey) });
        }

        NextRoundActionOptions.Clear();
        foreach (var action in GameElementCatalog.NextRoundActions)
        {
            NextRoundActionOptions.Add(new LanguageOption
            {
                Code = action,
                DisplayName = GameElementCatalog.GetNextRoundActionDisplayName(action)
            });
        }

        WaitModeOptions.Clear();
        WaitModeOptions.Add(new LanguageOption { Code = WaitModeType.Time.ToString(), DisplayName = _localizationService.T("Editor.Property.WaitMode.Time") });
        WaitModeOptions.Add(new LanguageOption { Code = WaitModeType.Gold.ToString(), DisplayName = _localizationService.T("Editor.Property.WaitMode.Gold") });
        WaitModeOptions.Add(new LanguageOption { Code = WaitModeType.Round.ToString(), DisplayName = _localizationService.T("Editor.Property.WaitMode.Round") });
        WaitModeOptions.Add(new LanguageOption { Code = WaitModeType.CoordinateColor.ToString(), DisplayName = _localizationService.T("Editor.Property.WaitMode.CoordinateColor") });
    }

    private void BuildMetadataOptions()
    {
        var selectedDifficultyCode = SelectedDifficultyOption?.Code ?? StageDifficulty.Medium.ToString();
        var selectedModeCode = SelectedModeOption?.Code ?? StageMode.Standard.ToString();
        var selectedHeroCode = SelectedHeroOption?.Code ?? HeroType.Quincy.ToString();

        DifficultyOptions.Clear();
        DifficultyOptions.Add(new LanguageOption { Code = StageDifficulty.Easy.ToString(), DisplayName = _localizationService.T("GameElements.StageDifficulty.Easy") });
        DifficultyOptions.Add(new LanguageOption { Code = StageDifficulty.Medium.ToString(), DisplayName = _localizationService.T("GameElements.StageDifficulty.Medium") });
        DifficultyOptions.Add(new LanguageOption { Code = StageDifficulty.Hard.ToString(), DisplayName = _localizationService.T("GameElements.StageDifficulty.Hard") });

        ModeOptions.Clear();
        foreach (var mode in new[]
                 {
                     StageMode.Standard,
                     StageMode.PrimaryOnly,
                     StageMode.Deflation,
                     StageMode.MilitaryOnly,
                     StageMode.Apopalypse,
                     StageMode.Reverse,
                     StageMode.MagicOnly,
                     StageMode.DoubleHpMoabs,
                     StageMode.HalfCash,
                     StageMode.AlternateBloonsRounds,
                     StageMode.Impoppable,
                     StageMode.CHIMPS
                 })
        {
            ModeOptions.Add(new LanguageOption
            {
                Code = mode.ToString(),
                DisplayName = _localizationService.T($"GameElements.StageMode.{mode}")
            });
        }

        HeroOptions.Clear();
        foreach (var hero in GameElementCatalog.Heroes)
        {
            HeroOptions.Add(new LanguageOption
            {
                Code = hero.Type.ToString(),
                DisplayName = _localizationService.T(hero.NameKey)
            });
        }

        SelectedDifficultyOption = DifficultyOptions.FirstOrDefault(x => x.Code == selectedDifficultyCode) ?? DifficultyOptions.FirstOrDefault();
        SelectedModeOption = ModeOptions.FirstOrDefault(x => x.Code == selectedModeCode) ?? ModeOptions.FirstOrDefault();
        SelectedHeroOption = HeroOptions.FirstOrDefault(x => x.Code == selectedHeroCode) ?? HeroOptions.FirstOrDefault();
    }

    private void UpdateInstructionLocalization()
    {
        foreach (var template in InstructionLibrary)
        {
            template.DisplayName = _localizationService.T(template.NameKey);
            template.Description = _localizationService.T(template.DescriptionKey);
        }

        foreach (var instance in InstructionSequence)
        {
            instance.Description = _localizationService.T(instance.DescriptionKey);
        }

        UpdateAllInstructionDisplayNames();
    }

    private void UpdateAllInstructionDisplayNames()
    {
        var upgradeLevelStates = BuildUpgradeLevelStates();
        foreach (var instruction in InstructionSequence)
        {
            UpdateInstructionDisplayName(instruction, upgradeLevelStates);
        }
    }

    private void UpdateInstructionDisplayName(ScriptInstructionInstance instruction)
    {
        UpdateInstructionDisplayName(instruction, BuildUpgradeLevelStates());
    }

    private void UpdateInstructionDisplayName(
        ScriptInstructionInstance instruction,
        IReadOnlyDictionary<ScriptInstructionInstance, string> upgradeLevelStates)
    {
        var text = instruction.Type switch
        {
            ScriptCommandType.PlaceMonkey => string.Format(
                _localizationService.T("Editor.Display.PlaceMonkey"),
                string.IsNullOrWhiteSpace(instruction.MonkeyObjectId)
                    ? GetPlaceSelectionDisplayName(instruction.SelectedMonkeyTower)
                    : GetMonkeyObjectDisplayName(instruction.MonkeyObjectId),
                FormatCoordinate(instruction.PositionX),
                FormatCoordinate(instruction.PositionY)),
            ScriptCommandType.UpgradeMonkey => IsHeroObjectKey(ResolveTargetMonkeyObjectKey(instruction))
                ? string.Format(
                    _localizationService.T("Editor.Display.UpgradeMonkey.Hero"),
                    GetTargetMonkeyDisplayName(instruction),
                    instruction.UpgradeCount)
                : string.Format(
                    _localizationService.T("Editor.Display.UpgradeMonkey"),
                    GetTargetMonkeyDisplayName(instruction),
                    upgradeLevelStates.GetValueOrDefault(instruction, "000")),
            ScriptCommandType.SwitchMonkeyTarget => string.Format(
                _localizationService.T("Editor.Display.SwitchMonkeyTarget"),
                GetTargetMonkeyDisplayName(instruction),
                GetSwitchDirectionDisplayName(instruction.SwitchDirection),
                instruction.SwitchCount),
            ScriptCommandType.SetMonkeyAbility => string.Format(
                _localizationService.T("Editor.Display.SetMonkeyAbility"),
                GetTargetMonkeyDisplayName(instruction),
                GetAbilityDisplayNumber(instruction.SelectedAbility),
                instruction.RequiresAbilityCoordinate
                    ? string.Format(
                        _localizationService.T("Editor.Display.SetMonkeyAbility.WithCoordinateSuffix"),
                        FormatCoordinate(instruction.AbilityCoordinateX),
                        FormatCoordinate(instruction.AbilityCoordinateY))
                    : string.Empty),
            ScriptCommandType.SellMonkey => string.Format(
                _localizationService.T("Editor.Display.SellMonkey"),
                GetTargetMonkeyDisplayName(instruction)),
            ScriptCommandType.PlaceHeroInventory => string.Format(
                _localizationService.T("Editor.Display.PlaceHeroInventory"),
                GetInventoryDisplayName(instruction.SelectedInventoryItem),
                instruction.RequiresAbilityCoordinate
                    ? string.Format(
                        _localizationService.T("Editor.Display.PlaceHeroInventory.WithCoordinateSuffix"),
                        FormatCoordinate(instruction.PositionX),
                        FormatCoordinate(instruction.PositionY))
                    : string.Empty),
            ScriptCommandType.ActivateAbility => string.Format(
                _localizationService.T("Editor.Display.ActivateAbility"),
                GetActivatedAbilityDisplayName(instruction.SelectedActivatedAbility),
                instruction.RequiresAbilityCoordinate
                    ? string.Format(
                        _localizationService.T("Editor.Display.ActivateAbility.WithCoordinateSuffix"),
                        FormatCoordinate(instruction.AbilityCoordinateX),
                        FormatCoordinate(instruction.AbilityCoordinateY))
                    : string.Empty),
            ScriptCommandType.NextRound => GetNextRoundActionDisplayName(instruction),
            ScriptCommandType.Wait => GetWaitDisplayName(instruction),
            ScriptCommandType.ModifyMonkeyCoordinate => string.Format(
                _localizationService.T("Editor.Display.ModifyMonkeyCoordinate"),
                GetTargetMonkeyDisplayName(instruction),
                FormatCoordinate(instruction.PositionX),
                FormatCoordinate(instruction.PositionY)),
            ScriptCommandType.Comment => string.Format(
                _localizationService.T("Editor.Display.Comment"),
                GetCommentPreview(instruction.CommentContent)),
            _ => _localizationService.T(instruction.NameKey)
        };

        instruction.DisplayName = text;
    }

    private bool ShouldRefreshAllInstructionDisplayNames(ScriptInstructionInstance instruction, string? propertyName)
    {
        if (instruction.Type != ScriptCommandType.UpgradeMonkey)
        {
            return false;
        }

        return string.Equals(propertyName, nameof(ScriptInstructionInstance.TargetMonkeyBindingId), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(ScriptInstructionInstance.UpgradePath), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(ScriptInstructionInstance.UpgradeCount), StringComparison.Ordinal);
    }

    private IReadOnlyDictionary<ScriptInstructionInstance, string> BuildUpgradeLevelStates()
    {
        var upgradeStates = new Dictionary<ScriptInstructionInstance, string>();
        var monkeyLevels = new Dictionary<string, (int Top, int Middle, int Bottom)>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < InstructionSequence.Count; index++)
        {
            var instruction = InstructionSequence[index];
            if (instruction.Type != ScriptCommandType.UpgradeMonkey)
            {
                continue;
            }

            var targetObjectKey = ResolveTargetMonkeyObjectKey(instruction);
            if (IsHeroObjectKey(targetObjectKey))
            {
                continue;
            }

            var trackingKey = ResolveUpgradeTrackingKey(instruction, targetObjectKey, index);
            monkeyLevels.TryGetValue(trackingKey, out var currentLevels);
            currentLevels = instruction.UpgradePath switch
            {
                UpgradePathType.Top => (currentLevels.Top + instruction.UpgradeCount, currentLevels.Middle, currentLevels.Bottom),
                UpgradePathType.Middle => (currentLevels.Top, currentLevels.Middle + instruction.UpgradeCount, currentLevels.Bottom),
                UpgradePathType.Bottom => (currentLevels.Top, currentLevels.Middle, currentLevels.Bottom + instruction.UpgradeCount),
                _ => currentLevels
            };

            monkeyLevels[trackingKey] = currentLevels;
            upgradeStates[instruction] = $"{currentLevels.Top}{currentLevels.Middle}{currentLevels.Bottom}";
        }

        return upgradeStates;
    }

    private static string ResolveUpgradeTrackingKey(
        ScriptInstructionInstance instruction,
        string targetObjectKey,
        int sequenceIndex)
    {
        if (!string.IsNullOrWhiteSpace(targetObjectKey))
        {
            return $"Object:{targetObjectKey}";
        }

        if (!string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            return $"Binding:{instruction.TargetMonkeyBindingId}";
        }

        if (!string.IsNullOrWhiteSpace(instruction.TargetMonkeyObjectId))
        {
            return $"Target:{instruction.TargetMonkeyObjectId}";
        }

        return $"Instruction:{sequenceIndex}";
    }

    private static bool IsHeroObjectKey(string objectKey)
    {
        return objectKey.StartsWith("Hero:", StringComparison.OrdinalIgnoreCase);
    }

    private string GetTowerDisplayName(MonkeyTowerType towerType)
    {
        var tower = GameElementCatalog.MonkeyTowers.FirstOrDefault(x => x.Type == towerType);
        return tower is null ? towerType.ToString() : _localizationService.T(tower.NameKey);
    }

    private string GetPlaceSelectionDisplayName(string selectionCode)
    {
        var normalized = NormalizePlaceSelectionCode(selectionCode);
        if (TryParseTowerSelection(normalized, out var towerType))
        {
            return GetTowerDisplayName(towerType);
        }

        if (TryParseHeroSelection(normalized, out var heroType))
        {
            return BuildHeroObjectDisplayName(heroType);
        }

        return GetTowerDisplayName(MonkeyTowerType.DartMonkey);
    }

    private string GetMonkeyObjectDisplayName(string objectKey)
    {
        return string.IsNullOrWhiteSpace(objectKey)
            ? _localizationService.T("Editor.Property.TargetMonkey")
            : BuildMonkeyObjectDisplayName(objectKey);
    }

    private string GetTargetMonkeyDisplayName(ScriptInstructionInstance instruction)
    {
        return GetMonkeyObjectDisplayName(ResolveTargetMonkeyObjectKey(instruction));
    }

    private string GetUpgradePathDisplayName(UpgradePathType path)
    {
        return path switch
        {
            UpgradePathType.Top => _localizationService.T("Editor.Display.Path.Top"),
            UpgradePathType.Middle => _localizationService.T("Editor.Display.Path.Middle"),
            UpgradePathType.Bottom => _localizationService.T("Editor.Display.Path.Bottom"),
            _ => _localizationService.T("Editor.Display.Path.Top")
        };
    }

    private string GetSwitchDirectionDisplayName(SwitchDirectionType direction)
    {
        return direction switch
        {
            SwitchDirectionType.Right => _localizationService.T("Editor.Display.Direction.Right"),
            SwitchDirectionType.Left => _localizationService.T("Editor.Display.Direction.Left"),
            _ => _localizationService.T("Editor.Display.Direction.Right")
        };
    }

    private static int GetAbilityDisplayNumber(MonkeyAbilityType ability)
    {
        return ability == MonkeyAbilityType.Ability2 ? 2 : 1;
    }

    private string GetInventoryDisplayName(string inventoryCode)
    {
        return Enum.TryParse<InventoryType>(inventoryCode, out var inventoryType)
            ? GameElementCatalog.GetInventoryDisplayName(inventoryType)
            : _localizationService.T("Editor.Property.Inventory");
    }

    private string GetActivatedAbilityDisplayName(string activatedAbilityCode)
    {
        return Enum.TryParse<ActivatedAbilityType>(activatedAbilityCode, out var abilityType)
            ? GameElementCatalog.GetActivatedAbilityDisplayName(abilityType)
            : _localizationService.T("Editor.Property.ActivatedAbility");
    }

    private string GetNextRoundActionDisplayName(ScriptInstructionInstance instruction)
    {
        return instruction.NextRoundAction switch
        {
            "SendNextRound" => string.Format(
                _localizationService.T("Editor.Display.NextRound.SendNextRound"),
                instruction.NextRoundSendCount),
            _ => _localizationService.T("Editor.Display.NextRound.PlayFastForward")
        };
    }

    private string GetWaitDisplayName(ScriptInstructionInstance instruction)
    {
        return instruction.WaitMode switch
        {
            nameof(WaitModeType.Gold) => string.Format(
                _localizationService.T("Editor.Display.Wait.Gold"),
                instruction.WaitGoldAmount),
            nameof(WaitModeType.Round) => string.Format(
                _localizationService.T("Editor.Display.Wait.Round"),
                instruction.WaitRoundCount),
            nameof(WaitModeType.CoordinateColor) => string.Format(
                _localizationService.T("Editor.Display.Wait.CoordinateColor"),
                FormatCoordinate(instruction.WaitColorCoordinateX),
                FormatCoordinate(instruction.WaitColorCoordinateY),
                FormatWaitColorHex(instruction.WaitColorHex),
                instruction.WaitColorTolerance),
            _ => string.Format(
                _localizationService.T("Editor.Display.Wait.Time"),
                instruction.WaitTimeMilliseconds)
        };
    }

    private static string FormatCoordinate(double value)
    {
        return value.ToString("0.##");
    }

    private static string FormatWaitColorHex(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim().ToUpperInvariant();
    }

    private string GetCommentPreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return _localizationService.T("Editor.Display.Comment.Empty");
        }

        return content
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();
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
        OnPropertyChanged(nameof(MetadataMapPlaceholderText));
        OnPropertyChanged(nameof(ScriptCategoryAllText));
        OnPropertyChanged(nameof(ScriptCategoryCollectionText));
        OnPropertyChanged(nameof(ScriptCategoryBlackBorderText));
        OnPropertyChanged(nameof(ScriptCategoryRaceText));
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
        OnPropertyChanged(nameof(PropertyWaitModeText));
        OnPropertyChanged(nameof(PropertyWaitTimeMillisecondsText));
        OnPropertyChanged(nameof(PropertyWaitGoldAmountText));
        OnPropertyChanged(nameof(PropertyWaitRoundCountText));
        OnPropertyChanged(nameof(PropertyWaitColorHexText));
        OnPropertyChanged(nameof(PropertyWaitColorToleranceText));
        OnPropertyChanged(nameof(PropertyCommentContentText));
        OnPropertyChanged(nameof(PropertyAdvancedText));
        OnPropertyChanged(nameof(PropertyIntervalToNextInstructionText));
        OnPropertyChanged(nameof(NonExecutableInstructionHintText));
        OnPropertyChanged(nameof(PropertyNotesText));
        OnPropertyChanged(nameof(DeleteSelectedInstructionText));
        OnPropertyChanged(nameof(CopySelectedInstructionText));
        OnPropertyChanged(nameof(PasteInstructionText));
    }
}
