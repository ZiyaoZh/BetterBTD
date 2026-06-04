using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterBTD.Core.AutoTasks;
using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Services;
using BetterBTD.Services.Shared;
using BetterBTD.Views.Windows;
using Microsoft.Win32;

namespace BetterBTD.ViewModels;

public sealed class AutoTasksPageViewModel : ObservableObject
{
    private const string AllMapsOptionCode = "__all__";

    private static readonly StageEntryTarget CollectionPlaceholderStageTarget = new()
    {
        Map = GameMapType.DarkCastle,
        Difficulty = StageDifficulty.Hard,
        Mode = StageMode.CHIMPS
    };

    private static readonly StageEntryTarget GoldBalloonPlaceholderStageTarget = new()
    {
        Map = GameMapType.MonkeyMeadow,
        Difficulty = StageDifficulty.Easy,
        Mode = StageMode.Standard
    };

    private static readonly StageEntryTarget LoopStagePlaceholderStageTarget = new()
    {
        Map = GameMapType.MonkeyMeadow,
        Difficulty = StageDifficulty.Easy,
        Mode = StageMode.Standard
    };

    private static readonly StageEntryTarget OdysseyPlaceholderStageTarget = new()
    {
        Map = GameMapType.MonkeyMeadow,
        Difficulty = StageDifficulty.Easy,
        Mode = StageMode.Standard
    };

    private readonly LocalizationService _localizationService;
    private readonly AppDialogService _appDialogService;
    private readonly ImportProgressDialogService _importProgressDialogService;
    private readonly AutoTaskCoordinator _autoTaskCoordinator;
    private readonly ManagedScriptLibraryService _managedScriptLibraryService;
    private readonly CollectionScriptSubscriptionService _collectionScriptSubscriptionService;
    private readonly GoldBalloonScriptSubscriptionService _goldBalloonScriptSubscriptionService;
    private readonly BlackBorderScriptSubscriptionService _blackBorderScriptSubscriptionService;
    private readonly Dictionary<string, TaskRuntimeWindow> _runtimeWindowsByTaskKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskRuntimeWindowViewModel> _runtimeViewModelsByTaskKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextFileEditorWindow> _textEditorWindowsByKey = new(StringComparer.OrdinalIgnoreCase);
    private bool _isImportingAssets;

    private string _runningTaskKey = string.Empty;

    public AutoTasksPageViewModel()
        : this(
            LocalizationService.Instance,
            AppDialogService.Instance,
            AutoTaskCoordinator.Instance,
            ManagedScriptLibraryService.Instance,
            CollectionScriptSubscriptionService.Instance,
            GoldBalloonScriptSubscriptionService.Instance,
            BlackBorderScriptSubscriptionService.Instance)
    {
    }

    internal AutoTasksPageViewModel(
        LocalizationService localizationService,
        AppDialogService appDialogService,
        AutoTaskCoordinator autoTaskCoordinator,
        ManagedScriptLibraryService managedScriptLibraryService,
        CollectionScriptSubscriptionService collectionScriptSubscriptionService,
        GoldBalloonScriptSubscriptionService goldBalloonScriptSubscriptionService,
        BlackBorderScriptSubscriptionService blackBorderScriptSubscriptionService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _appDialogService = appDialogService ?? throw new ArgumentNullException(nameof(appDialogService));
        _importProgressDialogService = ImportProgressDialogService.Instance;
        _autoTaskCoordinator = autoTaskCoordinator ?? throw new ArgumentNullException(nameof(autoTaskCoordinator));
        _managedScriptLibraryService = managedScriptLibraryService ?? throw new ArgumentNullException(nameof(managedScriptLibraryService));
        _collectionScriptSubscriptionService = collectionScriptSubscriptionService ?? throw new ArgumentNullException(nameof(collectionScriptSubscriptionService));
        _goldBalloonScriptSubscriptionService = goldBalloonScriptSubscriptionService ?? throw new ArgumentNullException(nameof(goldBalloonScriptSubscriptionService));
        _blackBorderScriptSubscriptionService = blackBorderScriptSubscriptionService ?? throw new ArgumentNullException(nameof(blackBorderScriptSubscriptionService));

        Tasks =
        [
            CreateCollectionTask(),
            CreateGoldBalloonTask(),
            CreateBlackBorderTask(),
            CreateLoopStageTask(),
            CreateOdysseyTask()
        ];

        foreach (var task in Tasks)
        {
            task.PropertyChanged += OnTaskPropertyChanged;
        }

        ToggleTaskCommand = new RelayCommand<AutoTaskConfig?>(ToggleTask);
        OpenTutorialCommand = new RelayCommand<AutoTaskConfig?>(OpenTutorial);
        OpenTaskScriptConfigCommand = new RelayCommand<AutoTaskConfig?>(OpenTaskScriptConfig);
        ExportSubscriptionCommand = new RelayCommand<AutoTaskConfig?>(ExportSubscription);
        ImportSubscriptionPackageCommand = new AsyncRelayCommand(ImportSubscriptionPackageAsync, CanImportAssets);
        ImportSingleScriptCommand = new AsyncRelayCommand(ImportSingleScriptAsync, CanImportAssets);

        _localizationService.LanguageChanged += (_, _) => RefreshLocalizedContent();
        RefreshLocalizedContent();
    }

    public ObservableCollection<AutoTaskConfig> Tasks { get; }

    public IRelayCommand<AutoTaskConfig?> ToggleTaskCommand { get; }

    public IRelayCommand<AutoTaskConfig?> OpenTutorialCommand { get; }

    public IRelayCommand<AutoTaskConfig?> OpenTaskScriptConfigCommand { get; }

    public IRelayCommand<AutoTaskConfig?> ExportSubscriptionCommand { get; }

    public IAsyncRelayCommand ImportSubscriptionPackageCommand { get; }

    public IAsyncRelayCommand ImportSingleScriptCommand { get; }

    public string PageTitle => _localizationService.T("Tasks.PageTitle");

    public string PageDescription => _localizationService.T("Tasks.PageDescription");

    public string ImportButtonText => _localizationService.T("Tasks.Import");

    public string ImportSubscriptionPackageText => _localizationService.T("Tasks.Import.SubscriptionPackage");

    public string ImportSingleScriptText => _localizationService.T("Tasks.Import.SingleScript");

    public string ImportProgressTitle => _localizationService.T("Tasks.Dialog.ImportProgress.Title");

    public string ImportSubscriptionProgressInitialMessage => _localizationService.T("Tasks.Dialog.ImportProgress.SubscriptionMessage.Initial");

    public string TutorialLinkText => _localizationService.T("Tasks.Tutorial");

    public string OperationIntervalLabel => _localizationService.T("Tasks.OperationInterval");

    public string OperationIntervalDescription => _localizationService.T("Tasks.OperationIntervalDesc");

    public string CollectionOptionLabel => _localizationService.T("Tasks.CollectionOptionLabel");

    public string CollectionOptionDescription => _localizationService.T("Tasks.CollectionOptionDescription");

    public string BlackBorderCategoryLabel => _localizationService.T("Tasks.BlackBorderCategoryLabel");

    public string BlackBorderCategoryDescription => _localizationService.T("Tasks.BlackBorderCategoryDescription");

    public string MapLabel => _localizationService.T("Tasks.MapLabel");

    public string MapDescription => _localizationService.T("Tasks.MapDescription");

    public string ScriptConfigLabel => _localizationService.T("Tasks.ScriptConfigLabel");

    public string ScriptConfigDescription => _localizationService.T("Tasks.ScriptConfigDescription");

    public string ScriptConfigButtonText => _localizationService.T("Tasks.ScriptConfigButton");

    public string ScriptIdLabel => _localizationService.T("Tasks.ScriptIdLabel");

    public string ScriptIdDescription => _localizationService.T("Tasks.ScriptIdDescription");

    public string OdysseyScriptIdsLabel => _localizationService.T("Tasks.OdysseyScriptIdsLabel");

    public string OdysseyScriptIdsDescription => _localizationService.T("Tasks.OdysseyScriptIdsDescription");

    public string CollectionSubscriptionLabel => _localizationService.T("Tasks.CollectionSubscriptionLabel");

    public string CollectionSubscriptionDescription => _localizationService.T("Tasks.CollectionSubscriptionDescription");

    public string GoldBalloonSubscriptionLabel => _localizationService.T("Tasks.GoldBalloonSubscriptionLabel");

    public string GoldBalloonSubscriptionDescription => _localizationService.T("Tasks.GoldBalloonSubscriptionDescription");

    public string BlackBorderSubscriptionLabel => _localizationService.T("Tasks.BlackBorderSubscriptionLabel");

    public string BlackBorderSubscriptionDescription => _localizationService.T("Tasks.BlackBorderSubscriptionDescription");

    public string SubscriptionExportButtonText => _localizationService.T("Tasks.Subscription.Export");

    public string SubscriptionExportTypeLabel => _localizationService.T("Tasks.BlackBorderSubscription.ExportTypeLabel");

    public string SubscriptionExportMapLabel => _localizationService.T("Tasks.BlackBorderSubscription.ExportMapLabel");

    private AutoTaskConfig CreateCollectionTask()
    {
        return new AutoTaskConfig
        {
            Key = AutoTaskKind.Collection.ToKey(),
            ShowStageTargetConfiguration = false,
            ShowCollectionVariantConfiguration = true,
            ShowScriptConfiguration = true,
            ShowCollectionSubscriptionActions = true
        };
    }

    private AutoTaskConfig CreateGoldBalloonTask()
    {
        return new AutoTaskConfig
        {
            Key = AutoTaskKind.GoldBalloon.ToKey(),
            ShowStageTargetConfiguration = false,
            ShowScriptConfiguration = true,
            ShowCollectionSubscriptionActions = true
        };
    }

    private AutoTaskConfig CreateBlackBorderTask()
    {
        return new AutoTaskConfig
        {
            Key = AutoTaskKind.BlackBorder.ToKey(),
            ShowStageTargetConfiguration = true,
            ShowBlackBorderVariantConfiguration = true,
            ShowScriptConfiguration = true,
            ShowBlackBorderSubscriptionActions = true
        };
    }

    private AutoTaskConfig CreateLoopStageTask()
    {
        return new AutoTaskConfig
        {
            Key = AutoTaskKind.LoopStage.ToKey(),
            ShowStageTargetConfiguration = false,
            ShowScriptIdConfiguration = true
        };
    }

    private AutoTaskConfig CreateOdysseyTask()
    {
        return new AutoTaskConfig
        {
            Key = AutoTaskKind.Odyssey.ToKey(),
            ShowStageTargetConfiguration = false,
            ShowOdysseyScriptIdConfiguration = true
        };
    }

    private void ToggleTask(AutoTaskConfig? task)
    {
        if (task is null)
        {
            return;
        }

        if (task.IsRunning)
        {
            _ = _autoTaskCoordinator.RequestStop();
            return;
        }

        if (_autoTaskCoordinator.IsRunning)
        {
            ShowDialogByKey("Tasks.Dialog.TaskRunning.Title", "Tasks.Dialog.TaskRunning.Message");
            return;
        }

        OpenTaskRuntimeWindow(task);
    }

    private void OpenTaskRuntimeWindow(AutoTaskConfig task)
    {
        var runtimeWindow = EnsureRuntimeWindow(task);

        RunOnUiThread(() =>
        {
            if (!runtimeWindow.IsVisible)
            {
                runtimeWindow.Show();
            }

            runtimeWindow.Activate();
        });
    }

    private AutoTaskRequest BuildRequest(AutoTaskConfig task)
    {
        var taskKind = ResolveTaskKind(task.Key);
        return taskKind switch
        {
            AutoTaskKind.Collection => BuildCollectionRequest(task),
            AutoTaskKind.GoldBalloon => BuildGoldBalloonRequest(task),
            AutoTaskKind.BlackBorder => BuildBlackBorderRequest(task),
            AutoTaskKind.LoopStage => BuildLoopStageRequest(task),
            AutoTaskKind.Odyssey => BuildOdysseyRequest(task),
            _ => throw new InvalidOperationException($"Auto task '{taskKind}' is not supported on this page.")
        };
    }

    private AutoTaskRequest BuildCollectionRequest(AutoTaskConfig task)
    {
        var selectedVariantKey = task.SelectedVariantOption?.Code ?? ManagedScriptCollectionModeCatalog.Modes[0].Key;
        return new AutoTaskRequest
        {
            Kind = AutoTaskKind.Collection,
            StageTarget = CollectionPlaceholderStageTarget,
            VariantKey = selectedVariantKey,
            OperationIntervalMs = Math.Max(20, task.OperationIntervalMs),
            Key = task.Key
        };
    }

    private AutoTaskRequest BuildGoldBalloonRequest(AutoTaskConfig task)
    {
        return new AutoTaskRequest
        {
            Kind = AutoTaskKind.GoldBalloon,
            StageTarget = GoldBalloonPlaceholderStageTarget,
            OperationIntervalMs = Math.Max(20, task.OperationIntervalMs),
            Key = task.Key
        };
    }

    private AutoTaskRequest BuildBlackBorderRequest(AutoTaskConfig task)
    {
        var category = ParseEnumOrDefault(task.SelectedVariantOption?.Code, BlackBorderMapCategory.Beginner);
        var scopeTargets = BuildBlackBorderScopeTargets(category, task.SelectedMapOption?.Code);
        var firstTarget = scopeTargets.FirstOrDefault()
            ?? new StageEntryTarget
            {
                Map = BlackBorderTaskCatalog.GetMapsByCategory(category).First().Type,
                Difficulty = StageDifficulty.Easy,
                Mode = StageMode.Standard
            };
        var scopeMapCode = string.IsNullOrWhiteSpace(task.SelectedMapOption?.Code)
            ? AllMapsOptionCode
            : task.SelectedMapOption.Code;

        return new AutoTaskRequest
        {
            Kind = AutoTaskKind.BlackBorder,
            StageTarget = firstTarget,
            VariantKey = BuildBlackBorderScopeVariantKey(category, scopeMapCode),
            OperationIntervalMs = Math.Max(20, task.OperationIntervalMs),
            Key = task.Key
        };
    }

    private AutoTaskRequest BuildLoopStageRequest(AutoTaskConfig task)
    {
        var scriptId = task.ScriptId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(scriptId))
        {
            throw new InvalidOperationException("Loop-stage script ID is required.");
        }

        if (!_managedScriptLibraryService.TryGetManagedScriptFilePath(scriptId, out var filePath))
        {
            throw new InvalidOperationException($"Managed script ID '{scriptId}' was not found.");
        }

        return new AutoTaskRequest
        {
            Kind = AutoTaskKind.LoopStage,
            StageTarget = LoopStagePlaceholderStageTarget,
            OperationIntervalMs = Math.Max(20, task.OperationIntervalMs),
            PreferredScriptPath = filePath,
            VariantKey = scriptId,
            Key = task.Key
        };
    }

    private AutoTaskRequest BuildOdysseyRequest(AutoTaskConfig task)
    {
        var rawScriptIds = new[]
        {
            task.ScriptId,
            task.ScriptId2,
            task.ScriptId3,
            task.ScriptId4,
            task.ScriptId5
        };

        var scriptIds = new List<string>(rawScriptIds.Length);
        var encounteredGap = false;
        foreach (var rawScriptId in rawScriptIds)
        {
            var scriptId = rawScriptId?.Trim() ?? string.Empty;
            if (scriptId.Length == 0)
            {
                encounteredGap = true;
                continue;
            }

            if (encounteredGap)
            {
                throw new InvalidOperationException("Odyssey script IDs must be filled in order without gaps.");
            }

            scriptIds.Add(scriptId);
        }

        if (scriptIds.Count < 3)
        {
            throw new InvalidOperationException("Odyssey requires at least 3 script IDs.");
        }

        var filePaths = new List<string>(scriptIds.Count);
        foreach (var scriptId in scriptIds)
        {
            if (!_managedScriptLibraryService.TryGetManagedScriptFilePath(scriptId, out var filePath))
            {
                throw new InvalidOperationException($"Managed script ID '{scriptId}' was not found.");
            }

            filePaths.Add(filePath);
        }

        return new AutoTaskRequest
        {
            Kind = AutoTaskKind.Odyssey,
            StageTarget = OdysseyPlaceholderStageTarget,
            OperationIntervalMs = Math.Max(20, task.OperationIntervalMs),
            PreferredScriptPaths = filePaths,
            VariantKey = string.Join(";", scriptIds),
            Key = task.Key
        };
    }

    private void OpenTutorial(AutoTaskConfig? task)
    {
        if (task is null || string.IsNullOrWhiteSpace(task.TutorialUrl))
        {
            return;
        }

        _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = task.TutorialUrl,
            UseShellExecute = true
        });
    }

    private void RefreshLocalizedContent()
    {
        var collectionVariantOptions = BuildCollectionVariantOptions();
        var blackBorderCategoryOptions = BuildBlackBorderCategoryOptions();
        var blackBorderExportTypeOptions = BuildBlackBorderExportTypeOptions();

        foreach (var task in Tasks)
        {
            task.Title = _localizationService.T($"Tasks.{task.Key}.Title");
            task.Description = _localizationService.T($"Tasks.{task.Key}.Description");
            task.TutorialUrl = _localizationService.T("Tasks.TutorialUrl");
            task.RunningButtonText = task.IsRunning ? _localizationService.T("Tasks.Stop") : _localizationService.T("Tasks.Start");
            task.SubscriptionLabel = ResolveSubscriptionLabel(task.Key);
            task.SubscriptionDescription = ResolveSubscriptionDescription(task.Key);

            if (string.Equals(task.Key, AutoTaskKind.Collection.ToKey(), StringComparison.OrdinalIgnoreCase))
            {
                var previousVariantCode = task.SelectedVariantOption?.Code;
                task.VariantOptions = new ObservableCollection<LanguageOption>(collectionVariantOptions);
                task.SelectedVariantOption = SelectOption(task.VariantOptions, previousVariantCode)
                    ?? task.VariantOptions.FirstOrDefault();
            }
            else if (string.Equals(task.Key, AutoTaskKind.BlackBorder.ToKey(), StringComparison.OrdinalIgnoreCase))
            {
                var previousCategoryCode = task.SelectedVariantOption?.Code;
                var previousMapCode = task.SelectedMapOption?.Code;
                var previousExportTypeCode = task.SelectedSubscriptionExportTypeOption?.Code;
                var previousSubscriptionMapCode = task.SelectedSubscriptionMapOption?.Code;

                task.VariantOptions = new ObservableCollection<LanguageOption>(blackBorderCategoryOptions);
                task.SelectedVariantOption = SelectOption(task.VariantOptions, previousCategoryCode)
                    ?? task.VariantOptions.FirstOrDefault();
                RefreshBlackBorderMapOptions(task, previousMapCode);

                task.SubscriptionExportTypeOptions = new ObservableCollection<LanguageOption>(blackBorderExportTypeOptions);
                task.SelectedSubscriptionExportTypeOption = SelectOption(task.SubscriptionExportTypeOptions, previousExportTypeCode)
                    ?? task.SubscriptionExportTypeOptions.FirstOrDefault();
                task.SubscriptionMapOptions = new ObservableCollection<LanguageOption>(BuildAllMapOptions());
                task.SelectedSubscriptionMapOption = SelectOption(task.SubscriptionMapOptions, previousSubscriptionMapCode)
                    ?? task.SubscriptionMapOptions.FirstOrDefault();
                UpdateSubscriptionMapSelectionVisibility(task);
            }

            if (_runtimeViewModelsByTaskKey.TryGetValue(task.Key, out var runtimeViewModel))
            {
                runtimeViewModel.UpdateTaskMetadata(task.Title, BuildTaskRuntimeSummary(task));
            }
        }

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageDescription));
        OnPropertyChanged(nameof(ImportButtonText));
        OnPropertyChanged(nameof(ImportSubscriptionPackageText));
        OnPropertyChanged(nameof(ImportSingleScriptText));
        OnPropertyChanged(nameof(ImportProgressTitle));
        OnPropertyChanged(nameof(ImportSubscriptionProgressInitialMessage));
        OnPropertyChanged(nameof(TutorialLinkText));
        OnPropertyChanged(nameof(OperationIntervalLabel));
        OnPropertyChanged(nameof(OperationIntervalDescription));
        OnPropertyChanged(nameof(CollectionOptionLabel));
        OnPropertyChanged(nameof(CollectionOptionDescription));
        OnPropertyChanged(nameof(BlackBorderCategoryLabel));
        OnPropertyChanged(nameof(BlackBorderCategoryDescription));
        OnPropertyChanged(nameof(MapLabel));
        OnPropertyChanged(nameof(MapDescription));
        OnPropertyChanged(nameof(ScriptConfigLabel));
        OnPropertyChanged(nameof(ScriptConfigDescription));
        OnPropertyChanged(nameof(ScriptConfigButtonText));
        OnPropertyChanged(nameof(ScriptIdLabel));
        OnPropertyChanged(nameof(ScriptIdDescription));
        OnPropertyChanged(nameof(OdysseyScriptIdsLabel));
        OnPropertyChanged(nameof(OdysseyScriptIdsDescription));
        OnPropertyChanged(nameof(CollectionSubscriptionLabel));
        OnPropertyChanged(nameof(CollectionSubscriptionDescription));
        OnPropertyChanged(nameof(GoldBalloonSubscriptionLabel));
        OnPropertyChanged(nameof(GoldBalloonSubscriptionDescription));
        OnPropertyChanged(nameof(BlackBorderSubscriptionLabel));
        OnPropertyChanged(nameof(BlackBorderSubscriptionDescription));
        OnPropertyChanged(nameof(SubscriptionExportButtonText));
        OnPropertyChanged(nameof(SubscriptionExportTypeLabel));
        OnPropertyChanged(nameof(SubscriptionExportMapLabel));
    }

    private void OnTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not AutoTaskConfig task)
        {
            return;
        }

        if (string.Equals(task.Key, AutoTaskKind.BlackBorder.ToKey(), StringComparison.OrdinalIgnoreCase))
        {
            switch (e.PropertyName)
            {
                case nameof(AutoTaskConfig.SelectedVariantOption):
                    RefreshBlackBorderMapOptions(task, task.SelectedMapOption?.Code);
                    UpdateRuntimeSummary(task);
                    break;
                case nameof(AutoTaskConfig.SelectedMapOption):
                    UpdateRuntimeSummary(task);
                    break;
                case nameof(AutoTaskConfig.SelectedSubscriptionExportTypeOption):
                    UpdateSubscriptionMapSelectionVisibility(task);
                    break;
            }
        }
        else if (string.Equals(task.Key, AutoTaskKind.Collection.ToKey(), StringComparison.OrdinalIgnoreCase) &&
                 e.PropertyName == nameof(AutoTaskConfig.SelectedVariantOption))
        {
            UpdateRuntimeSummary(task);
        }
        else if (string.Equals(task.Key, AutoTaskKind.LoopStage.ToKey(), StringComparison.OrdinalIgnoreCase) &&
                 e.PropertyName == nameof(AutoTaskConfig.ScriptId))
        {
            UpdateRuntimeSummary(task);
        }
        else if (string.Equals(task.Key, AutoTaskKind.Odyssey.ToKey(), StringComparison.OrdinalIgnoreCase) &&
                 e.PropertyName is nameof(AutoTaskConfig.ScriptId)
                     or nameof(AutoTaskConfig.ScriptId2)
                     or nameof(AutoTaskConfig.ScriptId3)
                     or nameof(AutoTaskConfig.ScriptId4)
                     or nameof(AutoTaskConfig.ScriptId5))
        {
            UpdateRuntimeSummary(task);
        }
    }

    private IReadOnlyList<LanguageOption> BuildCollectionVariantOptions()
    {
        return
        [
            new LanguageOption
            {
                Code = "simple",
                DisplayName = _localizationService.T("Tasks.CollectionOption.Simple")
            },
            new LanguageOption
            {
                Code = "double-cash",
                DisplayName = _localizationService.T("Tasks.CollectionOption.DoubleCash")
            },
            new LanguageOption
            {
                Code = "fast-track",
                DisplayName = _localizationService.T("Tasks.CollectionOption.FastTrack")
            },
            new LanguageOption
            {
                Code = "double-cash-fast-track",
                DisplayName = _localizationService.T("Tasks.CollectionOption.DoubleCashFastTrack")
            }
        ];
    }

    private IReadOnlyList<LanguageOption> BuildBlackBorderCategoryOptions()
    {
        return BlackBorderTaskCatalog.Categories
            .Select(category => new LanguageOption
            {
                Code = category.ToString(),
                DisplayName = _localizationService.T($"Tasks.BlackBorderCategory.{category}")
            })
            .ToList();
    }

    private IReadOnlyList<LanguageOption> BuildBlackBorderExportTypeOptions()
    {
        return
        [
            CreateExportTypeOption(BlackBorderSubscriptionExportType.BeginnerMaps),
            CreateExportTypeOption(BlackBorderSubscriptionExportType.IntermediateMaps),
            CreateExportTypeOption(BlackBorderSubscriptionExportType.AdvancedMaps),
            CreateExportTypeOption(BlackBorderSubscriptionExportType.ExpertMaps),
            CreateExportTypeOption(BlackBorderSubscriptionExportType.SingleMap)
        ];
    }

    private LanguageOption CreateExportTypeOption(BlackBorderSubscriptionExportType exportType)
    {
        return new LanguageOption
        {
            Code = exportType.ToString(),
            DisplayName = _localizationService.T($"Tasks.BlackBorderSubscription.ExportType.{exportType}")
        };
    }

    private IReadOnlyList<LanguageOption> BuildAllMapOptions()
    {
        return GameElementCatalog.Maps
            .Select(map => new LanguageOption
            {
                Code = map.Type.ToString(),
                DisplayName = GameElementCatalog.GetMapDisplayName(map.Type)
            })
            .ToList();
    }

    private void RefreshBlackBorderMapOptions(AutoTaskConfig task, string? previousMapCode = null)
    {
        var category = ParseEnumOrDefault(task.SelectedVariantOption?.Code, BlackBorderMapCategory.Beginner);
        var options = new List<LanguageOption>
        {
            new()
            {
                Code = AllMapsOptionCode,
                DisplayName = _localizationService.T("Tasks.Map.All")
            }
        };
        options.AddRange(BlackBorderTaskCatalog.GetMapsByCategory(category)
            .Select(map => new LanguageOption
            {
                Code = map.Type.ToString(),
                DisplayName = GameElementCatalog.GetMapDisplayName(map.Type)
            }));

        task.MapOptions = new ObservableCollection<LanguageOption>(options);
        task.SelectedMapOption = SelectOption(task.MapOptions, previousMapCode) ?? task.MapOptions.FirstOrDefault();
    }

    private void UpdateSubscriptionMapSelectionVisibility(AutoTaskConfig task)
    {
        var exportType = ParseEnumOrDefault(
            task.SelectedSubscriptionExportTypeOption?.Code,
            BlackBorderSubscriptionExportType.BeginnerMaps);
        task.ShowSubscriptionMapSelection = exportType == BlackBorderSubscriptionExportType.SingleMap;
    }

    private void SetRunningTask(string taskKey)
    {
        _runningTaskKey = taskKey ?? string.Empty;
        foreach (var task in Tasks)
        {
            task.IsRunning = string.Equals(task.Key, _runningTaskKey, StringComparison.OrdinalIgnoreCase);
            task.RunningButtonText = task.IsRunning ? _localizationService.T("Tasks.Stop") : _localizationService.T("Tasks.Start");
        }
    }

    private void ClearRunningTask()
    {
        _runningTaskKey = string.Empty;
        foreach (var task in Tasks)
        {
            task.IsRunning = false;
            task.RunningButtonText = _localizationService.T("Tasks.Start");
        }
    }

    private void ShowDialog(string titleKey, string message)
    {
        RunOnUiThread(() => _appDialogService.Show(new AppDialogRequest
        {
            Title = _localizationService.T(titleKey),
            Message = message,
            PrimaryButtonText = _localizationService.T("Tasks.Dialog.Ok")
        }));
    }

    private void ShowDialogByKey(string titleKey, string messageKey)
    {
        ShowDialog(titleKey, _localizationService.T(messageKey));
    }

    private void OpenTaskScriptConfig(AutoTaskConfig? task)
    {
        if (task is null)
        {
            return;
        }

        var taskKind = ResolveTaskKind(task.Key);
        var editorKey = $"{taskKind}:bindings";
        if (_textEditorWindowsByKey.TryGetValue(editorKey, out var existingWindow))
        {
            RunOnUiThread(() =>
            {
                if (!existingWindow.IsVisible)
                {
                    existingWindow.Show();
                }

                existingWindow.Activate();
            });
            return;
        }

        try
        {
            var filePath = _managedScriptLibraryService.EnsureTaskBindingTemplate(taskKind);
            var viewModel = new TextFileEditorWindowViewModel(
                _localizationService,
                _appDialogService,
                filePath,
                () =>
                {
                    if (_textEditorWindowsByKey.TryGetValue(editorKey, out var editorWindow))
                    {
                        editorWindow.Close();
                    }
                });
            var window = new TextFileEditorWindow(viewModel);

            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(x => x.IsActive)
                ?? Application.Current?.MainWindow;
            if (owner is not null && !ReferenceEquals(owner, window))
            {
                window.Owner = owner;
            }

            window.Closed += OnTextEditorWindowClosed;
            _textEditorWindowsByKey[editorKey] = window;

            RunOnUiThread(() =>
            {
                window.Show();
                window.Activate();
            });
        }
        catch (Exception ex)
        {
            ShowDialog("Tasks.Dialog.ScriptConfigOpenFailed.Title", ex.Message);
        }
    }

    private void ExportSubscription(AutoTaskConfig? task)
    {
        if (task is null)
        {
            return;
        }

        var taskKind = ResolveTaskKind(task.Key);
        switch (taskKind)
        {
            case AutoTaskKind.Collection:
                ExportCollectionSubscription();
                return;
            case AutoTaskKind.GoldBalloon:
                ExportGoldBalloonSubscription();
                return;
            case AutoTaskKind.BlackBorder:
                ExportBlackBorderSubscription(task);
                return;
            default:
                return;
        }
    }

    private void ExportCollectionSubscription()
    {
        var dialog = new SaveFileDialog
        {
            Filter = _localizationService.T("Tasks.Subscription.ExportFilter"),
            FileName = "collection-subscription.btdsub"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _collectionScriptSubscriptionService.Export(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowDialog("Tasks.Dialog.CollectionSubscriptionExportFailed.Title", ex.Message);
        }
    }

    private void ExportGoldBalloonSubscription()
    {
        var dialog = new SaveFileDialog
        {
            Filter = _localizationService.T("Tasks.Subscription.ExportFilter"),
            FileName = "goldballoon-subscription.btdsub"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _goldBalloonScriptSubscriptionService.Export(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowDialog("Tasks.Dialog.GoldBalloonSubscriptionExportFailed.Title", ex.Message);
        }
    }

    private void ExportBlackBorderSubscription(AutoTaskConfig task)
    {
        var exportType = ParseEnumOrDefault(
            task.SelectedSubscriptionExportTypeOption?.Code,
            BlackBorderSubscriptionExportType.BeginnerMaps);
        var map = exportType == BlackBorderSubscriptionExportType.SingleMap
            ? ParseEnumOrDefault(task.SelectedSubscriptionMapOption?.Code, GameMapType.MonkeyMeadow)
            : (GameMapType?)null;

        var fileName = exportType == BlackBorderSubscriptionExportType.SingleMap && map is not null
            ? $"blackborder-{map.Value}.btdsub"
            : $"blackborder-{exportType}.btdsub";
        var dialog = new SaveFileDialog
        {
            Filter = _localizationService.T("Tasks.Subscription.ExportFilter"),
            FileName = fileName
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var descriptor = new BlackBorderSubscriptionDescriptor
            {
                ExportType = exportType,
                Category = TryMapExportTypeToCategory(exportType),
                Map = map
            };
            _blackBorderScriptSubscriptionService.Export(dialog.FileName, descriptor);
        }
        catch (Exception ex)
        {
            ShowDialog("Tasks.Dialog.BlackBorderSubscriptionExportFailed.Title", ex.Message);
        }
    }

    private async Task ImportSubscriptionPackageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = _localizationService.T("Tasks.Subscription.ImportFilter"),
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var progressDialog = _importProgressDialogService.Show(new ImportProgressDialogRequest
        {
            Title = ImportProgressTitle,
            Message = ImportSubscriptionProgressInitialMessage,
            ProgressValue = 0,
            ProgressMaximum = 1,
            IsIndeterminate = true
        });

        await ExecuteAssetImportAsync(async () =>
        {
            var progress = new Progress<SubscriptionImportProgress>(report =>
            {
                var total = Math.Max(report.TotalScriptCount, 1);
                progressDialog.UpdateProgress(report.ProcessedScriptCount, total, isIndeterminate: false);
                progressDialog.UpdateMessage(string.Format(
                    _localizationService.T("Tasks.Dialog.ImportProgress.SubscriptionMessage"),
                    report.ProcessedScriptCount,
                    report.TotalScriptCount));
            });

            await Task.Run(() =>
            {
                if (BlackBorderScriptSubscriptionService.IsBlackBorderSubscriptionPackage(dialog.FileName))
                {
                    _blackBorderScriptSubscriptionService.Import(dialog.FileName, progress);
                    return;
                }

                if (GoldBalloonScriptSubscriptionService.IsGoldBalloonSubscriptionPackage(dialog.FileName))
                {
                    _goldBalloonScriptSubscriptionService.Import(dialog.FileName, progress);
                    return;
                }

                if (CollectionScriptSubscriptionService.IsCollectionSubscriptionPackage(dialog.FileName))
                {
                    _collectionScriptSubscriptionService.Import(dialog.FileName, progress);
                    return;
                }

                throw new InvalidDataException(_localizationService.T("Tasks.Dialog.UnsupportedSubscription.Message"));
            }).ConfigureAwait(false);
        }, "Tasks.Dialog.SubscriptionImportFailed.Title");
    }

    private async Task ImportSingleScriptAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = _localizationService.T("Tasks.Script.ImportFilter"),
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ExecuteAssetImportAsync(async () =>
        {
            var extension = Path.GetExtension(dialog.FileName);
            if (!string.Equals(extension, ".btd", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".btd6", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(_localizationService.T("Tasks.Dialog.UnsupportedScript.Message"));
            }

            var imported = await Task.Run(() => _managedScriptLibraryService.ImportScript(dialog.FileName)).ConfigureAwait(false);
            var loopStageTask = FindTask(AutoTaskKind.LoopStage);
            if (loopStageTask is null)
            {
                throw new InvalidOperationException("Loop-stage task was not found.");
            }

            RunOnUiThread(() => loopStageTask.ScriptId = imported.ScriptId);
        }, "Tasks.Dialog.ScriptImportFailed.Title");
    }

    private async Task ExecuteAssetImportAsync(Func<Task> importAction, string errorTitleKey)
    {
        ArgumentNullException.ThrowIfNull(importAction);

        SetIsImportingAssets(true);
        try
        {
            await importAction().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowDialog(errorTitleKey, ex.Message);
        }
        finally
        {
            RunOnUiThread(() => SetIsImportingAssets(false));
        }
    }

    private bool CanImportAssets()
    {
        return !_isImportingAssets;
    }

    private void SetIsImportingAssets(bool value)
    {
        if (_isImportingAssets == value)
        {
            return;
        }

        _isImportingAssets = value;
        ImportSubscriptionPackageCommand.NotifyCanExecuteChanged();
        ImportSingleScriptCommand.NotifyCanExecuteChanged();
    }

    private string ResolveSubscriptionLabel(string taskKey)
    {
        return string.Equals(taskKey, AutoTaskKind.GoldBalloon.ToKey(), StringComparison.OrdinalIgnoreCase)
            ? GoldBalloonSubscriptionLabel
            : CollectionSubscriptionLabel;
    }

    private string ResolveSubscriptionDescription(string taskKey)
    {
        return string.Equals(taskKey, AutoTaskKind.GoldBalloon.ToKey(), StringComparison.OrdinalIgnoreCase)
            ? GoldBalloonSubscriptionDescription
            : CollectionSubscriptionDescription;
    }

    private AutoTaskConfig? FindTask(AutoTaskKind taskKind)
    {
        var key = taskKind.ToKey();
        return Tasks.FirstOrDefault(task => string.Equals(task.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private TaskRuntimeWindow EnsureRuntimeWindow(AutoTaskConfig task)
    {
        if (_runtimeWindowsByTaskKey.TryGetValue(task.Key, out var existingWindow))
        {
            return existingWindow;
        }

        var runtimeViewModel = new TaskRuntimeWindowViewModel(
            _localizationService,
            task.Title,
            BuildTaskRuntimeSummary(task),
            task.OperationIntervalMs,
            viewModel => StartTaskExecutionAsync(task, viewModel),
            () => _autoTaskCoordinator.RequestStop());
        var runtimeWindow = new TaskRuntimeWindow(runtimeViewModel);

        var owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(x => x.IsActive)
            ?? Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, runtimeWindow))
        {
            runtimeWindow.Owner = owner;
        }

        runtimeWindow.Closed += OnTaskRuntimeWindowClosed;
        _runtimeWindowsByTaskKey[task.Key] = runtimeWindow;
        _runtimeViewModelsByTaskKey[task.Key] = runtimeViewModel;
        return runtimeWindow;
    }

    private async Task StartTaskExecutionAsync(
        AutoTaskConfig task,
        TaskRuntimeWindowViewModel runtimeViewModel)
    {
        EventHandler<AutoTaskProgressSnapshot>? progressHandler = (_, snapshot) =>
            runtimeViewModel.PostProgressSnapshot(snapshot);

        try
        {
            task.OperationIntervalMs = runtimeViewModel.OperationIntervalMs;
            var request = BuildRequest(task);

            _autoTaskCoordinator.ProgressChanged += progressHandler;
            RunOnUiThread(() => SetRunningTask(task.Key));

            var result = await _autoTaskCoordinator.ExecuteAsync(request).ConfigureAwait(false);
            RunOnUiThread(() => runtimeViewModel.ApplyResult(result));

            if (result.Status == AutoTaskExecutionStatus.Failed)
            {
                ShowDialog(
                    "Tasks.Dialog.ExecutionFailed.Title",
                    result.Failure?.Message ?? result.Exception?.Message ?? "Auto task execution failed.");
            }
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => runtimeViewModel.ApplyUnexpectedException(ex));
            ShowDialog("Tasks.Dialog.StartFailed.Title", ex.Message);
        }
        finally
        {
            _autoTaskCoordinator.ProgressChanged -= progressHandler;
            RunOnUiThread(ClearRunningTask);
        }
    }

    private string BuildTaskRuntimeSummary(AutoTaskConfig task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            parts.Add(task.Description);
        }

        if (task.ShowCollectionVariantConfiguration && !string.IsNullOrWhiteSpace(task.SelectedVariantOption?.DisplayName))
        {
            parts.Add($"{CollectionOptionLabel}: {task.SelectedVariantOption!.DisplayName}");
        }

        if (task.ShowBlackBorderVariantConfiguration)
        {
            if (!string.IsNullOrWhiteSpace(task.SelectedVariantOption?.DisplayName))
            {
                parts.Add($"{BlackBorderCategoryLabel}: {task.SelectedVariantOption.DisplayName}");
            }

            if (!string.IsNullOrWhiteSpace(task.SelectedMapOption?.DisplayName))
            {
                parts.Add($"{MapLabel}: {task.SelectedMapOption.DisplayName}");
            }
        }

        if (task.ShowScriptIdConfiguration && !string.IsNullOrWhiteSpace(task.ScriptId))
        {
            parts.Add($"{ScriptIdLabel}: {task.ScriptId.Trim()}");
        }

        if (task.ShowOdysseyScriptIdConfiguration)
        {
            var scriptIds = GetOdysseyScriptIds(task);
            if (scriptIds.Count > 0)
            {
                parts.Add($"{OdysseyScriptIdsLabel}: {string.Join(", ", scriptIds)}");
            }
        }

        return string.Join(" | ", parts);
    }

    private void UpdateRuntimeSummary(AutoTaskConfig task)
    {
        if (_runtimeViewModelsByTaskKey.TryGetValue(task.Key, out var runtimeViewModel))
        {
            runtimeViewModel.UpdateTaskMetadata(task.Title, BuildTaskRuntimeSummary(task));
        }
    }

    private void OnTaskRuntimeWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not TaskRuntimeWindow window)
        {
            return;
        }

        window.Closed -= OnTaskRuntimeWindowClosed;

        var entry = _runtimeWindowsByTaskKey
            .FirstOrDefault(pair => ReferenceEquals(pair.Value, window));
        if (string.IsNullOrWhiteSpace(entry.Key))
        {
            return;
        }

        _runtimeWindowsByTaskKey.Remove(entry.Key);
        _runtimeViewModelsByTaskKey.Remove(entry.Key);
    }

    private void OnTextEditorWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not TextFileEditorWindow window)
        {
            return;
        }

        window.Closed -= OnTextEditorWindowClosed;

        var entry = _textEditorWindowsByKey
            .FirstOrDefault(pair => ReferenceEquals(pair.Value, window));
        if (string.IsNullOrWhiteSpace(entry.Key))
        {
            return;
        }

        _textEditorWindowsByKey.Remove(entry.Key);
    }

    private static AutoTaskKind ResolveTaskKind(string taskKey)
    {
        return taskKey?.Trim().ToLowerInvariant() switch
        {
            "collection" => AutoTaskKind.Collection,
            "goldballoon" => AutoTaskKind.GoldBalloon,
            "blackborder" => AutoTaskKind.BlackBorder,
            "loopstage" => AutoTaskKind.LoopStage,
            "odyssey" => AutoTaskKind.Odyssey,
            "race" => AutoTaskKind.Race,
            "custom" => AutoTaskKind.Custom,
            _ => throw new InvalidOperationException($"Unsupported auto task key '{taskKey}'.")
        };
    }

    private static IReadOnlyList<string> GetOdysseyScriptIds(AutoTaskConfig task)
    {
        return new[]
        {
            task.ScriptId?.Trim() ?? string.Empty,
            task.ScriptId2?.Trim() ?? string.Empty,
            task.ScriptId3?.Trim() ?? string.Empty,
            task.ScriptId4?.Trim() ?? string.Empty,
            task.ScriptId5?.Trim() ?? string.Empty
        }
        .Where(static scriptId => !string.IsNullOrWhiteSpace(scriptId))
        .ToList();
    }

    private static LanguageOption? SelectOption(IEnumerable<LanguageOption> options, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return options.FirstOrDefault(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    private static TEnum ParseEnumOrDefault<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private static BlackBorderMapCategory? TryMapExportTypeToCategory(BlackBorderSubscriptionExportType exportType)
    {
        return exportType switch
        {
            BlackBorderSubscriptionExportType.BeginnerMaps => BlackBorderMapCategory.Beginner,
            BlackBorderSubscriptionExportType.IntermediateMaps => BlackBorderMapCategory.Intermediate,
            BlackBorderSubscriptionExportType.AdvancedMaps => BlackBorderMapCategory.Advanced,
            BlackBorderSubscriptionExportType.ExpertMaps => BlackBorderMapCategory.Expert,
            _ => null
        };
    }

    private static IReadOnlyList<StageEntryTarget> BuildBlackBorderScopeTargets(
        BlackBorderMapCategory category,
        string? selectedMapCode)
    {
        var maps = selectedMapCode is not null &&
                   !string.Equals(selectedMapCode, AllMapsOptionCode, StringComparison.OrdinalIgnoreCase) &&
                   Enum.TryParse<GameMapType>(selectedMapCode, ignoreCase: true, out var selectedMap)
            ? GameElementCatalog.Maps.Where(map => map.Type == selectedMap)
            : BlackBorderTaskCatalog.GetMapsByCategory(category);

        return maps
            .SelectMany(
                map => BlackBorderTaskCatalog.Difficulties,
                (map, difficulty) => new { map.Type, Difficulty = difficulty })
            .SelectMany(
                item => BlackBorderTaskCatalog.GetModesForDifficulty(item.Difficulty),
                (item, mode) => new StageEntryTarget
                {
                    Map = item.Type,
                    Difficulty = item.Difficulty,
                    Mode = mode
                })
            .ToList();
    }

    private static string BuildBlackBorderScopeVariantKey(BlackBorderMapCategory category, string mapCode)
    {
        return $"category={category};map={mapCode}";
    }

    private static void RunOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }
}
