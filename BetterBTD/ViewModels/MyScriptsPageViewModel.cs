using System.Collections.ObjectModel;
using System.IO;
using BetterBTD.Helpers;
using BetterBTD.Models;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Windows;
using Wpf.Ui.Violeta.Controls;

namespace BetterBTD.ViewModels;

public sealed class MyScriptsPageViewModel : ObservableObject
{
    private readonly LocalizationService _localizationService;
    private readonly AppDialogService _appDialogService;
    private readonly ImportProgressDialogService _importProgressDialogService;
    private readonly ManagedScriptLibraryService _managedScriptLibraryService;

    private List<ManagedScriptListItemViewModel> _allScripts = [];
    private Task? _refreshTask;
    private bool _isUpdatingFilters;
    private bool _hasScripts;
    private bool _isLoadingScripts;
    private bool _isImportingScripts;
    private bool _hasLoadedScripts;
    private double _loadingProgressValue;
    private double _loadingProgressMaximum = 1d;
    private string _loadingStatusText = string.Empty;
    private string _scriptSearchText = string.Empty;
    private ICascadingItem? _selectedMapItem;
    private LanguageOption? _selectedDifficultyOption;
    private LanguageOption? _selectedModeOption;
    private ManagedScriptListItemViewModel? _selectedScript;
    private string _selectedScriptName = string.Empty;
    private string _selectedScriptDescription = string.Empty;
    private string _selectedScriptId = string.Empty;
    private string _selectedScriptHero = string.Empty;
    private string _selectedScriptMap = string.Empty;
    private string _selectedScriptDifficulty = string.Empty;
    private string _selectedScriptMode = string.Empty;
    private string _selectedScriptTags = string.Empty;
    private string _selectedScriptState = string.Empty;

    public MyScriptsPageViewModel(LocalizationService localizationService)
        : this(localizationService, ManagedScriptLibraryService.Instance)
    {
    }

    internal MyScriptsPageViewModel(
        LocalizationService localizationService,
        ManagedScriptLibraryService managedScriptLibraryService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _managedScriptLibraryService = managedScriptLibraryService ?? throw new ArgumentNullException(nameof(managedScriptLibraryService));
        _appDialogService = AppDialogService.Instance;
        _importProgressDialogService = ImportProgressDialogService.Instance;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        ImportScriptCommand = new AsyncRelayCommand(ImportScriptAsync, CanImportScript);
        ExportSelectedScriptCommand = new RelayCommand(ExportSelectedScript, CanExportSelectedScript);
        RemoveSelectedScriptCommand = new RelayCommand(RemoveSelectedScript, CanRemoveSelectedScript);
        CopySelectedScriptIdCommand = new RelayCommand(CopySelectedScriptId, CanCopySelectedScriptId);

        LoadingProgressMaximum = 1d;
        LoadingProgressValue = 0d;
        LoadingStatusText = _localizationService.T("Library.Loading.Message");
        IsLoadingScripts = true;

        _localizationService.LanguageChanged += (_, _) => OnLanguageChanged();

        RefreshLocalizedContent();
    }

    public ObservableCollection<LanguageOption> DifficultyOptions { get; } = [];

    public ObservableCollection<LanguageOption> ModeOptions { get; } = [];

    public ObservableCollection<ManagedScriptListItemViewModel> Scripts { get; } = [];

    public IReadOnlyList<ICascadingItem> MapItems => GameElementCascadingItems.MapItems;

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ImportScriptCommand { get; }

    public IRelayCommand ExportSelectedScriptCommand { get; }

    public IRelayCommand RemoveSelectedScriptCommand { get; }

    public IRelayCommand CopySelectedScriptIdCommand { get; }

    public string ImportText => _localizationService.T("Library.Action.Import");

    public string ExportText => _localizationService.T("Library.Action.Export");

    public string RemoveText => _localizationService.T("Library.Action.Remove");

    public string EditText => _localizationService.T("Library.Action.Edit");

    public string RunText => _localizationService.T("Library.Action.Run");

    public string RefreshText => _localizationService.T("Library.Action.Refresh");

    public string ScriptSearchLabel => _localizationService.T("Library.Filters.Search");

    public string ScriptSearchPlaceholder => _localizationService.T("Library.Filters.Search.Placeholder");

    public string MapFilterLabel => _localizationService.T("Library.Filters.Map");

    public string DifficultyFilterLabel => _localizationService.T("Library.Filters.Difficulty");

    public string ModeFilterLabel => _localizationService.T("Library.Filters.Mode");

    public string ScriptsSectionText => _localizationService.T("Library.Section.Scripts");

    public string PropertiesSectionText => _localizationService.T("Library.Section.Properties");

    public string EmptyScriptsText => _localizationService.T("Library.Empty.Scripts");

    public string VisibleScriptCountText => Scripts.Count == _allScripts.Count
        ? Scripts.Count.ToString()
        : $"{Scripts.Count} / {_allScripts.Count}";

    public bool HasScripts
    {
        get => _hasScripts;
        private set
        {
            if (!SetProperty(ref _hasScripts, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowEmptyScriptsState));
        }
    }

    public bool IsLoadingScripts
    {
        get => _isLoadingScripts;
        private set
        {
            if (!SetProperty(ref _isLoadingScripts, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowEmptyScriptsState));
            RefreshCommand.NotifyCanExecuteChanged();
            ImportScriptCommand.NotifyCanExecuteChanged();
            ExportSelectedScriptCommand.NotifyCanExecuteChanged();
            RemoveSelectedScriptCommand.NotifyCanExecuteChanged();
            CopySelectedScriptIdCommand.NotifyCanExecuteChanged();
        }
    }

    public bool ShowEmptyScriptsState => !IsLoadingScripts && !HasScripts;

    public double LoadingProgressValue
    {
        get => _loadingProgressValue;
        private set => SetProperty(ref _loadingProgressValue, value);
    }

    public double LoadingProgressMaximum
    {
        get => _loadingProgressMaximum;
        private set => SetProperty(ref _loadingProgressMaximum, value);
    }

    public string LoadingStatusText
    {
        get => _loadingStatusText;
        private set => SetProperty(ref _loadingStatusText, value);
    }

    public string SelectedScriptSummary => SelectedScript is null
        ? _localizationService.T("Library.Summary.None")
        : SelectedScript.DisplayName;

    public bool HasSelectedScript => SelectedScript is not null;

    public string PropertyNameText => _localizationService.T("Library.Property.Name");

    public string PropertyDescriptionText => _localizationService.T("Library.Property.Description");

    public string PropertyScriptIdText => _localizationService.T("Library.Property.ScriptId");

    public string PropertyHeroText => _localizationService.T("Library.Property.Hero");

    public string PropertyMapText => _localizationService.T("Library.Property.Map");

    public string PropertyDifficultyText => _localizationService.T("Library.Property.Difficulty");

    public string PropertyModeText => _localizationService.T("Library.Property.Mode");

    public string PropertyTagsText => _localizationService.T("Library.Property.Tags");

    public string PropertyStateText => _localizationService.T("Library.Property.State");

    public string CopyScriptIdText => _localizationService.T("Library.Action.CopyScriptId");

    public string ScriptSearchText
    {
        get => _scriptSearchText;
        set
        {
            if (!SetProperty(ref _scriptSearchText, value))
            {
                return;
            }

            RefreshFilteredScripts();
        }
    }

    public ICascadingItem? SelectedMapItem
    {
        get => _selectedMapItem;
        set
        {
            if (!SetProperty(ref _selectedMapItem, value))
            {
                return;
            }

            if (_isUpdatingFilters)
            {
                return;
            }

            RefreshFilteredScripts();
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

            if (_isUpdatingFilters)
            {
                return;
            }

            RefreshFilteredScripts();
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

            if (_isUpdatingFilters)
            {
                return;
            }

            RefreshFilteredScripts();
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

            UpdateSelectedScriptDetails();
            OnPropertyChanged(nameof(SelectedScriptSummary));
            OnPropertyChanged(nameof(HasSelectedScript));
            ExportSelectedScriptCommand.NotifyCanExecuteChanged();
            RemoveSelectedScriptCommand.NotifyCanExecuteChanged();
            CopySelectedScriptIdCommand.NotifyCanExecuteChanged();
        }
    }

    public string SelectedScriptName
    {
        get => _selectedScriptName;
        private set => SetProperty(ref _selectedScriptName, value);
    }

    public string SelectedScriptDescription
    {
        get => _selectedScriptDescription;
        private set => SetProperty(ref _selectedScriptDescription, value);
    }

    public string SelectedScriptId
    {
        get => _selectedScriptId;
        private set => SetProperty(ref _selectedScriptId, value);
    }

    public string SelectedScriptHero
    {
        get => _selectedScriptHero;
        private set => SetProperty(ref _selectedScriptHero, value);
    }

    public string SelectedScriptMap
    {
        get => _selectedScriptMap;
        private set => SetProperty(ref _selectedScriptMap, value);
    }

    public string SelectedScriptDifficulty
    {
        get => _selectedScriptDifficulty;
        private set => SetProperty(ref _selectedScriptDifficulty, value);
    }

    public string SelectedScriptMode
    {
        get => _selectedScriptMode;
        private set => SetProperty(ref _selectedScriptMode, value);
    }

    public string SelectedScriptTags
    {
        get => _selectedScriptTags;
        private set => SetProperty(ref _selectedScriptTags, value);
    }

    public string SelectedScriptState
    {
        get => _selectedScriptState;
        private set => SetProperty(ref _selectedScriptState, value);
    }

    public Task EnsureInitializedAsync()
    {
        return _hasLoadedScripts ? Task.CompletedTask : RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshTask is not null && !_refreshTask.IsCompleted)
        {
            await _refreshTask;
            return;
        }

        var refreshTask = RefreshCoreAsync();
        _refreshTask = refreshTask;

        try
        {
            await refreshTask;
        }
        finally
        {
            if (ReferenceEquals(_refreshTask, refreshTask))
            {
                _refreshTask = null;
            }
        }
    }

    private async Task RefreshCoreAsync()
    {
        IsLoadingScripts = true;
        LoadingProgressMaximum = 1d;
        LoadingProgressValue = 0d;
        LoadingStatusText = _localizationService.T("Library.Loading.Message");

        try
        {
            var progress = new Progress<ManagedScriptLibraryLoadProgress>(UpdateLoadingProgress);
            var snapshot = await Task.Run(() => _managedScriptLibraryService.GetSnapshot(progress));
            _allScripts = snapshot.Scripts.Select(CreateScriptItem).ToList();
            BuildFilterOptions();
            RefreshFilteredScripts();
            _hasLoadedScripts = true;
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.RefreshError.Title", ex.Message);
        }
        finally
        {
            IsLoadingScripts = false;
        }
    }

    private async Task ImportScriptAsync()
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

        var isLegacyPackage = string.Equals(Path.GetExtension(dialog.FileName), ".btd6s", StringComparison.OrdinalIgnoreCase);
        using var progressDialog = _importProgressDialogService.Show(new ImportProgressDialogRequest
        {
            Title = _localizationService.T("Library.Dialog.ImportProgress.Title"),
            Message = _localizationService.T(isLegacyPackage
                ? "Library.Dialog.ImportProgress.PackageMessage.Initial"
                : "Library.Dialog.ImportProgress.Message")
        });

        _isImportingScripts = true;
        ImportScriptCommand.NotifyCanExecuteChanged();

        try
        {
            if (isLegacyPackage)
            {
                var progress = new Progress<int>(processedCount =>
                {
                    progressDialog.UpdateMessage(string.Format(
                        _localizationService.T("Library.Dialog.ImportProgress.PackageMessage"),
                        processedCount));
                });

                await Task.Run(() => _managedScriptLibraryService.ImportLegacyScriptCollection(dialog.FileName, progress));
            }
            else
            {
                await Task.Run(() => _managedScriptLibraryService.ImportScript(dialog.FileName));
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.ImportError.Title", ex.Message);
        }
        finally
        {
            _isImportingScripts = false;
            ImportScriptCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanImportScript()
    {
        return !_isImportingScripts && !IsLoadingScripts;
    }

    private bool CanRefresh()
    {
        return !_isImportingScripts && !IsLoadingScripts;
    }

    private bool CanExportSelectedScript()
    {
        return !IsLoadingScripts && SelectedScript is not null;
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
        return !IsLoadingScripts && SelectedScript is not null;
    }

    private bool CanCopySelectedScriptId()
    {
        return !IsLoadingScripts && SelectedScript is not null && !string.IsNullOrWhiteSpace(SelectedScript.ScriptId);
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
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.RemoveError.Title", ex.Message);
        }
    }

    private void CopySelectedScriptId()
    {
        if (SelectedScript is null || string.IsNullOrWhiteSpace(SelectedScript.ScriptId))
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedScript.ScriptId);
        }
        catch (Exception ex)
        {
            ShowError("Library.Dialog.CopyScriptIdError.Title", ex.Message);
        }
    }

    private void RefreshFilteredScripts()
    {
        var selectedScriptId = SelectedScript?.ScriptId;

        var filteredScripts = _allScripts
            .Where(MatchesScriptFilters)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceCollection(Scripts, filteredScripts);
        HasScripts = Scripts.Count > 0;
        SelectedScript = Scripts.FirstOrDefault(x => x.ScriptId == selectedScriptId) ?? Scripts.FirstOrDefault();
        OnPropertyChanged(nameof(VisibleScriptCountText));
    }

    private bool MatchesScriptFilters(ManagedScriptListItemViewModel script)
    {
        if (SelectedMapItem?.Tag is GameMapType selectedMap &&
            !string.Equals(script.MapCode, selectedMap.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedDifficultyOption?.Code) &&
            !string.Equals(script.DifficultyCode, SelectedDifficultyOption.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedModeOption?.Code) &&
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
               script.TagsText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               script.HeroDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               script.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               script.SourceFileName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private ManagedScriptListItemViewModel CreateScriptItem(ManagedScriptAssetEntry entry)
    {
        return new ManagedScriptListItemViewModel
        {
            ScriptId = entry.ScriptId,
            DisplayName = entry.DisplayName,
            Description = entry.Description,
            SourceFileName = entry.SourceFileName,
            MapCode = entry.Map.ToString(),
            DifficultyCode = entry.Difficulty.ToString(),
            ModeCode = entry.Mode.ToString(),
            MapDisplayName = GameElementCatalog.GetMapDisplayName(entry.Map),
            DifficultyDisplayName = GameElementCatalog.GetStageDifficultyDisplayName(entry.Difficulty),
            ModeDisplayName = GameElementCatalog.GetStageModeDisplayName(entry.Mode),
            HeroDisplayName = GameElementCatalog.GetHeroDisplayName(entry.Hero),
            TagsText = entry.Tags.Count == 0
                ? string.Empty
                : string.Join(", ", entry.Tags.Select(ScriptTagCatalog.GetDisplayName)),
            StateText = ResolveScriptStateText(entry),
            PreviewText = string.IsNullOrWhiteSpace(entry.Description) ? entry.SourceFileName : entry.Description.Trim(),
            UpdatedAtText = entry.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            MonogramText = ResolveMonogram(entry.DisplayName),
            HasIssue = entry.HasMissingFile || entry.HasMetadataIssue,
            UpdatedAt = entry.UpdatedAt
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

    private void BuildFilterOptions()
    {
        var allText = _localizationService.T("Library.Filters.All");
        var previousMap = SelectedMapItem?.Tag as GameMapType?;
        var previousDifficulty = SelectedDifficultyOption?.Code ?? string.Empty;
        var previousMode = SelectedModeOption?.Code ?? string.Empty;

        _isUpdatingFilters = true;

        try
        {
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

            SelectedMapItem = previousMap is GameMapType mapType
                ? FindMapItem(mapType)
                : null;
            SelectedDifficultyOption = DifficultyOptions.FirstOrDefault(x => x.Code == previousDifficulty) ?? DifficultyOptions.FirstOrDefault();
            SelectedModeOption = ModeOptions.FirstOrDefault(x => x.Code == previousMode) ?? ModeOptions.FirstOrDefault();
        }
        finally
        {
            _isUpdatingFilters = false;
        }
    }

    private void UpdateSelectedScriptDetails()
    {
        if (SelectedScript is null)
        {
            SelectedScriptName = string.Empty;
            SelectedScriptDescription = string.Empty;
            SelectedScriptId = string.Empty;
            SelectedScriptHero = string.Empty;
            SelectedScriptMap = string.Empty;
            SelectedScriptDifficulty = string.Empty;
            SelectedScriptMode = string.Empty;
            SelectedScriptTags = string.Empty;
            SelectedScriptState = string.Empty;
            return;
        }

        SelectedScriptName = FormatDetailValue(SelectedScript.DisplayName);
        SelectedScriptDescription = FormatDetailValue(SelectedScript.Description);
        SelectedScriptId = FormatDetailValue(SelectedScript.ScriptId);
        SelectedScriptHero = FormatDetailValue(SelectedScript.HeroDisplayName);
        SelectedScriptMap = FormatDetailValue(SelectedScript.MapDisplayName);
        SelectedScriptDifficulty = FormatDetailValue(SelectedScript.DifficultyDisplayName);
        SelectedScriptMode = FormatDetailValue(SelectedScript.ModeDisplayName);
        SelectedScriptTags = FormatDetailValue(SelectedScript.TagsText);
        SelectedScriptState = FormatDetailValue(SelectedScript.StateText);
    }

    private void RefreshLocalizedContent()
    {
        if (IsLoadingScripts)
        {
            LoadingStatusText = _localizationService.T("Library.Loading.Message");
        }

        OnPropertyChanged(nameof(ImportText));
        OnPropertyChanged(nameof(ExportText));
        OnPropertyChanged(nameof(RemoveText));
        OnPropertyChanged(nameof(EditText));
        OnPropertyChanged(nameof(RunText));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(ScriptSearchLabel));
        OnPropertyChanged(nameof(ScriptSearchPlaceholder));
        OnPropertyChanged(nameof(MapItems));
        OnPropertyChanged(nameof(MapFilterLabel));
        OnPropertyChanged(nameof(DifficultyFilterLabel));
        OnPropertyChanged(nameof(ModeFilterLabel));
        OnPropertyChanged(nameof(ScriptsSectionText));
        OnPropertyChanged(nameof(PropertiesSectionText));
        OnPropertyChanged(nameof(EmptyScriptsText));
        OnPropertyChanged(nameof(ShowEmptyScriptsState));
        OnPropertyChanged(nameof(VisibleScriptCountText));
        OnPropertyChanged(nameof(SelectedScriptSummary));
        OnPropertyChanged(nameof(HasSelectedScript));
        OnPropertyChanged(nameof(PropertyNameText));
        OnPropertyChanged(nameof(PropertyDescriptionText));
        OnPropertyChanged(nameof(PropertyScriptIdText));
        OnPropertyChanged(nameof(PropertyHeroText));
        OnPropertyChanged(nameof(PropertyMapText));
        OnPropertyChanged(nameof(PropertyDifficultyText));
        OnPropertyChanged(nameof(PropertyModeText));
        OnPropertyChanged(nameof(PropertyTagsText));
        OnPropertyChanged(nameof(PropertyStateText));
        OnPropertyChanged(nameof(CopyScriptIdText));
    }

    private void OnLanguageChanged()
    {
        RefreshLocalizedContent();

        if (_hasLoadedScripts)
        {
            _ = RefreshAsync();
        }
    }

    private void UpdateLoadingProgress(ManagedScriptLibraryLoadProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        LoadingProgressMaximum = Math.Max(progress.TotalScriptCount, 1);
        LoadingProgressValue = progress.TotalScriptCount == 0
            ? 0
            : Math.Min(progress.ProcessedScriptCount, progress.TotalScriptCount);
        LoadingStatusText = progress.TotalScriptCount == 0
            ? _localizationService.T("Library.Loading.Message")
            : string.Format(
                _localizationService.T("Library.Loading.Progress"),
                progress.ProcessedScriptCount,
                progress.TotalScriptCount);
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

    private static string FormatDetailValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim();
    }

    private static string ResolveMonogram(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "S";
        }

        return value.Trim()[0].ToString().ToUpperInvariant();
    }

    private static ICascadingItem? FindMapItem(GameMapType mapType)
    {
        foreach (var tier in GameElementCascadingItems.MapItems)
        {
            var found = tier.Children?.FirstOrDefault(item => item.Tag is GameMapType candidate && candidate == mapType);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}

public sealed class ManagedScriptListItemViewModel
{
    public required string ScriptId { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string SourceFileName { get; init; }

    public required string MapCode { get; init; }

    public required string DifficultyCode { get; init; }

    public required string ModeCode { get; init; }

    public required string MapDisplayName { get; init; }

    public required string DifficultyDisplayName { get; init; }

    public required string ModeDisplayName { get; init; }

    public required string HeroDisplayName { get; init; }

    public required string TagsText { get; init; }

    public required string StateText { get; init; }

    public required string PreviewText { get; init; }

    public required string UpdatedAtText { get; init; }

    public required string MonogramText { get; init; }

    public bool HasIssue { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
