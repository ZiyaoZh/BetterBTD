using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterBTD.ViewModels;

public sealed class TaskRuntimeWindowViewModel : ObservableObject
{
    private static readonly Brush PendingStateBrush = CreateBrush("#FF8A93A6");
    private static readonly Brush RunningStateBrush = CreateBrush("#FF57A6FF");
    private static readonly Brush CompletedStateBrush = CreateBrush("#FF49B675");
    private static readonly Brush FailedStateBrush = CreateBrush("#FFE35D6A");
    private static readonly Brush CancelledStateBrush = CreateBrush("#FFE8A344");
    private static readonly Brush PendingCardBrush = CreateBrush("#1A8A93A6");
    private static readonly Brush RunningCardBrush = CreateBrush("#1857A6FF");
    private static readonly Brush CompletedCardBrush = CreateBrush("#1649B675");
    private static readonly Brush FailedCardBrush = CreateBrush("#1AE35D6A");
    private static readonly Brush CancelledCardBrush = CreateBrush("#1AE8A344");
    private static readonly Brush PendingTitleBrush = CreateBrush("#FF8F9CAF");
    private static readonly Brush RunningTitleBrush = CreateBrush("#FFF3F6FB");
    private static readonly Brush CompletedTitleBrush = CreateBrush("#FFD6DDEA");
    private static readonly Brush FailedTitleBrush = CreateBrush("#FFFFB2BA");
    private static readonly Brush CancelledTitleBrush = CreateBrush("#FFFFD39A");

    private readonly LocalizationService _localizationService;
    private readonly Dispatcher _dispatcher;
    private readonly Func<TaskRuntimeWindowViewModel, Task> _startExecutionAsync;
    private readonly Action _requestStop;
    private readonly object _progressDispatchSync = new();

    private string _windowTitle = string.Empty;
    private string _taskDisplayName = string.Empty;
    private string _taskSummaryText = string.Empty;
    private string _statusText = string.Empty;
    private string _logText = string.Empty;
    private bool _isRunning;
    private int _operationIntervalMs = 200;
    private ScriptExecutionStepItem? _focusedStep;
    private ScriptExecutionStepItem? _selectedStep;
    private string _lastLogSignature = string.Empty;
    private string _sequenceSignature = string.Empty;
    private AutoTaskProgressSnapshot? _pendingProgressSnapshot;
    private bool _isProgressFlushScheduled;
    private bool _acceptProgressSnapshots;

    public TaskRuntimeWindowViewModel(
        LocalizationService localizationService,
        string taskDisplayName,
        string taskSummaryText,
        int operationIntervalMs,
        Func<TaskRuntimeWindowViewModel, Task> startExecutionAsync,
        Action requestStop)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _startExecutionAsync = startExecutionAsync ?? throw new ArgumentNullException(nameof(startExecutionAsync));
        _requestStop = requestStop ?? throw new ArgumentNullException(nameof(requestStop));
        _operationIntervalMs = Math.Clamp(operationIntervalMs, 20, 5000);

        UpdateTaskMetadata(taskDisplayName, taskSummaryText);
        _statusText = _localizationService.T("Tasks.Runtime.NotStarted");

        StartCommand = new AsyncRelayCommand(StartExecutionAsync, CanStartExecution);
        StopCommand = new RelayCommand(StopExecution, CanStopExecution);

        SetSequencePlaceholder(_localizationService.T("Tasks.Runtime.ScriptPending"));
    }

    public ObservableCollection<ScriptExecutionStepItem> Steps { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    public IAsyncRelayCommand StartCommand { get; }

    public IRelayCommand StopCommand { get; }

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public string TaskDisplayName
    {
        get => _taskDisplayName;
        private set => SetProperty(ref _taskDisplayName, value);
    }

    public string TaskSummaryText
    {
        get => _taskSummaryText;
        private set => SetProperty(ref _taskSummaryText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
            {
                return;
            }

            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanEditOperationInterval));
        }
    }

    public int OperationIntervalMs
    {
        get => _operationIntervalMs;
        set => SetProperty(ref _operationIntervalMs, Math.Clamp(value, 20, 5000));
    }

    public bool CanEditOperationInterval => !IsRunning;

    public ScriptExecutionStepItem? FocusedStep
    {
        get => _focusedStep;
        private set => SetProperty(ref _focusedStep, value);
    }

    public ScriptExecutionStepItem? SelectedStep
    {
        get => _selectedStep;
        set => SetProperty(ref _selectedStep, value);
    }

    public string SequenceTitle => _localizationService.T("Tasks.Runtime.Sequence");

    public string StatusTitle => _localizationService.T("Tasks.Runtime.Status");

    public string OutputTitle => _localizationService.T("Tasks.Runtime.Log");

    public string OperationIntervalLabel => _localizationService.T("Tasks.Runtime.OperationInterval");

    public string StartText => _localizationService.T("Tasks.Start");

    public string StopText => _localizationService.T("Tasks.Stop");

    public void UpdateTaskMetadata(string taskDisplayName, string taskSummaryText)
    {
        TaskDisplayName = string.IsNullOrWhiteSpace(taskDisplayName)
            ? _localizationService.T("Tasks.Runtime.UnknownTask")
            : taskDisplayName;
        TaskSummaryText = taskSummaryText?.Trim() ?? string.Empty;
        WindowTitle = $"{_localizationService.T("Tasks.Runtime.WindowTitle")} - {TaskDisplayName}";
    }

    public void PostProgressSnapshot(AutoTaskProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var shouldScheduleFlush = false;
        lock (_progressDispatchSync)
        {
            if (!_acceptProgressSnapshots)
            {
                return;
            }

            _pendingProgressSnapshot = snapshot;
            if (_isProgressFlushScheduled)
            {
                return;
            }

            _isProgressFlushScheduled = true;
            shouldScheduleFlush = true;
        }

        if (!shouldScheduleFlush)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            FlushPendingProgressSnapshots();
            return;
        }

        _ = _dispatcher.InvokeAsync(FlushPendingProgressSnapshots, DispatcherPriority.Render);
    }

    public void ApplyProgressSnapshot(AutoTaskProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        IsRunning = snapshot.RunState is AutoTaskRunState.Running
            or AutoTaskRunState.PauseRequested
            or AutoTaskRunState.Paused;

        EnsureSequence(snapshot);
        UpdateSequenceProgress(snapshot);
        StatusText = BuildStatusText(snapshot);
        AppendProgressLog(snapshot);
    }

    public void ApplyResult(AutoTaskExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        StopAcceptingProgressSnapshots();
        IsRunning = false;
        EnsureSequence(result.FinalProgress);
        UpdateSequenceProgress(result.FinalProgress);

        var finalText = result.Status switch
        {
            AutoTaskExecutionStatus.Completed => _localizationService.T("Tasks.Runtime.Completed"),
            AutoTaskExecutionStatus.Cancelled => _localizationService.T("Tasks.Runtime.Cancelled"),
            AutoTaskExecutionStatus.Failed => string.Format(
                _localizationService.T("Tasks.Runtime.Failed"),
                result.Failure?.Message ?? result.Exception?.Message ?? _localizationService.T("Tasks.Runtime.UnknownError")),
            _ => _localizationService.T("Tasks.Runtime.UnknownResult")
        };

        StatusText = BuildStatusText(result.FinalProgress, finalText);
        AppendLog(finalText);
    }

    public void ApplyUnexpectedException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        StopAcceptingProgressSnapshots();
        IsRunning = false;
        StatusText = string.Format(_localizationService.T("Tasks.Runtime.UnexpectedError"), exception.Message);
        AppendLog(StatusText);
    }

    public void HandleWindowClosing()
    {
        if (IsRunning)
        {
            StopExecution();
        }
    }

    private async Task StartExecutionAsync()
    {
        PrepareForExecution();

        try
        {
            await _startExecutionAsync(this);
        }
        catch (Exception ex)
        {
            ApplyUnexpectedException(ex);
        }
    }

    private bool CanStartExecution()
    {
        return !IsRunning;
    }

    private bool CanStopExecution()
    {
        return IsRunning;
    }

    private void StopExecution()
    {
        _requestStop();
        StatusText = _localizationService.T("Tasks.Runtime.StopRequested");
        AppendLog(StatusText);
    }

    private void PrepareForExecution()
    {
        BeginAcceptingProgressSnapshots();
        _lastLogSignature = string.Empty;
        _sequenceSignature = string.Empty;
        LogLines.Clear();
        LogText = string.Empty;
        SetSequencePlaceholder(_localizationService.T("Tasks.Runtime.ScriptPending"));
        FocusedStep = Steps.FirstOrDefault();
        IsRunning = true;
        StatusText = _localizationService.T("Tasks.Runtime.Starting");
        AppendLog(StatusText);
    }

    private void BeginAcceptingProgressSnapshots()
    {
        lock (_progressDispatchSync)
        {
            _acceptProgressSnapshots = true;
            _pendingProgressSnapshot = null;
            _isProgressFlushScheduled = false;
        }
    }

    private void StopAcceptingProgressSnapshots()
    {
        lock (_progressDispatchSync)
        {
            _acceptProgressSnapshots = false;
            _pendingProgressSnapshot = null;
            _isProgressFlushScheduled = false;
        }
    }

    private void FlushPendingProgressSnapshots()
    {
        while (true)
        {
            AutoTaskProgressSnapshot? snapshot;
            lock (_progressDispatchSync)
            {
                if (!_acceptProgressSnapshots)
                {
                    _pendingProgressSnapshot = null;
                    _isProgressFlushScheduled = false;
                    return;
                }

                snapshot = _pendingProgressSnapshot;
                _pendingProgressSnapshot = null;
                if (snapshot is null)
                {
                    _isProgressFlushScheduled = false;
                    return;
                }
            }

            ApplyProgressSnapshot(snapshot);
        }
    }

    private void EnsureSequence(AutoTaskProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.ActiveScriptSteps.Count == 0)
        {
            if (Steps.Count == 0 || !string.Equals(_sequenceSignature, "placeholder", StringComparison.Ordinal))
            {
                SetSequencePlaceholder(_localizationService.T("Tasks.Runtime.ScriptPending"));
            }

            return;
        }

        var signature = $"{snapshot.ActiveScriptPath}\n{string.Join("\n", snapshot.ActiveScriptSteps)}";
        if (string.Equals(signature, _sequenceSignature, StringComparison.Ordinal))
        {
            return;
        }

        _sequenceSignature = signature;
        Steps.Clear();

        for (var index = 0; index < snapshot.ActiveScriptSteps.Count; index++)
        {
            var title = snapshot.ActiveScriptSteps[index];
            Steps.Add(new ScriptExecutionStepItem(index, title, title));
        }

        SetAllStepsToPending();
        FocusedStep = ResolveFocusedStep(snapshot.ActiveScriptProgress?.CurrentStepIndex ?? -1);
    }

    private void SetSequencePlaceholder(string title)
    {
        _sequenceSignature = "placeholder";
        Steps.Clear();
        var placeholder = new ScriptExecutionStepItem(0, title, title);
        Steps.Add(placeholder);
        ApplyStepVisual(
            placeholder,
            _localizationService.T("Tasks.Runtime.Step.Pending"),
            PendingStateBrush,
            PendingCardBrush,
            isCurrent: false,
            PendingTitleBrush);
    }

    private void SetAllStepsToPending()
    {
        foreach (var step in Steps)
        {
            ApplyStepVisual(
                step,
                _localizationService.T("Tasks.Runtime.Step.Pending"),
                PendingStateBrush,
                PendingCardBrush,
                isCurrent: false,
                PendingTitleBrush);
        }
    }

    private void UpdateSequenceProgress(AutoTaskProgressSnapshot snapshot)
    {
        if (string.Equals(_sequenceSignature, "placeholder", StringComparison.Ordinal))
        {
            return;
        }

        if (snapshot.ActiveScriptProgress is null)
        {
            SetAllStepsToPending();
            FocusedStep = Steps.FirstOrDefault();
            return;
        }

        var progress = snapshot.ActiveScriptProgress;
        for (var index = 0; index < Steps.Count; index++)
        {
            if (index <= progress.LastCompletedStepIndex)
            {
                ApplyStepVisual(
                    Steps[index],
                    _localizationService.T("Tasks.Runtime.Step.Completed"),
                    CompletedStateBrush,
                    CompletedCardBrush,
                    isCurrent: false,
                    CompletedTitleBrush);
                continue;
            }

            if (index == progress.CurrentStepIndex)
            {
                ApplyStepVisual(
                    Steps[index],
                    ResolveRunningStepState(progress.RunState),
                    RunningStateBrush,
                    RunningCardBrush,
                    isCurrent: true,
                    RunningTitleBrush);
                continue;
            }

            if (snapshot.RunState == AutoTaskRunState.Completed)
            {
                ApplyStepVisual(
                    Steps[index],
                    _localizationService.T("Tasks.Runtime.Step.Completed"),
                    CompletedStateBrush,
                    CompletedCardBrush,
                    isCurrent: false,
                    CompletedTitleBrush);
                continue;
            }

            ApplyStepVisual(
                Steps[index],
                _localizationService.T("Tasks.Runtime.Step.Pending"),
                PendingStateBrush,
                PendingCardBrush,
                isCurrent: false,
                PendingTitleBrush);
        }

        FocusedStep = ResolveFocusedStep(progress.CurrentStepIndex >= 0
            ? progress.CurrentStepIndex
            : progress.LastCompletedStepIndex);
    }

    private string BuildStatusText(AutoTaskProgressSnapshot snapshot, string? finalLineOverride = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder();
        AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.RunState"), Humanize(snapshot.RunState));
        AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Phase"), Humanize(snapshot.Phase));
        AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Loop"), snapshot.LoopIteration.ToString());
        AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Checkpoint"), string.IsNullOrWhiteSpace(snapshot.CurrentCheckpoint)
            ? _localizationService.T("Tasks.Runtime.Unknown")
            : snapshot.CurrentCheckpoint);

        if (snapshot.LastUiSnapshot is { } uiSnapshot)
        {
            AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.UiState"), Humanize(uiSnapshot.State));
            AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.UiSummary"), string.IsNullOrWhiteSpace(uiSnapshot.Summary)
                ? _localizationService.T("Tasks.Runtime.Unknown")
                : uiSnapshot.Summary);
            AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Confidence"), $"{uiSnapshot.Confidence:P0}");

            var recognizedMap = ResolveRecognizedMapText(uiSnapshot);
            if (!string.IsNullOrWhiteSpace(recognizedMap))
            {
                AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Map"), recognizedMap);
            }

            if (uiSnapshot.StageState is { } stageState)
            {
                if (stageState.IsInLevel.HasValue)
                {
                    AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.InLevel"), FormatBool(stageState.IsInLevel.Value));
                }

                if (stageState.Gold.HasValue)
                {
                    AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Gold"), stageState.Gold.Value.ToString());
                }

                if (stageState.Round.HasValue)
                {
                    AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Round"), stageState.Round.Value.ToString());
                }

                if (!string.IsNullOrWhiteSpace(stageState.StageTarget))
                {
                    AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.StageTarget"), stageState.StageTarget);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ActiveScriptDisplayName))
        {
            AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Script"), snapshot.ActiveScriptDisplayName);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ActiveScriptPath))
        {
            AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.ScriptPath"), snapshot.ActiveScriptPath);
        }

        if (snapshot.ActiveScriptProgress is { } scriptProgress &&
            TryResolveScriptStepTitle(snapshot, scriptProgress, out var stepTitle))
        {
            AppendStatusLine(
                builder,
                _localizationService.T("Tasks.Runtime.Status.ScriptStep"),
                $"#{scriptProgress.CurrentStepIndex + 1:000} {stepTitle}");
        }

        if (snapshot.ConsecutiveNavigationFailures > 0)
        {
            AppendStatusLine(
                builder,
                _localizationService.T("Tasks.Runtime.Status.NavigationFailures"),
                snapshot.ConsecutiveNavigationFailures.ToString());
        }

        AppendStatusLine(
            builder,
            _localizationService.T("Tasks.Runtime.Status.Message"),
            string.IsNullOrWhiteSpace(finalLineOverride) ? snapshot.Message : finalLineOverride);

        return builder.ToString().TrimEnd();
    }

    private void AppendProgressLog(AutoTaskProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var signature = string.Join("|",
            snapshot.RunState,
            snapshot.Phase,
            snapshot.CurrentUiState,
            snapshot.CurrentCheckpoint,
            snapshot.ConsecutiveNavigationFailures,
            snapshot.ActiveScriptPath,
            snapshot.Message);

        if (string.Equals(signature, _lastLogSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastLogSignature = signature;

        var parts = new List<string>
        {
            Humanize(snapshot.Phase)
        };

        if (snapshot.CurrentUiState != GameUiStateId.Unknown)
        {
            parts.Add(Humanize(snapshot.CurrentUiState));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ActiveScriptDisplayName))
        {
            parts.Add($"{_localizationService.T("Tasks.Runtime.Status.Script")}: {snapshot.ActiveScriptDisplayName}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Message))
        {
            parts.Add(snapshot.Message);
        }

        if (snapshot.ConsecutiveNavigationFailures > 0)
        {
            parts.Add($"{_localizationService.T("Tasks.Runtime.Status.NavigationFailures")}: {snapshot.ConsecutiveNavigationFailures}");
        }

        AppendLog(string.Join(" | ", parts));
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        LogLines.Add(line);
        LogText = string.Join(Environment.NewLine, LogLines);
    }

    private static void ApplyStepVisual(
        ScriptExecutionStepItem step,
        string stateText,
        Brush stateBrush,
        Brush cardBackgroundBrush,
        bool isCurrent,
        Brush titleBrush)
    {
        step.StateText = stateText;
        step.StateBrush = stateBrush;
        step.CardBorderBrush = stateBrush;
        step.CardBackgroundBrush = cardBackgroundBrush;
        step.IsCurrent = isCurrent;
        step.TitleBrush = titleBrush;
        step.TitleWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private string ResolveRunningStepState(ScriptExecutionRunState runState)
    {
        return runState switch
        {
            ScriptExecutionRunState.PauseRequested => _localizationService.T("Tasks.Runtime.Step.PauseRequested"),
            ScriptExecutionRunState.Paused => _localizationService.T("Tasks.Runtime.Step.Paused"),
            _ => _localizationService.T("Tasks.Runtime.Step.Running")
        };
    }

    private ScriptExecutionStepItem? ResolveFocusedStep(int stepIndex)
    {
        return stepIndex < 0 || stepIndex >= Steps.Count
            ? Steps.FirstOrDefault()
            : Steps[stepIndex];
    }

    private bool TryResolveScriptStepTitle(
        AutoTaskProgressSnapshot taskSnapshot,
        ScriptExecutionProgressSnapshot scriptProgress,
        out string title)
    {
        if (scriptProgress.CurrentStepIndex >= 0 &&
            scriptProgress.CurrentStepIndex < taskSnapshot.ActiveScriptSteps.Count)
        {
            title = taskSnapshot.ActiveScriptSteps[scriptProgress.CurrentStepIndex];
            return true;
        }

        title = string.Empty;
        return false;
    }

    private string ResolveRecognizedMapText(GameUiSnapshot snapshot)
    {
        if (snapshot.Facts.TryGetValue("collectionMap", out var rawMap) && rawMap is GameMapType map)
        {
            return GameElementCatalog.GetMapDisplayName(map);
        }

        return string.Empty;
    }

    private string FormatBool(bool value)
    {
        return value
            ? _localizationService.T("Tasks.Runtime.Yes")
            : _localizationService.T("Tasks.Runtime.No");
    }

    private static void AppendStatusLine(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(value.Trim());
    }

    private static string Humanize<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(
            Regex.Replace(text, "([A-Z])([A-Z][a-z])", "$1 $2"),
            "([a-z0-9])([A-Z])",
            "$1 $2");
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
