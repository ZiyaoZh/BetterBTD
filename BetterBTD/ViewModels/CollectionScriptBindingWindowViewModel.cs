using System.Collections.ObjectModel;
using System.ComponentModel;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Services.ChildSession;
using BetterBTD.Services.MyScripts;
using BetterBTD.Services.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterBTD.ViewModels;

public sealed partial class CollectionScriptBindingWindowViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService _localizationService;
    private readonly AppDialogService _appDialogService;
    private readonly ManagedScriptLibraryService _managedScriptLibraryService;
    private readonly Action _closeWindow;
    private readonly Dictionary<string, string> _savedBindings = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ManagedScriptAssetEntry> _scripts = [];

    [ObservableProperty]
    private CollectionScriptBindingModeViewModel? _selectedMode;

    [ObservableProperty]
    private CollectionScriptBindingModeViewModel? _copySourceMode;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _canEdit;

    internal CollectionScriptBindingWindowViewModel(
        LocalizationService localizationService,
        AppDialogService appDialogService,
        ManagedScriptLibraryService managedScriptLibraryService,
        string? initialModeKey,
        Action closeWindow)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _appDialogService = appDialogService ?? throw new ArgumentNullException(nameof(appDialogService));
        _managedScriptLibraryService = managedScriptLibraryService ?? throw new ArgumentNullException(nameof(managedScriptLibraryService));
        _closeWindow = closeWindow ?? throw new ArgumentNullException(nameof(closeWindow));
        _canEdit = ChildSessionRuntimeState.CanPersistSharedData;

        SaveCommand = new RelayCommand(Save, CanSave);
        CloseCommand = new RelayCommand(_closeWindow);
        CopyModeCommand = new RelayCommand(CopyMode, CanCopyMode);
        ClearModeCommand = new RelayCommand(ClearMode, CanClearMode);

        Load(initialModeKey);
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<CollectionScriptBindingModeViewModel> Modes { get; } = [];

    public ObservableCollection<CollectionScriptBindingModeViewModel> CopySourceModes { get; } = [];

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand CopyModeCommand { get; }

    public IRelayCommand ClearModeCommand { get; }

    public string WindowTitle => _localizationService.T("Tasks.CollectionBinding.WindowTitle");

    public string PageDescription => _localizationService.T("Tasks.CollectionBinding.Description");

    public string MapHeader => _localizationService.T("Tasks.CollectionBinding.Map");

    public string ScriptHeader => _localizationService.T("Tasks.CollectionBinding.Script");

    public string CopyFromLabel => _localizationService.T("Tasks.CollectionBinding.CopyFrom");

    public string CopyText => _localizationService.T("Tasks.CollectionBinding.Copy");

    public string ClearText => _localizationService.T("Tasks.CollectionBinding.Clear");

    public string CancelText => _localizationService.T("Tasks.Dialog.Cancel");

    public string SaveText => _localizationService.T("Tasks.CollectionBinding.Save");

    partial void OnSelectedModeChanged(CollectionScriptBindingModeViewModel? value)
    {
        RefreshCopySources();
        StatusText = BuildStatusText(value);
        CopyModeCommand.NotifyCanExecuteChanged();
        ClearModeCommand.NotifyCanExecuteChanged();
    }

    partial void OnCopySourceModeChanged(CollectionScriptBindingModeViewModel? value)
    {
        CopyModeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDirtyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanEditChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        CopyModeCommand.NotifyCanExecuteChanged();
        ClearModeCommand.NotifyCanExecuteChanged();
        StatusText = BuildStatusText(SelectedMode);
    }

    public void RefreshWritableState()
    {
        CanEdit = ChildSessionRuntimeState.CanPersistSharedData;
    }

    public bool ConfirmClose()
    {
        if (!IsDirty)
        {
            return true;
        }

        var result = _appDialogService.Show(new AppDialogRequest
        {
            Title = _localizationService.T("Tasks.CollectionBinding.Unsaved.Title"),
            Message = _localizationService.T("Tasks.CollectionBinding.Unsaved.Message"),
            PrimaryButtonText = SaveText,
            SecondaryButtonText = _localizationService.T("Tasks.CollectionBinding.Discard"),
            CloseButtonText = CancelText
        });

        if (result == AppDialogResult.Primary)
        {
            Save();
            return !IsDirty;
        }

        return result == AppDialogResult.Secondary;
    }

    public void Dispose()
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;
        foreach (var row in Modes.SelectMany(mode => mode.Rows))
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    private void Load(string? initialModeKey)
    {
        var snapshot = _managedScriptLibraryService.GetSnapshot();
        _scripts = snapshot.Scripts;
        var collectionSlots = snapshot.Slots
            .Where(slot => slot.Definition.TaskKind == AutoTaskKind.Collection)
            .ToList();

        _savedBindings.Clear();
        foreach (var slot in collectionSlots)
        {
            _savedBindings[slot.Definition.SlotId] = slot.BoundScriptId;
        }

        foreach (var modeDefinition in ManagedScriptCollectionModeCatalog.Modes)
        {
            var mode = new CollectionScriptBindingModeViewModel(
                modeDefinition.Key,
                ResolveModeDisplayName(modeDefinition.Key));

            foreach (var slot in collectionSlots
                         .Where(slot => string.Equals(
                             GetQualifier(slot.Definition, "modeKey"),
                             modeDefinition.Key,
                             StringComparison.OrdinalIgnoreCase))
                         .OrderBy(slot => slot.Definition.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var map = Enum.TryParse<GameMapType>(GetQualifier(slot.Definition, "map"), true, out var parsedMap)
                    ? parsedMap
                    : (GameMapType?)null;
                var choices = BuildScriptChoices(
                    _scripts,
                    map,
                    slot.BoundScriptId,
                    slot.IsBrokenBinding);
                var selectedChoice = choices.FirstOrDefault(choice => string.Equals(
                                         choice.ScriptId,
                                         slot.BoundScriptId,
                                         StringComparison.OrdinalIgnoreCase))
                                     ?? choices[0];
                var row = new CollectionScriptBindingRowViewModel(
                    slot.Definition.SlotId,
                    map is null ? slot.Definition.DisplayName : GameElementCatalog.GetMapDisplayName(map.Value),
                    choices,
                    selectedChoice);
                row.PropertyChanged += OnRowPropertyChanged;
                mode.Rows.Add(row);
            }

            mode.RefreshConfiguredCount(_localizationService);
            Modes.Add(mode);
        }

        SelectedMode = Modes.FirstOrDefault(mode => string.Equals(mode.ModeKey, initialModeKey, StringComparison.OrdinalIgnoreCase))
                       ?? Modes.FirstOrDefault();
        IsDirty = false;
    }

    private ObservableCollection<CollectionScriptChoiceViewModel> BuildScriptChoices(
        IReadOnlyList<ManagedScriptAssetEntry> scripts,
        GameMapType? map,
        string boundScriptId,
        bool isBrokenBinding)
    {
        var choices = new ObservableCollection<CollectionScriptChoiceViewModel>
        {
            new(string.Empty, _localizationService.T("Tasks.CollectionBinding.Unconfigured"), false)
        };

        foreach (var script in scripts
                     .OrderByDescending(script => IsRecommended(script, map))
                     .ThenBy(script => script.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var hasIssue = script.HasMissingFile || script.HasMetadataIssue;
            var metadata = string.Join(
                " / ",
                GameElementCatalog.GetMapDisplayName(script.Map),
                GameElementCatalog.GetHeroDisplayName(script.Hero));
            var issueSuffix = hasIssue
                ? $" - {_localizationService.T("Tasks.CollectionBinding.NeedsAttention")}"
                : string.Empty;
            choices.Add(new CollectionScriptChoiceViewModel(
                script.ScriptId,
                $"{script.DisplayName} ({metadata}){issueSuffix}",
                hasIssue));
        }

        if (isBrokenBinding &&
            choices.All(choice => !string.Equals(choice.ScriptId, boundScriptId, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Insert(1, new CollectionScriptChoiceViewModel(
                boundScriptId,
                _localizationService.T("Tasks.CollectionBinding.MissingScript"),
                true));
        }

        return choices;
    }

    private static bool IsRecommended(ManagedScriptAssetEntry script, GameMapType? map)
    {
        return map is not null &&
               script.Map == map.Value &&
               script.Tags.Contains("collection", StringComparer.OrdinalIgnoreCase) &&
               !script.HasMissingFile &&
               !script.HasMetadataIssue;
    }

    private bool CanSave() => CanEdit && IsDirty;

    private void Save()
    {
        try
        {
            var bindings = Modes
                .SelectMany(mode => mode.Rows)
                .ToDictionary(
                    row => row.SlotId,
                    row => (string?)row.SelectedScript?.ScriptId,
                    StringComparer.OrdinalIgnoreCase);
            _managedScriptLibraryService.SetTaskBindings(AutoTaskKind.Collection, bindings);

            _savedBindings.Clear();
            foreach (var binding in bindings)
            {
                _savedBindings[binding.Key] = binding.Value ?? string.Empty;
            }

            IsDirty = false;
            StatusText = _localizationService.T("Tasks.CollectionBinding.Saved");
        }
        catch (Exception ex)
        {
            _appDialogService.Show(new AppDialogRequest
            {
                Title = _localizationService.T("Tasks.CollectionBinding.SaveFailed"),
                Message = ex.Message,
                PrimaryButtonText = _localizationService.T("Tasks.Dialog.Ok")
            });
        }
    }

    private bool CanCopyMode()
    {
        return CanEdit && SelectedMode is not null && CopySourceMode is not null;
    }

    private void CopyMode()
    {
        if (SelectedMode is null || CopySourceMode is null)
        {
            return;
        }

        var result = _appDialogService.Show(new AppDialogRequest
        {
            Title = _localizationService.T("Tasks.CollectionBinding.CopyConfirm.Title"),
            Message = string.Format(
                _localizationService.T("Tasks.CollectionBinding.CopyConfirm.Message"),
                CopySourceMode.DisplayName,
                SelectedMode.DisplayName),
            PrimaryButtonText = CopyText,
            SecondaryButtonText = CancelText
        });
        if (result != AppDialogResult.Primary)
        {
            return;
        }

        var sourceByMap = CopySourceMode.Rows.ToDictionary(
            GetMapQualifierFromSlotId,
            row => row.SelectedScript?.ScriptId ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
        foreach (var row in SelectedMode.Rows)
        {
            if (!sourceByMap.TryGetValue(GetMapQualifierFromSlotId(row), out var scriptId))
            {
                continue;
            }

            row.SelectedScript = row.ScriptChoices.FirstOrDefault(choice =>
                                     string.Equals(choice.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase))
                                 ?? row.ScriptChoices[0];
        }
    }

    private bool CanClearMode()
    {
        return CanEdit && SelectedMode?.Rows.Any(row => !string.IsNullOrWhiteSpace(row.SelectedScript?.ScriptId)) == true;
    }

    private void ClearMode()
    {
        if (SelectedMode is null)
        {
            return;
        }

        var result = _appDialogService.Show(new AppDialogRequest
        {
            Title = _localizationService.T("Tasks.CollectionBinding.ClearConfirm.Title"),
            Message = string.Format(
                _localizationService.T("Tasks.CollectionBinding.ClearConfirm.Message"),
                SelectedMode.DisplayName),
            PrimaryButtonText = ClearText,
            SecondaryButtonText = CancelText
        });
        if (result != AppDialogResult.Primary)
        {
            return;
        }

        foreach (var row in SelectedMode.Rows)
        {
            row.SelectedScript = row.ScriptChoices[0];
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CollectionScriptBindingRowViewModel row ||
            e.PropertyName != nameof(CollectionScriptBindingRowViewModel.SelectedScript))
        {
            return;
        }

        var mode = Modes.First(item => item.Rows.Contains(row));
        mode.RefreshConfiguredCount(_localizationService);
        if (ReferenceEquals(mode, SelectedMode))
        {
            StatusText = BuildStatusText(mode);
        }

        IsDirty = Modes
            .SelectMany(item => item.Rows)
            .Any(item => !_savedBindings.TryGetValue(item.SlotId, out var savedScriptId) ||
                         !string.Equals(savedScriptId, item.SelectedScript?.ScriptId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        ClearModeCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCopySources()
    {
        var previousKey = CopySourceMode?.ModeKey;
        CopySourceModes.Clear();
        foreach (var mode in Modes.Where(mode => !ReferenceEquals(mode, SelectedMode)))
        {
            CopySourceModes.Add(mode);
        }

        CopySourceMode = CopySourceModes.FirstOrDefault(mode => string.Equals(mode.ModeKey, previousKey, StringComparison.OrdinalIgnoreCase))
                         ?? CopySourceModes.FirstOrDefault();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var mode in Modes)
        {
            mode.DisplayName = ResolveModeDisplayName(mode.ModeKey);
            foreach (var row in mode.Rows)
            {
                var selectedScriptId = row.SelectedScript?.ScriptId ?? string.Empty;
                var hasMap = Enum.TryParse<GameMapType>(GetMapQualifierFromSlotId(row), true, out var map);
                if (hasMap)
                {
                    row.MapDisplayName = GameElementCatalog.GetMapDisplayName(map);
                }

                var choices = BuildScriptChoices(
                    _scripts,
                    hasMap ? map : null,
                    selectedScriptId,
                    selectedScriptId.Length > 0 &&
                    _scripts.All(script => !string.Equals(script.ScriptId, selectedScriptId, StringComparison.OrdinalIgnoreCase)));
                row.ScriptChoices.Clear();
                foreach (var choice in choices)
                {
                    row.ScriptChoices.Add(choice);
                }

                row.SelectedScript = row.ScriptChoices.First(choice =>
                    string.Equals(choice.ScriptId, selectedScriptId, StringComparison.OrdinalIgnoreCase));
            }

            mode.RefreshConfiguredCount(_localizationService);
        }

        RaiseLocalizedProperties();
        StatusText = BuildStatusText(SelectedMode);
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(PageDescription));
        OnPropertyChanged(nameof(MapHeader));
        OnPropertyChanged(nameof(ScriptHeader));
        OnPropertyChanged(nameof(CopyFromLabel));
        OnPropertyChanged(nameof(CopyText));
        OnPropertyChanged(nameof(ClearText));
        OnPropertyChanged(nameof(CancelText));
        OnPropertyChanged(nameof(SaveText));
    }

    private string ResolveModeDisplayName(string modeKey)
    {
        return modeKey switch
        {
            "simple" => _localizationService.T("Tasks.CollectionOption.Simple"),
            "double-cash" => _localizationService.T("Tasks.CollectionOption.DoubleCash"),
            "fast-track" => _localizationService.T("Tasks.CollectionOption.FastTrack"),
            "double-cash-fast-track" => _localizationService.T("Tasks.CollectionOption.DoubleCashFastTrack"),
            _ => ManagedScriptCollectionModeCatalog.GetDisplayName(modeKey)
        };
    }

    private string BuildStatusText(CollectionScriptBindingModeViewModel? mode)
    {
        if (mode is null)
        {
            return string.Empty;
        }

        return CanEdit
            ? mode.ConfiguredCountText
            : $"{_localizationService.T("Tasks.CollectionBinding.ReadOnly")} - {mode.ConfiguredCountText}";
    }

    private static string GetQualifier(ManagedScriptSlotDefinition definition, string key)
    {
        return definition.Qualifiers.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string GetMapQualifierFromSlotId(CollectionScriptBindingRowViewModel row)
    {
        var separatorIndex = row.SlotId.LastIndexOf('/');
        return separatorIndex >= 0 ? row.SlotId[(separatorIndex + 1)..] : row.SlotId;
    }
}

public sealed partial class CollectionScriptBindingModeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _configuredCountText = string.Empty;

    public CollectionScriptBindingModeViewModel(string modeKey, string displayName)
    {
        ModeKey = modeKey;
        _displayName = displayName;
    }

    public string ModeKey { get; }

    public ObservableCollection<CollectionScriptBindingRowViewModel> Rows { get; } = [];

    public void RefreshConfiguredCount(LocalizationService localizationService)
    {
        var configuredCount = Rows.Count(row => !string.IsNullOrWhiteSpace(row.SelectedScript?.ScriptId));
        ConfiguredCountText = string.Format(
            localizationService.T("Tasks.CollectionBinding.ConfiguredCount"),
            configuredCount,
            Rows.Count);
    }
}

public sealed partial class CollectionScriptBindingRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _mapDisplayName;

    [ObservableProperty]
    private CollectionScriptChoiceViewModel? _selectedScript;

    public CollectionScriptBindingRowViewModel(
        string slotId,
        string mapDisplayName,
        ObservableCollection<CollectionScriptChoiceViewModel> scriptChoices,
        CollectionScriptChoiceViewModel selectedScript)
    {
        SlotId = slotId;
        _mapDisplayName = mapDisplayName;
        ScriptChoices = scriptChoices;
        _selectedScript = selectedScript;
    }

    public string SlotId { get; }

    public ObservableCollection<CollectionScriptChoiceViewModel> ScriptChoices { get; }
}

public sealed record CollectionScriptChoiceViewModel(string ScriptId, string DisplayText, bool HasIssue);
