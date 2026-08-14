using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterBTD.ViewModels;

public sealed class TaskRuntimeWindowViewModel : ObservableObject, IDisposable
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
    private readonly DispatcherTimer _runtimeDurationTimer;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TaskRuntimeWindowViewModel, Task> _startExecutionAsync;
    private readonly Action _requestStop;
    private readonly object _progressDispatchSync = new();

    private string _windowTitle = string.Empty;
    private string _taskDisplayName = string.Empty;
    private string _taskSummaryText = string.Empty;
    private string _statusText = string.Empty;
    private string _currentPhaseText = string.Empty;
    private string _currentActivityText = string.Empty;
    private string _activityStatusText = string.Empty;
    private string _completedStageCountText = string.Empty;
    private string _runtimeDurationText = string.Empty;
    private bool _isRunning;
    private bool _isStopRequested;
    private bool _isRuntimeDurationActive;
    private bool _isDisposed;
    private int _operationIntervalMs = 200;
    private DateTimeOffset? _runtimeStartedAt;
    private ScriptExecutionStepItem? _focusedStep;
    private ScriptExecutionStepItem? _selectedStep;
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
        Action requestStop,
        TimeProvider? timeProvider = null)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _startExecutionAsync = startExecutionAsync ?? throw new ArgumentNullException(nameof(startExecutionAsync));
        _requestStop = requestStop ?? throw new ArgumentNullException(nameof(requestStop));
        _operationIntervalMs = Math.Clamp(operationIntervalMs, 20, 5000);
        _runtimeDurationTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _runtimeDurationTimer.Tick += OnRuntimeDurationTimerTick;

        UpdateTaskMetadata(taskDisplayName, taskSummaryText);
        _statusText = BuildUiInfoText(new AutoTaskProgressSnapshot());
        _currentPhaseText = _localizationService.T("Tasks.Runtime.NotStarted");
        _currentActivityText = _localizationService.T("Tasks.Runtime.NotStarted");
        _activityStatusText = _localizationService.T("Tasks.Runtime.NotStarted");
        var runtimeMetricPlaceholder = _localizationService.T("Tasks.Runtime.Metrics.NotStarted");
        _completedStageCountText = runtimeMetricPlaceholder;
        _runtimeDurationText = runtimeMetricPlaceholder;

        StartCommand = new AsyncRelayCommand(StartExecutionAsync, CanStartExecution);
        StopCommand = new RelayCommand(StopExecution, CanStopExecution);

        SetSequencePlaceholder(_localizationService.T("Tasks.Runtime.ScriptPending"));
    }

    public ObservableCollection<ScriptExecutionStepItem> Steps { get; } = [];

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

    public string CurrentPhaseText
    {
        get => _currentPhaseText;
        private set => SetProperty(ref _currentPhaseText, value);
    }

    public string CurrentActivityText
    {
        get => _currentActivityText;
        private set => SetProperty(ref _currentActivityText, value);
    }

    public string ActivityStatusText
    {
        get => _activityStatusText;
        private set => SetProperty(ref _activityStatusText, value);
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

    public string ActivityTitle => _localizationService.T("Tasks.Runtime.ActivityTitle");

    public string CurrentPhaseLabel => _localizationService.T("Tasks.Runtime.Activity.Phase");

    public string CurrentActivityLabel => _localizationService.T("Tasks.Runtime.Activity.Action");

    public string ActivityStatusLabel => _localizationService.T("Tasks.Runtime.Activity.Status");

    public string CompletedStageCountTitle => _localizationService.T("Tasks.Runtime.Metrics.CompletedStages");

    public string RuntimeDurationTitle => _localizationService.T("Tasks.Runtime.Metrics.RuntimeDuration");

    public string CompletedStageCountText
    {
        get => _completedStageCountText;
        private set => SetProperty(ref _completedStageCountText, value);
    }

    public string RuntimeDurationText
    {
        get => _runtimeDurationText;
        private set => SetProperty(ref _runtimeDurationText, value);
    }

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

        var isActiveRunState = snapshot.RunState is AutoTaskRunState.Running
            or AutoTaskRunState.PauseRequested
            or AutoTaskRunState.Paused;
        IsRunning = isActiveRunState;
        UpdateRuntimeMetrics(snapshot, isActiveRunState);

        EnsureSequence(snapshot);
        UpdateSequenceProgress(snapshot);
        StatusText = BuildUiInfoText(snapshot);
        UpdateActivityDisplay(snapshot);
    }

    public void ApplyResult(AutoTaskExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        StopAcceptingProgressSnapshots();
        IsRunning = false;
        CompletedStageCountText = result.FinalProgress.CompletedStageCount.ToString(CultureInfo.InvariantCulture);
        StopRuntimeDuration();
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

        StatusText = BuildUiInfoText(result.FinalProgress);
        CurrentPhaseText = LocalizeEnum("Tasks.Runtime.Phase", result.FinalProgress.Phase);
        CurrentActivityText = LocalizeEnum("Tasks.Runtime.Activity", result.FinalProgress.CurrentActivity);
        ActivityStatusText = finalText;
    }

    public void ApplyUnexpectedException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        StopAcceptingProgressSnapshots();
        IsRunning = false;
        StopRuntimeDuration();
        CurrentPhaseText = LocalizeEnum("Tasks.Runtime.Phase", AutoTaskPhase.Failed);
        CurrentActivityText = LocalizeEnum("Tasks.Runtime.Activity", AutoTaskActivityKind.None);
        ActivityStatusText = string.Format(_localizationService.T("Tasks.Runtime.UnexpectedError"), exception.Message);
    }

    public void HandleWindowClosing()
    {
        if (IsRunning)
        {
            StopExecution();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopRuntimeDuration();
        _runtimeDurationTimer.Tick -= OnRuntimeDurationTimerTick;
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
        return IsRunning && !_isStopRequested;
    }

    private void StopExecution()
    {
        if (_isStopRequested)
        {
            return;
        }

        _isStopRequested = true;
        StopCommand.NotifyCanExecuteChanged();
        StopRuntimeDuration();
        _requestStop();
        ActivityStatusText = _localizationService.T("Tasks.Runtime.StopRequested");
    }

    private void PrepareForExecution()
    {
        BeginAcceptingProgressSnapshots();
        _isStopRequested = false;
        _sequenceSignature = string.Empty;
        CompletedStageCountText = _localizationService.T("Tasks.Runtime.Metrics.NotStarted");
        BeginRuntimeDuration();
        SetSequencePlaceholder(_localizationService.T("Tasks.Runtime.ScriptPending"));
        FocusedStep = Steps.FirstOrDefault();
        IsRunning = true;
        StatusText = BuildUiInfoText(new AutoTaskProgressSnapshot());
        CurrentPhaseText = LocalizeEnum("Tasks.Runtime.Phase", AutoTaskPhase.PreparingStage);
        CurrentActivityText = LocalizeEnum("Tasks.Runtime.Activity", AutoTaskActivityKind.Preparing);
        ActivityStatusText = _localizationService.T("Tasks.Runtime.Starting");
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

    private void UpdateRuntimeMetrics(AutoTaskProgressSnapshot snapshot, bool isActiveRunState)
    {
        CompletedStageCountText = snapshot.CompletedStageCount.ToString(CultureInfo.InvariantCulture);

        if (!isActiveRunState)
        {
            StopRuntimeDuration();
            return;
        }

        if (!_isRuntimeDurationActive || _isStopRequested)
        {
            return;
        }

        if (snapshot.StartedAt != default)
        {
            _runtimeStartedAt = snapshot.StartedAt;
        }

        UpdateRuntimeDuration();
    }

    private void BeginRuntimeDuration()
    {
        _runtimeStartedAt = _timeProvider.GetUtcNow();
        _isRuntimeDurationActive = true;
        RuntimeDurationText = FormatRuntimeDuration(TimeSpan.Zero);
        _runtimeDurationTimer.Start();
    }

    private void StopRuntimeDuration()
    {
        if (!_isRuntimeDurationActive)
        {
            return;
        }

        UpdateRuntimeDuration();
        _isRuntimeDurationActive = false;
        _runtimeDurationTimer.Stop();
    }

    private void OnRuntimeDurationTimerTick(object? sender, EventArgs e)
    {
        UpdateRuntimeDuration();
    }

    private void UpdateRuntimeDuration()
    {
        if (!_isRuntimeDurationActive || _runtimeStartedAt is not { } startedAt)
        {
            return;
        }

        var elapsed = _timeProvider.GetUtcNow() - startedAt;
        RuntimeDurationText = FormatRuntimeDuration(elapsed);
    }

    private static string FormatRuntimeDuration(TimeSpan duration)
    {
        var totalSeconds = Math.Max(0L, (long)Math.Floor(duration.TotalSeconds));
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;

        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, seconds);
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

    private string BuildUiInfoText(AutoTaskProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder();
        var uiSnapshot = snapshot.LastUiSnapshot;
        var uiState = uiSnapshot?.State ?? snapshot.CurrentUiState;
        AppendStatusLine(
            builder,
            _localizationService.T("Tasks.Runtime.Status.UiState"),
            LocalizeEnum("CaptureTest.GameUiState", uiState));

        if (uiSnapshot?.State == GameUiStateId.InLevel && uiSnapshot.StageState is { } stageState)
        {
            if (stageState.Gold.HasValue)
            {
                AppendStatusLine(
                    builder,
                    _localizationService.T("Tasks.Runtime.Status.Gold"),
                    stageState.Gold.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (stageState.Round.HasValue)
            {
                AppendStatusLine(
                    builder,
                    _localizationService.T("Tasks.Runtime.Status.Round"),
                    stageState.Round.Value.ToString(CultureInfo.InvariantCulture));
            }

            AppendUpgradePanel(
                builder,
                _localizationService.T("Tasks.Runtime.Status.LeftUpgrade"),
                stageState.LeftUpgradePanel);
            AppendUpgradePanel(
                builder,
                _localizationService.T("Tasks.Runtime.Status.RightUpgrade"),
                stageState.RightUpgradePanel);
        }
        else if (uiSnapshot?.State == GameUiStateId.MapSearch)
        {
            var recognizedMap = ResolveRecognizedMapText(uiSnapshot);
            if (!string.IsNullOrWhiteSpace(recognizedMap))
            {
                AppendStatusLine(builder, _localizationService.T("Tasks.Runtime.Status.Map"), recognizedMap);
            }
        }

        return builder.ToString().TrimEnd();
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

    private void UpdateActivityDisplay(AutoTaskProgressSnapshot snapshot)
    {
        CurrentPhaseText = LocalizeEnum("Tasks.Runtime.Phase", snapshot.Phase);
        CurrentActivityText = LocalizeEnum("Tasks.Runtime.Activity", snapshot.CurrentActivity);

        if (snapshot.ConsecutiveNavigationFailures > 0)
        {
            ActivityStatusText = string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.T("Tasks.Runtime.ActivityStatus.NavigationRetry"),
                snapshot.ConsecutiveNavigationFailures);
            return;
        }

        ActivityStatusText = snapshot.RunState switch
        {
            AutoTaskRunState.Idle => _localizationService.T("Tasks.Runtime.ActivityStatus.Idle"),
            AutoTaskRunState.Running when snapshot.CurrentActivity == AutoTaskActivityKind.Waiting =>
                _localizationService.T("Tasks.Runtime.ActivityStatus.Waiting"),
            AutoTaskRunState.Running => _localizationService.T("Tasks.Runtime.ActivityStatus.Running"),
            AutoTaskRunState.PauseRequested => _localizationService.T("Tasks.Runtime.ActivityStatus.PauseRequested"),
            AutoTaskRunState.Paused => _localizationService.T("Tasks.Runtime.ActivityStatus.Paused"),
            AutoTaskRunState.Completed => _localizationService.T("Tasks.Runtime.ActivityStatus.Completed"),
            AutoTaskRunState.Cancelled => _localizationService.T("Tasks.Runtime.ActivityStatus.Cancelled"),
            AutoTaskRunState.Failed => _localizationService.T("Tasks.Runtime.ActivityStatus.Failed"),
            _ => _localizationService.T("Tasks.Runtime.Unknown")
        };
    }

    private string ResolveRecognizedMapText(GameUiSnapshot snapshot)
    {
        if (snapshot.Facts.TryGetValue(MapSearchFlowState.CollectionMapFact, out var rawMap) && rawMap is GameMapType map)
        {
            return GameElementCatalog.GetMapDisplayName(map);
        }

        if (snapshot.Facts.TryGetValue(MapSearchFlowState.GoldBalloonMapFact, out rawMap) && rawMap is GameMapType goldBalloonMap)
        {
            return GameElementCatalog.GetMapDisplayName(goldBalloonMap);
        }

        return string.Empty;
    }

    private void AppendUpgradePanel(
        StringBuilder builder,
        string label,
        GameStageUpgradePanelState panel)
    {
        if (panel.IsVisible != true)
        {
            return;
        }

        var levels = new List<string>(3);
        AppendUpgradeLevel(levels, "CaptureTest.PathTop", panel.TopPathLevel);
        AppendUpgradeLevel(levels, "CaptureTest.PathMiddle", panel.MiddlePathLevel);
        AppendUpgradeLevel(levels, "CaptureTest.PathBottom", panel.BottomPathLevel);

        if (levels.Count > 0)
        {
            AppendStatusLine(builder, label, string.Join(" / ", levels));
        }
    }

    private void AppendUpgradeLevel(List<string> levels, string labelKey, int? level)
    {
        if (level.HasValue)
        {
            levels.Add($"{_localizationService.T(labelKey)} {level.Value.ToString(CultureInfo.InvariantCulture)}");
        }
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

    private string LocalizeEnum<T>(string keyPrefix, T value) where T : struct, Enum
    {
        var key = $"{keyPrefix}.{value}";
        var localized = _localizationService.T(key);
        return string.Equals(localized, key, StringComparison.Ordinal)
            ? value.ToString()
            : localized;
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
