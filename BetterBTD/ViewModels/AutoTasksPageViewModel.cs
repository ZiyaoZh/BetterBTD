using System.Collections.ObjectModel;
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

namespace BetterBTD.ViewModels;

public sealed class AutoTasksPageViewModel : ObservableObject
{
    private static readonly StageEntryTarget CollectionPlaceholderStageTarget = new()
    {
        Map = GameMapType.DarkCastle,
        Difficulty = StageDifficulty.Hard,
        Mode = StageMode.CHIMPS
    };

    private readonly LocalizationService _localizationService;
    private readonly AppDialogService _appDialogService;
    private readonly AutoTaskCoordinator _autoTaskCoordinator;
    private readonly Dictionary<string, TaskRuntimeWindow> _runtimeWindowsByTaskKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskRuntimeWindowViewModel> _runtimeViewModelsByTaskKey = new(StringComparer.OrdinalIgnoreCase);

    private string _runningTaskKey = string.Empty;

    public AutoTasksPageViewModel()
        : this(LocalizationService.Instance, AppDialogService.Instance, AutoTaskCoordinator.Instance)
    {
    }

    internal AutoTasksPageViewModel(
        LocalizationService localizationService,
        AppDialogService appDialogService,
        AutoTaskCoordinator autoTaskCoordinator)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _appDialogService = appDialogService ?? throw new ArgumentNullException(nameof(appDialogService));
        _autoTaskCoordinator = autoTaskCoordinator ?? throw new ArgumentNullException(nameof(autoTaskCoordinator));

        Tasks =
        [
            new AutoTaskConfig
            {
                Key = AutoTaskKind.Collection.ToKey(),
                ShowStageTargetConfiguration = false,
                ShowCollectionVariantConfiguration = true
            }
        ];

        ToggleTaskCommand = new RelayCommand<AutoTaskConfig?>(ToggleTask);
        OpenTutorialCommand = new RelayCommand<AutoTaskConfig?>(OpenTutorial);

        _localizationService.LanguageChanged += (_, _) => RefreshLocalizedContent();
        RefreshLocalizedContent();
    }

    public ObservableCollection<AutoTaskConfig> Tasks { get; }

    public IRelayCommand<AutoTaskConfig?> ToggleTaskCommand { get; }

    public IRelayCommand<AutoTaskConfig?> OpenTutorialCommand { get; }

    public string TutorialLinkText => _localizationService.T("Tasks.Tutorial");

    public string OperationIntervalLabel => _localizationService.T("Tasks.OperationInterval");

    public string OperationIntervalDescription => _localizationService.T("Tasks.OperationIntervalDesc");

    public string CollectionOptionLabel => _localizationService.T("Tasks.CollectionOptionLabel");

    public string CollectionOptionDescription => _localizationService.T("Tasks.CollectionOptionDescription");

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

        _ = OpenTaskRuntimeWindowAsync(task);
    }

    private async Task OpenTaskRuntimeWindowAsync(AutoTaskConfig task)
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

        if (runtimeWindow.DataContext is TaskRuntimeWindowViewModel viewModel &&
            viewModel.StartCommand.CanExecute(null))
        {
            await viewModel.StartCommand.ExecuteAsync(null);
        }
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

        foreach (var task in Tasks)
        {
            var previousVariantCode = task.SelectedVariantOption?.Code;

            task.Title = _localizationService.T($"Tasks.{task.Key}.Title");
            task.Description = _localizationService.T($"Tasks.{task.Key}.Description");
            task.TutorialUrl = _localizationService.T("Tasks.TutorialUrl");
            task.RunningButtonText = task.IsRunning ? _localizationService.T("Tasks.Stop") : _localizationService.T("Tasks.Start");
            task.VariantOptions = new ObservableCollection<LanguageOption>(collectionVariantOptions);
            task.SelectedVariantOption = SelectOption(task.VariantOptions, previousVariantCode)
                ?? task.VariantOptions.FirstOrDefault();

            if (_runtimeViewModelsByTaskKey.TryGetValue(task.Key, out var runtimeViewModel))
            {
                runtimeViewModel.UpdateTaskMetadata(task.Title, BuildTaskRuntimeSummary(task));
            }
        }

        OnPropertyChanged(nameof(TutorialLinkText));
        OnPropertyChanged(nameof(OperationIntervalLabel));
        OnPropertyChanged(nameof(OperationIntervalDescription));
        OnPropertyChanged(nameof(CollectionOptionLabel));
        OnPropertyChanged(nameof(CollectionOptionDescription));
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
            viewModel => StartCollectionTaskExecutionAsync(task, viewModel),
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

    private async Task StartCollectionTaskExecutionAsync(
        AutoTaskConfig task,
        TaskRuntimeWindowViewModel runtimeViewModel)
    {
        EventHandler<AutoTaskProgressSnapshot>? progressHandler = (_, snapshot) =>
            runtimeViewModel.PostProgressSnapshot(snapshot);

        try
        {
            task.OperationIntervalMs = runtimeViewModel.OperationIntervalMs;
            var request = BuildCollectionRequest(task);

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

        return string.Join(" | ", parts);
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

    private static LanguageOption? SelectOption(IEnumerable<LanguageOption> options, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return options.FirstOrDefault(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase));
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
