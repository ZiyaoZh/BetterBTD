using System.Collections.ObjectModel;
using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BetterBTD.ViewModels;

public sealed class MyScriptsPageViewModel : ObservableObject
{
    private readonly LocalizationService _localizationService;
    private readonly AppDialogService _appDialogService;
    private readonly ManagedScriptLibraryService _managedScriptLibraryService;

    private List<ManagedScriptListItemViewModel> _allScripts = [];
    private List<ManagedScriptSlotListItemViewModel> _allSlots = [];
    private string _scriptSearchText = string.Empty;
    private string _slotSearchText = string.Empty;
    private LanguageOption? _selectedTaskKindOption;
    private LanguageOption? _selectedMapOption;
    private LanguageOption? _selectedDifficultyOption;
    private LanguageOption? _selectedModeOption;
    private ManagedScriptListItemViewModel? _selectedScript;
    private ManagedScriptSlotListItemViewModel? _selectedSlot;

    public MyScriptsPageViewModel(LocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _appDialogService = AppDialogService.Instance;
        _managedScriptLibraryService = ManagedScriptLibraryService.Instance;

        RefreshCommand = new RelayCommand(Refresh);
        ImportScriptCommand = new RelayCommand(ImportScript);
        ExportSelectedScriptCommand = new RelayCommand(ExportSelectedScript, CanExportSelectedScript);
        RemoveSelectedScriptCommand = new RelayCommand(RemoveSelectedScript, CanRemoveSelectedScript);
        BindSelectedScriptToSlotCommand = new RelayCommand(BindSelectedScriptToSlot, CanBindSelectedScriptToSlot);
        ClearSelectedSlotBindingCommand = new RelayCommand(ClearSelectedSlotBinding, CanClearSelectedSlotBinding);

        _localizationService.LanguageChanged += (_, _) => RefreshLocalizedContent();

        RefreshLocalizedContent();
        Refresh();
    }

    public ObservableCollection<LanguageOption> TaskKindOptions { get; } = [];

    public ObservableCollection<LanguageOption> MapOptions { get; } = [];

    public ObservableCollection<LanguageOption> DifficultyOptions { get; } = [];

    public ObservableCollection<LanguageOption> ModeOptions { get; } = [];

    public ObservableCollection<ManagedScriptListItemViewModel> Scripts { get; } = [];

    public ObservableCollection<ManagedScriptSlotListItemViewModel> Slots { get; } = [];

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand ImportScriptCommand { get; }

    public IRelayCommand ExportSelectedScriptCommand { get; }

    public IRelayCommand RemoveSelectedScriptCommand { get; }

    public IRelayCommand BindSelectedScriptToSlotCommand { get; }

    public IRelayCommand ClearSelectedSlotBindingCommand { get; }

    public string Title => _localizationService.T("Library.Page.Title");

    public string Subtitle => _localizationService.T("Library.Page.Subtitle");

    public string ImportText => _localizationService.T("Library.Action.Import");

    public string ExportText => _localizationService.T("Library.Action.Export");

    public string RemoveText => _localizationService.T("Library.Action.Remove");

    public string RefreshText => _localizationService.T("Library.Action.Refresh");

    public string BindText => _localizationService.T("Library.Action.Bind");

    public string ClearBindingText => _localizationService.T("Library.Action.ClearBinding");

    public string FiltersTitle => _localizationService.T("Library.Filters.Title");

    public string ScriptSearchLabel => _localizationService.T("Library.Filters.Search");

    public string ScriptSearchPlaceholder => _localizationService.T("Library.Filters.Search.Placeholder");

    public string MapFilterLabel => _localizationService.T("Library.Filters.Map");

    public string DifficultyFilterLabel => _localizationService.T("Library.Filters.Difficulty");

    public string ModeFilterLabel => _localizationService.T("Library.Filters.Mode");

    public string TaskKindFilterLabel => _localizationService.T("Library.Filters.TaskKind");

    public string SlotSearchLabel => _localizationService.T("Library.Filters.SlotSearch");

    public string ScriptsSectionTitle => _localizationService.T("Library.Section.Scripts");

    public string SlotsSectionTitle => _localizationService.T("Library.Section.Slots");

    public string EmptyScriptsText => _localizationService.T("Library.Empty.Scripts");

    public string EmptySlotsText => _localizationService.T("Library.Empty.Slots");

    public string NameColumnText => _localizationService.T("Library.Column.Name");

    public string MapColumnText => _localizationService.T("Library.Column.Map");

    public string DifficultyColumnText => _localizationService.T("Library.Column.Difficulty");

    public string ModeColumnText => _localizationService.T("Library.Column.Mode");

    public string TagsColumnText => _localizationService.T("Library.Column.Tags");

    public string BindingsColumnText => _localizationService.T("Library.Column.Bindings");

    public string StateColumnText => _localizationService.T("Library.Column.State");

    public string GroupColumnText => _localizationService.T("Library.Column.Group");

    public string SlotColumnText => _localizationService.T("Library.Column.Slot");

    public string BoundScriptColumnText => _localizationService.T("Library.Column.BoundScript");

    public string SelectedScriptSummary => SelectedScript is null
        ? _localizationService.T("Library.Summary.None")
        : string.Format(
            _localizationService.T("Library.Summary.Script"),
            SelectedScript.SourceFileName,
            SelectedScript.ScriptId);

    public string ScriptSearchText
    {
        get => _scriptSearchText;
        set
        {
            if (!SetProperty(ref _scriptSearchText, value))
            {
                return;
            }

            RefreshFilteredCollections();
        }
    }

    public string SlotSearchText
    {
        get => _slotSearchText;
        set
        {
            if (!SetProperty(ref _slotSearchText, value))
            {
                return;
            }

            RefreshFilteredCollections();
        }
    }

    public LanguageOption? SelectedTaskKindOption
    {
        get => _selectedTaskKindOption;
        set
        {
            if (!SetProperty(ref _selectedTaskKindOption, value))
            {
                return;
            }

            RefreshFilteredCollections();
        }
    }

    public LanguageOption? SelectedMapOption
    {
        get => _selectedMapOption;
        set
        {
            if (!SetProperty(ref _selectedMapOption, value))
            {
                return;
            }

            RefreshFilteredCollections();
        }
    }

    public LanguageOption? SelectedDifficultyOption
    {
        get => _selectedDifficultyOption;
        set
        {
            if (!SetProperty(ref _selectedDifficultyOption, value))
            {
                return;
            }

            RefreshFilteredCollections();
        }
    }

    public LanguageOption? SelectedModeOption
    {
        get => _selectedModeOption;
        set
        {
            if (!SetProperty(ref _selectedModeOption, value))
            {
                return;
            }

            RefreshFilteredCollections();
        }
    }

    public ManagedScriptListItemViewModel? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (!SetProperty(ref _selectedScript, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedScriptSummary));
            ExportSelectedScriptCommand.NotifyCanExecuteChanged();
            RemoveSelectedScriptCommand.NotifyCanExecuteChanged();
            BindSelectedScriptToSlotCommand.NotifyCanExecuteChanged();
        }
    }

    public ManagedScriptSlotListItemViewModel? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (!SetProperty(ref _selectedSlot, value))
            {
                return;
            }

            BindSelectedScriptToSlotCommand.NotifyCanExecuteChanged();
            ClearSelectedSlotBindingCommand.NotifyCanExecuteChanged();
        }
    }

    private void Refresh()
    {
        var snapshot = _managedScriptLibraryService.GetSnapshot();
        _allScripts = snapshot.Scripts.Select(CreateScriptItem).ToList();
        _allSlots = snapshot.Slots.Select(CreateSlotItem).ToList();
        BuildFilterOptions();
        RefreshFilteredCollections();
    }

    private void ImportScript()
    {
        var dialog = new OpenFileDialog
        {
            Filter = _localizationService.T("Library.File.ImportFilter"),
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _managedScriptLibraryService.ImportScript(dialog.FileName);
            Refresh();
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.ImportError.Title", ex.Message);
        }
    }

    private bool CanExportSelectedScript()
    {
        return SelectedScript is not null;
    }

    private void ExportSelectedScript()
    {
        if (SelectedScript is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = _localizationService.T("Library.File.ExportFilter"),
            FileName = $"{SelectedScript.DisplayName}.btd"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _managedScriptLibraryService.ExportScript(SelectedScript.ScriptId, dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.ExportError.Title", ex.Message);
        }
    }

    private bool CanRemoveSelectedScript()
    {
        return SelectedScript is not null;
    }

    private void RemoveSelectedScript()
    {
        if (SelectedScript is null)
        {
            return;
        }

        var result = _appDialogService.Show(new AppDialogRequest
        {
            Title = _localizationService.T("Library.Dialog.Remove.Title"),
            Message = _localizationService.T("Library.Dialog.Remove.Message"),
            PrimaryButtonText = _localizationService.T("Library.Dialog.Primary"),
            SecondaryButtonText = _localizationService.T("Library.Dialog.Cancel")
        });

        if (result != AppDialogResult.Primary)
        {
            return;
        }

        try
        {
            _managedScriptLibraryService.RemoveScript(SelectedScript.ScriptId);
            Refresh();
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.RemoveError.Title", ex.Message);
        }
    }

    private bool CanBindSelectedScriptToSlot()
    {
        return SelectedScript is not null && SelectedSlot is not null;
    }

    private void BindSelectedScriptToSlot()
    {
        if (SelectedScript is null || SelectedSlot is null)
        {
            return;
        }

        try
        {
            _managedScriptLibraryService.SetBinding(SelectedSlot.SlotId, SelectedScript.ScriptId);
            Refresh();
            RestoreSelection(SelectedScript.ScriptId, SelectedSlot.SlotId);
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.BindingError.Title", ex.Message);
        }
    }

    private bool CanClearSelectedSlotBinding()
    {
        return SelectedSlot?.HasBinding == true;
    }

    private void ClearSelectedSlotBinding()
    {
        if (SelectedSlot is null)
        {
            return;
        }

        try
        {
            _managedScriptLibraryService.SetBinding(SelectedSlot.SlotId, null);
            Refresh();
            RestoreSelection(SelectedScript?.ScriptId, SelectedSlot.SlotId);
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.BindingError.Title", ex.Message);
        }
    }

    private void RefreshFilteredCollections()
    {
        var selectedScriptId = SelectedScript?.ScriptId;
        var selectedSlotId = SelectedSlot?.SlotId;

        var filteredScripts = _allScripts
            .Where(MatchesScriptFilters)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filteredSlots = _allSlots
            .Where(MatchesSlotFilters)
            .OrderBy(x => x.TaskKind)
            .ThenBy(x => x.GroupDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SlotDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceCollection(Scripts, filteredScripts);
        ReplaceCollection(Slots, filteredSlots);

        SelectedScript = Scripts.FirstOrDefault(x => x.ScriptId == selectedScriptId) ?? Scripts.FirstOrDefault();
        SelectedSlot = Slots.FirstOrDefault(x => x.SlotId == selectedSlotId) ?? Slots.FirstOrDefault();
    }

    private bool MatchesScriptFilters(ManagedScriptListItemViewModel script)
    {
        if (SelectedMapOption?.Code.Length > 0 &&
            !string.Equals(script.MapCode, SelectedMapOption.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (SelectedDifficultyOption?.Code.Length > 0 &&
            !string.Equals(script.DifficultyCode, SelectedDifficultyOption.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (SelectedModeOption?.Code.Length > 0 &&
            !string.Equals(script.ModeCode, SelectedModeOption.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ScriptSearchText))
        {
            return true;
        }

        var query = ScriptSearchText.Trim();
        return script.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               script.MapDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               script.TagsText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSlotFilters(ManagedScriptSlotListItemViewModel slot)
    {
        if (SelectedTaskKindOption?.Code.Length > 0 &&
            !string.Equals(slot.TaskKind.ToKey(), SelectedTaskKindOption.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SlotSearchText))
        {
            return true;
        }

        var query = SlotSearchText.Trim();
        return slot.GroupDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               slot.SlotDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               slot.BoundScriptDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private ManagedScriptListItemViewModel CreateScriptItem(ManagedScriptAssetEntry entry)
    {
        return new ManagedScriptListItemViewModel
        {
            ScriptId = entry.ScriptId,
            DisplayName = entry.DisplayName,
            SourceFileName = entry.SourceFileName,
            MapCode = entry.Map.ToString(),
            DifficultyCode = entry.Difficulty.ToString(),
            ModeCode = entry.Mode.ToString(),
            MapDisplayName = GameElementCatalog.GetMapDisplayName(entry.Map),
            DifficultyDisplayName = GameElementCatalog.GetStageDifficultyDisplayName(entry.Difficulty),
            ModeDisplayName = GameElementCatalog.GetStageModeDisplayName(entry.Mode),
            TagsText = entry.Tags.Count == 0
                ? string.Empty
                : string.Join(", ", entry.Tags.Select(ScriptTagCatalog.GetDisplayName)),
            BindingCountText = entry.BindingCount.ToString(),
            StateText = ResolveScriptStateText(entry),
            UpdatedAt = entry.UpdatedAt
        };
    }

    private ManagedScriptSlotListItemViewModel CreateSlotItem(ManagedScriptSlotEntry entry)
    {
        var definition = entry.Definition;
        var groupDisplayName = definition.StageTarget is null
            ? definition.GroupName
            : $"{GameElementCatalog.GetMapDisplayName(definition.StageTarget.Map)} / {GameElementCatalog.GetStageDifficultyDisplayName(definition.StageTarget.Difficulty)}";
        var slotDisplayName = definition.StageTarget is null
            ? definition.DisplayName
            : GameElementCatalog.GetStageModeDisplayName(definition.StageTarget.Mode);

        return new ManagedScriptSlotListItemViewModel
        {
            SlotId = definition.SlotId,
            TaskKind = definition.TaskKind,
            GroupDisplayName = groupDisplayName,
            SlotDisplayName = slotDisplayName,
            BoundScriptDisplayName = entry.BoundScript?.DisplayName ?? string.Empty,
            StateText = ResolveSlotStateText(entry),
            HasBinding = entry.HasBinding
        };
    }

    private string ResolveScriptStateText(ManagedScriptAssetEntry entry)
    {
        if (entry.HasMissingFile)
        {
            return _localizationService.T("Library.State.MissingFile");
        }

        if (entry.HasMetadataIssue)
        {
            return _localizationService.T("Library.State.MetadataIssue");
        }

        return _localizationService.T("Library.State.Ready");
    }

    private string ResolveSlotStateText(ManagedScriptSlotEntry entry)
    {
        if (entry.IsBrokenBinding)
        {
            return _localizationService.T("Library.State.BrokenBinding");
        }

        if (!entry.HasBinding)
        {
            return entry.Definition.IsPlaceholder
                ? _localizationService.T("Library.State.Placeholder")
                : _localizationService.T("Library.State.Unbound");
        }

        return _localizationService.T("Library.State.Bound");
    }

    private void BuildFilterOptions()
    {
        var allText = _localizationService.T("Library.Filters.All");
        var previousTaskKind = SelectedTaskKindOption?.Code ?? string.Empty;
        var previousMap = SelectedMapOption?.Code ?? string.Empty;
        var previousDifficulty = SelectedDifficultyOption?.Code ?? string.Empty;
        var previousMode = SelectedModeOption?.Code ?? string.Empty;

        ReplaceCollection(
            TaskKindOptions,
            new[]
            {
                new LanguageOption { Code = string.Empty, DisplayName = allText },
                new LanguageOption { Code = AutoTaskKind.Custom.ToKey(), DisplayName = _localizationService.T("Library.TaskKind.Custom") },
                new LanguageOption { Code = AutoTaskKind.Collection.ToKey(), DisplayName = _localizationService.T("Library.TaskKind.Collection") },
                new LanguageOption { Code = AutoTaskKind.BlackBorder.ToKey(), DisplayName = _localizationService.T("Library.TaskKind.BlackBorder") },
                new LanguageOption { Code = AutoTaskKind.Race.ToKey(), DisplayName = _localizationService.T("Library.TaskKind.Race") }
            });

        ReplaceCollection(
            MapOptions,
            new[]
            {
                new LanguageOption { Code = string.Empty, DisplayName = allText }
            }.Concat(GameElementCatalog.Maps
                .Select(x => x.Type)
                .Distinct()
                .Select(map => new LanguageOption
                {
                    Code = map.ToString(),
                    DisplayName = GameElementCatalog.GetMapDisplayName(map)
                })));

        ReplaceCollection(
            DifficultyOptions,
            new[]
            {
                new LanguageOption { Code = string.Empty, DisplayName = allText },
                new LanguageOption { Code = StageDifficulty.Easy.ToString(), DisplayName = GameElementCatalog.GetStageDifficultyDisplayName(StageDifficulty.Easy) },
                new LanguageOption { Code = StageDifficulty.Medium.ToString(), DisplayName = GameElementCatalog.GetStageDifficultyDisplayName(StageDifficulty.Medium) },
                new LanguageOption { Code = StageDifficulty.Hard.ToString(), DisplayName = GameElementCatalog.GetStageDifficultyDisplayName(StageDifficulty.Hard) }
            });

        ReplaceCollection(
            ModeOptions,
            new[]
            {
                new LanguageOption { Code = string.Empty, DisplayName = allText }
            }.Concat(Enum.GetValues<StageMode>().Select(mode => new LanguageOption
            {
                Code = mode.ToString(),
                DisplayName = GameElementCatalog.GetStageModeDisplayName(mode)
            })));

        SelectedTaskKindOption = TaskKindOptions.FirstOrDefault(x => x.Code == previousTaskKind) ?? TaskKindOptions.FirstOrDefault();
        SelectedMapOption = MapOptions.FirstOrDefault(x => x.Code == previousMap) ?? MapOptions.FirstOrDefault();
        SelectedDifficultyOption = DifficultyOptions.FirstOrDefault(x => x.Code == previousDifficulty) ?? DifficultyOptions.FirstOrDefault();
        SelectedModeOption = ModeOptions.FirstOrDefault(x => x.Code == previousMode) ?? ModeOptions.FirstOrDefault();
    }

    private void RestoreSelection(string? scriptId, string? slotId)
    {
        SelectedScript = Scripts.FirstOrDefault(x => string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase))
                         ?? Scripts.FirstOrDefault();
        SelectedSlot = Slots.FirstOrDefault(x => string.Equals(x.SlotId, slotId, StringComparison.OrdinalIgnoreCase))
                       ?? Slots.FirstOrDefault();
    }

    private void RefreshLocalizedContent()
    {
        BuildFilterOptions();
        Refresh();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ImportText));
        OnPropertyChanged(nameof(ExportText));
        OnPropertyChanged(nameof(RemoveText));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(BindText));
        OnPropertyChanged(nameof(ClearBindingText));
        OnPropertyChanged(nameof(FiltersTitle));
        OnPropertyChanged(nameof(ScriptSearchLabel));
        OnPropertyChanged(nameof(ScriptSearchPlaceholder));
        OnPropertyChanged(nameof(MapFilterLabel));
        OnPropertyChanged(nameof(DifficultyFilterLabel));
        OnPropertyChanged(nameof(ModeFilterLabel));
        OnPropertyChanged(nameof(TaskKindFilterLabel));
        OnPropertyChanged(nameof(SlotSearchLabel));
        OnPropertyChanged(nameof(ScriptsSectionTitle));
        OnPropertyChanged(nameof(SlotsSectionTitle));
        OnPropertyChanged(nameof(EmptyScriptsText));
        OnPropertyChanged(nameof(EmptySlotsText));
        OnPropertyChanged(nameof(NameColumnText));
        OnPropertyChanged(nameof(MapColumnText));
        OnPropertyChanged(nameof(DifficultyColumnText));
        OnPropertyChanged(nameof(ModeColumnText));
        OnPropertyChanged(nameof(TagsColumnText));
        OnPropertyChanged(nameof(BindingsColumnText));
        OnPropertyChanged(nameof(StateColumnText));
        OnPropertyChanged(nameof(GroupColumnText));
        OnPropertyChanged(nameof(SlotColumnText));
        OnPropertyChanged(nameof(BoundScriptColumnText));
        OnPropertyChanged(nameof(SelectedScriptSummary));
    }

    private void ShowError(string titleKey, string message)
    {
        _appDialogService.Show(new AppDialogRequest
        {
            Title = _localizationService.T(titleKey),
            Message = message,
            PrimaryButtonText = _localizationService.T("Library.Dialog.Primary")
        });
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}

public sealed class ManagedScriptListItemViewModel
{
    public required string ScriptId { get; init; }

    public required string DisplayName { get; init; }

    public required string SourceFileName { get; init; }

    public required string MapCode { get; init; }

    public required string DifficultyCode { get; init; }

    public required string ModeCode { get; init; }

    public required string MapDisplayName { get; init; }

    public required string DifficultyDisplayName { get; init; }

    public required string ModeDisplayName { get; init; }

    public required string TagsText { get; init; }

    public required string BindingCountText { get; init; }

    public required string StateText { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed class ManagedScriptSlotListItemViewModel
{
    public required string SlotId { get; init; }

    public required AutoTaskKind TaskKind { get; init; }

    public required string GroupDisplayName { get; init; }

    public required string SlotDisplayName { get; init; }

    public required string BoundScriptDisplayName { get; init; }

    public required string StateText { get; init; }

    public bool HasBinding { get; init; }
}
