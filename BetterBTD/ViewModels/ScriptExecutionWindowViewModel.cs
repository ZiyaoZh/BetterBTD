using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterBTD.ViewModels;

public sealed class ScriptExecutionWindowViewModel : ObservableObject
{
    private const int MaxDisplayedLogLines = 400;
    private const int MaxRuntimeLogEntriesPerFlush = 32;

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
    private readonly Func<ScriptExecutionWindowViewModel, int, Task> _startExecutionAsync;
    private readonly object _progressDispatchSync = new();
    private readonly object _runtimeLogDispatchSync = new();
    private readonly Action _requestStop;
    private readonly Action<ScriptExecutionWindowSettings>? _persistSettings;
    private readonly Queue<ScriptExecutionRuntimeLogEntry> _pendingRuntimeLogEntries = new();
    private readonly List<ScriptExecutionDisplayLogLine> _displayLogLines = [];

    private string _windowTitle = string.Empty;
    private string _scriptDisplayName = string.Empty;
    private string _scriptSourcePath = string.Empty;
    private string _statusText = string.Empty;
    private string _currentInstructionText = string.Empty;
    private string _logText = string.Empty;
    private bool _isRunning;
    private ScriptExecutionStepItem? _focusedStep;
    private ScriptExecutionStepItem? _selectedStep;
    private ScriptExecutionOperationIntervalStrategyItem? _selectedIntervalStrategy;
    private int _commonOperationIntervalMs = 200;
    private int _activeStartStepIndex;

    private string _lastLoggedSignature = string.Empty;
    private string _runtimeLogFilePath = string.Empty;
    private ScriptExecutionProgressSnapshot? _pendingProgressSnapshot;
    private bool _isProgressFlushScheduled;
    private bool _isRuntimeLogFlushScheduled;
    private bool _acceptProgressSnapshots;
    private int _lastVisualCurrentStepIndex = -1;
    private int _lastVisualLastCompletedStepIndex = -1;
    private bool _hasProgressVisualState;

    public ScriptExecutionWindowViewModel(
        LocalizationService localizationService,
        string scriptDisplayName,
        string scriptSourcePath,
        IReadOnlyList<ScriptInstructionInstance> instructions,
        Func<ScriptExecutionWindowViewModel, int, Task> startExecutionAsync,
        Action requestStop,
        ScriptExecutionWindowSettings? initialSettings = null,
        Action<ScriptExecutionWindowSettings>? persistSettings = null)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _startExecutionAsync = startExecutionAsync ?? throw new ArgumentNullException(nameof(startExecutionAsync));
        _requestStop = requestStop ?? throw new ArgumentNullException(nameof(requestStop));
        _persistSettings = persistSettings;

        _scriptDisplayName = string.IsNullOrWhiteSpace(scriptDisplayName)
            ? _localizationService.T("Editor.Runtime.UntitledScript")
            : scriptDisplayName;
        _scriptSourcePath = string.IsNullOrWhiteSpace(scriptSourcePath)
            ? "[Unsaved Script]"
            : scriptSourcePath;
        _windowTitle = $"{_localizationService.T("Editor.Runtime.WindowTitle")} - {_scriptDisplayName}";
        _statusText = _localizationService.T("Editor.Runtime.NotStarted");
        _currentInstructionText = _localizationService.T("Editor.Runtime.NotStarted");
        IntervalStrategies.Add(new ScriptExecutionOperationIntervalStrategyItem
        {
            Strategy = ScriptExecutionOperationIntervalStrategy.InstructionCustom,
            DisplayName = _localizationService.T("Editor.Runtime.IntervalStrategy.InstructionCustom")
        });
        IntervalStrategies.Add(new ScriptExecutionOperationIntervalStrategyItem
        {
            Strategy = ScriptExecutionOperationIntervalStrategy.CommonOperationInterval,
            DisplayName = _localizationService.T("Editor.Runtime.IntervalStrategy.CommonOperationInterval")
        });

        var settings = initialSettings ?? new ScriptExecutionWindowSettings();
        _commonOperationIntervalMs = ClampCommonOperationInterval(settings.CommonOperationIntervalMs);
        _selectedIntervalStrategy = ResolveIntervalStrategyItem(settings.IntervalStrategy);

        StartCommand = new AsyncRelayCommand(StartExecutionAsync, CanStartExecution);
        StopCommand = new RelayCommand(StopExecution, CanStopExecution);

        ArgumentNullException.ThrowIfNull(instructions);
        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            Steps.Add(new ScriptExecutionStepItem(
                index,
                string.IsNullOrWhiteSpace(instruction.DisplayName) ? instruction.Type.ToString() : instruction.DisplayName,
                string.IsNullOrWhiteSpace(instruction.Description) ? instruction.Type.ToString() : instruction.Description));
        }

        SetAllStepsToPending();
    }

    public ObservableCollection<ScriptExecutionStepItem> Steps { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    public ObservableCollection<ScriptExecutionOperationIntervalStrategyItem> IntervalStrategies { get; } = [];

    public IAsyncRelayCommand StartCommand { get; }

    public IRelayCommand StopCommand { get; }

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public string ScriptDisplayName
    {
        get => _scriptDisplayName;
        private set => SetProperty(ref _scriptDisplayName, value);
    }

    public string ScriptSourcePath
    {
        get => _scriptSourcePath;
        private set => SetProperty(ref _scriptSourcePath, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CurrentInstructionText
    {
        get => _currentInstructionText;
        private set => SetProperty(ref _currentInstructionText, value);
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
        }
    }

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

    public ScriptExecutionOperationIntervalStrategyItem? SelectedIntervalStrategy
    {
        get => _selectedIntervalStrategy;
        set
        {
            if (!SetProperty(ref _selectedIntervalStrategy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedIntervalStrategyValue));
            OnPropertyChanged(nameof(ShowCommonOperationInterval));
            PersistIntervalSettings();
        }
    }

    public ScriptExecutionOperationIntervalStrategy SelectedIntervalStrategyValue =>
        SelectedIntervalStrategy?.Strategy ?? ScriptExecutionOperationIntervalStrategy.InstructionCustom;

    public int CommonOperationIntervalMs
    {
        get => _commonOperationIntervalMs;
        set
        {
            if (SetProperty(ref _commonOperationIntervalMs, ClampCommonOperationInterval(value)))
            {
                PersistIntervalSettings();
            }
        }
    }

    public bool ShowCommonOperationInterval =>
        SelectedIntervalStrategyValue == ScriptExecutionOperationIntervalStrategy.CommonOperationInterval;

    public string SequenceTitle => _localizationService.T("Editor.Runtime.Sequence");

    public string OutputTitle => _localizationService.T("Editor.Runtime.Output");

    public string StatusTitle => _localizationService.T("Editor.Runtime.Status");

    public string SourceLabelText => _localizationService.T("Editor.Runtime.Source");

    public string CurrentInstructionLabel => _localizationService.T("Editor.Runtime.CurrentInstruction");

    public string IntervalStrategyLabel => _localizationService.T("Editor.Runtime.IntervalStrategy");

    public string StartText => _localizationService.T("Editor.Debug.Run");

    public string StopText => _localizationService.T("Editor.Debug.Stop");

    public void MarkStarting(int startStepIndex)
    {
        BeginAcceptingProgressSnapshots();
        _activeStartStepIndex = NormalizeStepIndex(startStepIndex);
        ResetDisplayedLogs();
        SetAllStepsToPending();
        FocusedStep = ResolveFocusedStep(_activeStartStepIndex);
        IsRunning = true;
        StatusText = _localizationService.T("Editor.Runtime.Starting");
        CurrentInstructionText = _localizationService.T("Editor.Runtime.WaitingInstruction");
        AppendLog(StatusText);
        if (_activeStartStepIndex > 0)
        {
            AppendLog(string.Format(
                _localizationService.T("Editor.Runtime.StartingFromStep"),
                _activeStartStepIndex + 1));
        }
    }

    public void PostProgressSnapshot(ScriptExecutionProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        bool shouldScheduleFlush = false;
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

        ScheduleProgressFlush();
    }

    public void PostRuntimeLogEntry(ScriptExecutionRuntimeLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var shouldScheduleFlush = false;
        lock (_runtimeLogDispatchSync)
        {
            _pendingRuntimeLogEntries.Enqueue(entry);
            if (!_isRuntimeLogFlushScheduled)
            {
                _isRuntimeLogFlushScheduled = true;
                shouldScheduleFlush = true;
            }
        }

        if (!shouldScheduleFlush)
        {
            return;
        }

        _ = _dispatcher.InvokeAsync(FlushPendingRuntimeLogEntries, DispatcherPriority.Background);
    }

    public void ApplyProgressSnapshot(ScriptExecutionProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EnsureRuntimeLogPathLogged(snapshot);
        IsRunning = snapshot.RunState is ScriptExecutionRunState.Running
            or ScriptExecutionRunState.PauseRequested
            or ScriptExecutionRunState.Paused;

        UpdateStepsFromProgress(snapshot);
        FocusedStep = ResolveFocusedStep(snapshot.CurrentStepIndex);
        StatusText = BuildStatusText(snapshot);
        CurrentInstructionText = BuildCurrentInstructionText(snapshot);
        AppendProgressLog(snapshot);
    }

    public void ApplyResult(ScriptExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        StopAcceptingProgressSnapshots();
        IsRunning = false;
        UpdateStepsFromResult(result);
        FocusedStep = ResolveFocusedStep(ResolveResultFocusIndex(result));

        var finalText = result.Status switch
        {
            ScriptExecutionStatus.Completed => string.Format(
                _localizationService.T("Editor.Runtime.Completed"),
                result.ExecutedStepCount,
                result.LastCompletedStepIndex + 1),
            ScriptExecutionStatus.Cancelled => string.Format(
                _localizationService.T("Editor.Runtime.Cancelled"),
                result.ExecutedStepCount,
                result.LastCompletedStepIndex + 1),
            ScriptExecutionStatus.Failed => BuildFailureText(result),
            _ => _localizationService.T("Editor.Runtime.UnknownResult")
        };

        StatusText = finalText;
        CurrentInstructionText = finalText;
        AppendLog(finalText);
    }

    public void ApplyUnexpectedException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        StopAcceptingProgressSnapshots();
        IsRunning = false;
        StatusText = string.Format(_localizationService.T("Editor.Runtime.UnexpectedError"), exception.Message);
        CurrentInstructionText = StatusText;
        AppendLog(StatusText);
    }

    public void HandleWindowClosing()
    {
        if (IsRunning)
        {
            StopExecution();
        }
    }

    private string BuildFailureText(ScriptExecutionResult result)
    {
        var failure = result.Failure;
        if (failure is null)
        {
            return string.Format(
                _localizationService.T("Editor.Runtime.FailedSimple"),
                result.Exception?.Message ?? _localizationService.T("Editor.Runtime.UnknownError"));
        }

        return string.Format(
            _localizationService.T("Editor.Runtime.FailedDetailed"),
            failure.StepIndex + 1,
            failure.CommandType,
            failure.Checkpoint,
            failure.Attempt,
            failure.Message);
    }

    private void PersistIntervalSettings()
    {
        _persistSettings?.Invoke(new ScriptExecutionWindowSettings
        {
            IntervalStrategy = SelectedIntervalStrategyValue,
            CommonOperationIntervalMs = CommonOperationIntervalMs
        });
    }

    private void StopExecution()
    {
        _requestStop();
        StatusText = _localizationService.T("Editor.Runtime.StopRequested");
        CurrentInstructionText = StatusText;
        AppendLog(StatusText);
    }

    private async Task StartExecutionAsync()
    {
        await _startExecutionAsync(this, ResolveStartStepIndex());
    }

    private bool CanStartExecution()
    {
        return !IsRunning && Steps.Count > 0;
    }

    private bool CanStopExecution()
    {
        return IsRunning;
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

        lock (_runtimeLogDispatchSync)
        {
            _pendingRuntimeLogEntries.Clear();
            _isRuntimeLogFlushScheduled = false;
        }
    }

    private void FlushPendingProgressSnapshots()
    {
        ScriptExecutionProgressSnapshot? snapshot;
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

        lock (_progressDispatchSync)
        {
            if (!_acceptProgressSnapshots)
            {
                _pendingProgressSnapshot = null;
                _isProgressFlushScheduled = false;
                return;
            }

            if (_pendingProgressSnapshot is null)
            {
                _isProgressFlushScheduled = false;
                return;
            }
        }

        ScheduleProgressFlush();
    }

    private void FlushPendingRuntimeLogEntries()
    {
        var processedCount = 0;
        var changed = false;

        while (processedCount < MaxRuntimeLogEntriesPerFlush)
        {
            ScriptExecutionRuntimeLogEntry? entry = null;
            lock (_runtimeLogDispatchSync)
            {
                if (_pendingRuntimeLogEntries.Count == 0)
                {
                    break;
                }

                entry = _pendingRuntimeLogEntries.Dequeue();
            }

            changed |= ApplyRuntimeLogEntry(entry, rebuildDisplayedLogs: false);
            processedCount++;
        }

        if (changed)
        {
            RebuildDisplayedLogs();
        }

        lock (_runtimeLogDispatchSync)
        {
            if (_pendingRuntimeLogEntries.Count == 0)
            {
                _isRuntimeLogFlushScheduled = false;
                return;
            }
        }

        _ = _dispatcher.InvokeAsync(FlushPendingRuntimeLogEntries, DispatcherPriority.Background);
    }

    private void ScheduleProgressFlush()
    {
        _ = _dispatcher.InvokeAsync(FlushPendingProgressSnapshots, DispatcherPriority.Background);
    }

    private void UpdateStepsFromProgress(ScriptExecutionProgressSnapshot snapshot)
    {
        if (Steps.Count == 0)
        {
            ResetStepProgressTracking();
            return;
        }

        var clampedLastCompletedStepIndex = Math.Clamp(snapshot.LastCompletedStepIndex, -1, Steps.Count - 1);
        var clampedCurrentStepIndex = snapshot.CurrentStepIndex >= 0 && snapshot.CurrentStepIndex < Steps.Count
            ? snapshot.CurrentStepIndex
            : -1;

        if (!_hasProgressVisualState ||
            clampedLastCompletedStepIndex < _lastVisualLastCompletedStepIndex)
        {
            UpdateAllStepsFromProgress(snapshot);
            StoreStepProgressTracking(clampedCurrentStepIndex, clampedLastCompletedStepIndex);
            return;
        }

        ApplyStepVisualFromProgress(_lastVisualCurrentStepIndex, snapshot);

        var firstNewCompletedStepIndex = Math.Max(0, _lastVisualLastCompletedStepIndex + 1);
        for (var index = firstNewCompletedStepIndex; index <= clampedLastCompletedStepIndex; index++)
        {
            if (index != _lastVisualCurrentStepIndex)
            {
                ApplyStepVisualFromProgress(index, snapshot);
            }
        }

        if (clampedCurrentStepIndex != _lastVisualCurrentStepIndex &&
            (clampedCurrentStepIndex < firstNewCompletedStepIndex ||
             clampedCurrentStepIndex > clampedLastCompletedStepIndex))
        {
            ApplyStepVisualFromProgress(clampedCurrentStepIndex, snapshot);
        }

        StoreStepProgressTracking(clampedCurrentStepIndex, clampedLastCompletedStepIndex);
    }

    private void UpdateAllStepsFromProgress(ScriptExecutionProgressSnapshot snapshot)
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            ApplyStepVisualFromProgress(index, snapshot);
        }
    }

    private void ApplyStepVisualFromProgress(int index, ScriptExecutionProgressSnapshot snapshot)
    {
        if (index < 0 || index >= Steps.Count)
        {
            return;
        }

        if (index <= snapshot.LastCompletedStepIndex)
        {
            ApplyStepVisual(
                Steps[index],
                _localizationService.T("Editor.Runtime.Step.Completed"),
                CompletedStateBrush,
                CompletedCardBrush,
                isCurrent: false,
                CompletedTitleBrush);
            return;
        }

        if (index == snapshot.CurrentStepIndex)
        {
            ApplyStepVisual(
                Steps[index],
                ResolveRunningStepState(snapshot.RunState),
                RunningStateBrush,
                RunningCardBrush,
                isCurrent: true,
                RunningTitleBrush);
            return;
        }

        ApplyStepVisual(
            Steps[index],
            _localizationService.T("Editor.Runtime.Step.Pending"),
            PendingStateBrush,
            PendingCardBrush,
            isCurrent: false,
            PendingTitleBrush);
    }

    private void UpdateStepsFromResult(ScriptExecutionResult result)
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            if (index <= result.LastCompletedStepIndex)
            {
                ApplyStepVisual(
                    Steps[index],
                    _localizationService.T("Editor.Runtime.Step.Completed"),
                    CompletedStateBrush,
                    CompletedCardBrush,
                    isCurrent: false,
                    CompletedTitleBrush);
                continue;
            }

            if (result.Status == ScriptExecutionStatus.Failed &&
                result.Failure is not null &&
                index == result.Failure.StepIndex)
            {
                ApplyStepVisual(
                    Steps[index],
                    _localizationService.T("Editor.Runtime.Step.Failed"),
                    FailedStateBrush,
                    FailedCardBrush,
                    isCurrent: false,
                    FailedTitleBrush);
                continue;
            }

            if (result.Status == ScriptExecutionStatus.Cancelled &&
                index == Math.Clamp(result.LastCompletedStepIndex + 1, 0, Math.Max(0, Steps.Count - 1)) &&
                result.LastCompletedStepIndex < Steps.Count - 1)
            {
                ApplyStepVisual(
                    Steps[index],
                    _localizationService.T("Editor.Runtime.Step.Cancelled"),
                    CancelledStateBrush,
                    CancelledCardBrush,
                    isCurrent: false,
                    CancelledTitleBrush);
                continue;
            }

            ApplyStepVisual(
                Steps[index],
                _localizationService.T("Editor.Runtime.Step.Pending"),
                PendingStateBrush,
                PendingCardBrush,
                isCurrent: false,
                PendingTitleBrush);
        }
    }

    private void SetAllStepsToPending()
    {
        foreach (var step in Steps)
        {
            ApplyStepVisual(
                step,
                _localizationService.T("Editor.Runtime.Step.Pending"),
                PendingStateBrush,
                PendingCardBrush,
                isCurrent: false,
                PendingTitleBrush);
        }

        ResetStepProgressTracking();
    }

    private void ApplyStepVisual(
        ScriptExecutionStepItem step,
        string stateText,
        Brush stateBrush,
        Brush cardBrush,
        bool isCurrent,
        Brush titleBrush)
    {
        step.StateText = stateText;
        step.StateBrush = stateBrush;
        step.CardBorderBrush = stateBrush;
        step.CardBackgroundBrush = cardBrush;
        step.IsCurrent = isCurrent;
        step.TitleBrush = titleBrush;
        step.TitleWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private string BuildStatusText(ScriptExecutionProgressSnapshot snapshot)
    {
        var checkpointText = string.IsNullOrWhiteSpace(snapshot.CurrentCheckpoint)
            ? _localizationService.T("Editor.Runtime.None")
            : snapshot.CurrentCheckpoint;
        var messageText = string.IsNullOrWhiteSpace(snapshot.Message)
            ? _localizationService.T("Editor.Runtime.None")
            : snapshot.Message;

        return string.Format(
            _localizationService.T("Editor.Runtime.StatusTemplate"),
            snapshot.RunState,
            checkpointText,
            snapshot.CurrentAttempt,
            messageText);
    }

    private string BuildCurrentInstructionText(ScriptExecutionProgressSnapshot snapshot)
    {
        if (snapshot.CurrentStepIndex < 0 || snapshot.CurrentStepIndex >= Steps.Count)
        {
            return _localizationService.T("Editor.Runtime.WaitingInstruction");
        }

        var step = Steps[snapshot.CurrentStepIndex];
        return step.Title;
    }

    private string ResolveRunningStepState(ScriptExecutionRunState runState)
    {
        return runState switch
        {
            ScriptExecutionRunState.PauseRequested => _localizationService.T("Editor.Runtime.Step.PauseRequested"),
            ScriptExecutionRunState.Paused => _localizationService.T("Editor.Runtime.Step.Paused"),
            _ => _localizationService.T("Editor.Runtime.Step.Running")
        };
    }

    private void AppendProgressLog(ScriptExecutionProgressSnapshot snapshot)
    {
        if (IsCheckpointHandledByRuntimeLogger(snapshot.CurrentCheckpoint))
        {
            return;
        }

        var signature = $"{snapshot.RunState}|{snapshot.CurrentStepIndex}|{snapshot.CurrentCommandType}|{snapshot.CurrentCheckpoint}|{snapshot.CurrentAttempt}|{snapshot.Message}";
        if (string.Equals(signature, _lastLoggedSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedSignature = signature;

        var stepText = snapshot.CurrentStepIndex >= 0
            ? $"#{snapshot.CurrentStepIndex + 1:000}"
            : _localizationService.T("Editor.Runtime.NoStep");
        var commandText = string.IsNullOrWhiteSpace(snapshot.CurrentCommandType)
            ? _localizationService.T("Editor.Runtime.None")
            : snapshot.CurrentCommandType;
        var checkpointText = string.IsNullOrWhiteSpace(snapshot.CurrentCheckpoint)
            ? _localizationService.T("Editor.Runtime.None")
            : snapshot.CurrentCheckpoint;
        var attemptText = snapshot.CurrentAttempt > 0
            ? $" | attempt {snapshot.CurrentAttempt}"
            : string.Empty;
        var messageText = string.IsNullOrWhiteSpace(snapshot.Message)
            ? string.Empty
            : $" | {snapshot.Message}";

        AppendLog($"{snapshot.RunState} | {stepText} | {commandText} | {checkpointText}{attemptText}{messageText}");
    }

    private bool ApplyRuntimeLogEntry(ScriptExecutionRuntimeLogEntry entry, bool rebuildDisplayedLogs = true)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var prefix = $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Category}]";
        if (entry.Level is ScriptExecutionRuntimeLogLevel.Warning or ScriptExecutionRuntimeLogLevel.Error)
        {
            prefix = $"{prefix} [{entry.Level}]";
        }

        return AppendLog(
            $"{prefix} {entry.Message}",
            entry.Timestamp,
            entry.AggregationKey,
            entry.ReplaceExisting,
            rebuildDisplayedLogs);
    }

    private void EnsureRuntimeLogPathLogged(ScriptExecutionProgressSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.RuntimeLogFilePath) ||
            string.Equals(_runtimeLogFilePath, snapshot.RuntimeLogFilePath, StringComparison.Ordinal))
        {
            return;
        }

        _runtimeLogFilePath = snapshot.RuntimeLogFilePath;
        AppendLog($"Runtime log file: {_runtimeLogFilePath}");
    }

    private void ResetDisplayedLogs()
    {
        _lastLoggedSignature = string.Empty;
        _runtimeLogFilePath = string.Empty;
        _displayLogLines.Clear();
        LogLines.Clear();
        LogText = string.Empty;
    }

    private bool AppendLog(
        string message,
        DateTimeOffset? timestamp = null,
        string? aggregationKey = null,
        bool replaceExisting = false,
        bool rebuildDisplayedLogs = true)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalizedTimestamp = timestamp ?? DateTimeOffset.Now;
        var lineText = timestamp.HasValue
            ? message.Trim()
            : $"[{normalizedTimestamp:HH:mm:ss.fff}] {message.Trim()}";

        if (replaceExisting && !string.IsNullOrWhiteSpace(aggregationKey))
        {
            for (var index = _displayLogLines.Count - 1; index >= 0; index--)
            {
                if (!string.Equals(_displayLogLines[index].AggregationKey, aggregationKey, StringComparison.Ordinal))
                {
                    continue;
                }

                _displayLogLines[index] = new ScriptExecutionDisplayLogLine(aggregationKey, lineText);
                if (rebuildDisplayedLogs)
                {
                    RebuildDisplayedLogs();
                }

                return true;
            }
        }

        _displayLogLines.Add(new ScriptExecutionDisplayLogLine(aggregationKey ?? string.Empty, lineText));
        while (_displayLogLines.Count > MaxDisplayedLogLines)
        {
            _displayLogLines.RemoveAt(0);
        }

        if (rebuildDisplayedLogs)
        {
            RebuildDisplayedLogs();
        }

        return true;
    }

    private void RebuildDisplayedLogs()
    {
        LogLines.Clear();
        var builder = new StringBuilder();
        foreach (var line in _displayLogLines)
        {
            LogLines.Add(line.Text);
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line.Text);
        }

        LogText = builder.ToString();
    }

    private void StoreStepProgressTracking(
        int currentStepIndex,
        int lastCompletedStepIndex)
    {
        _lastVisualCurrentStepIndex = currentStepIndex;
        _lastVisualLastCompletedStepIndex = lastCompletedStepIndex;
        _hasProgressVisualState = true;
    }

    private void ResetStepProgressTracking()
    {
        _lastVisualCurrentStepIndex = -1;
        _lastVisualLastCompletedStepIndex = -1;
        _hasProgressVisualState = false;
    }

    private static bool IsCheckpointHandledByRuntimeLogger(string? checkpoint)
    {
        if (string.IsNullOrWhiteSpace(checkpoint))
        {
            return false;
        }

        return checkpoint switch
        {
            "WaitPolling" => true,
            "WaitSatisfied" => true,
            "WaitTimedOut" => true,
            "RetryAttempt" => true,
            "RetryAttemptFailed" => true,
            "RetryAttemptSucceeded" => true,
            _ when checkpoint.Contains("Delay", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }

    private ScriptExecutionStepItem? ResolveFocusedStep(int stepIndex)
    {
        return stepIndex >= 0 && stepIndex < Steps.Count
            ? Steps[stepIndex]
            : null;
    }

    private ScriptExecutionOperationIntervalStrategyItem ResolveIntervalStrategyItem(
        ScriptExecutionOperationIntervalStrategy strategy)
    {
        return IntervalStrategies.FirstOrDefault(item => item.Strategy == strategy) ?? IntervalStrategies[0];
    }

    private int ResolveStartStepIndex()
    {
        return NormalizeStepIndex(SelectedStep?.Index ?? 0);
    }

    private int ResolveResultFocusIndex(ScriptExecutionResult result)
    {
        if (result.Status == ScriptExecutionStatus.Failed && result.Failure is not null)
        {
            return result.Failure.StepIndex;
        }

        if (result.Status == ScriptExecutionStatus.Cancelled)
        {
            if (result.LastCompletedStepIndex < 0)
            {
                return _activeStartStepIndex;
            }

            return Math.Clamp(result.LastCompletedStepIndex + 1, 0, Math.Max(0, Steps.Count - 1));
        }

        if (result.LastCompletedStepIndex < 0)
        {
            return _activeStartStepIndex;
        }

        return Math.Clamp(result.LastCompletedStepIndex, 0, Math.Max(0, Steps.Count - 1));
    }

    private int NormalizeStepIndex(int stepIndex)
    {
        return Steps.Count == 0
            ? 0
            : Math.Clamp(stepIndex, 0, Steps.Count - 1);
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static int ClampCommonOperationInterval(int value)
    {
        return Math.Clamp(value, 50, 1000);
    }
}

internal readonly record struct ScriptExecutionDisplayLogLine(string AggregationKey, string Text);

public sealed class ScriptExecutionStepItem : ObservableObject
{
    private string _stateText = string.Empty;
    private Brush _stateBrush = Brushes.Transparent;
    private Brush _cardBorderBrush = Brushes.Transparent;
    private Brush _cardBackgroundBrush = Brushes.Transparent;
    private bool _isCurrent;
    private Brush _titleBrush = Brushes.White;
    private FontWeight _titleWeight = FontWeights.Normal;

    public ScriptExecutionStepItem(int index, string title, string description)
    {
        Index = index;
        Title = title;
        Description = description;
    }

    public int Index { get; }

    public string StepNumberText => $"#{Index + 1:000}";

    public string Title { get; }

    public string Description { get; }

    public string StateText
    {
        get => _stateText;
        set => SetProperty(ref _stateText, value);
    }

    public Brush StateBrush
    {
        get => _stateBrush;
        set => SetProperty(ref _stateBrush, value);
    }

    public Brush CardBorderBrush
    {
        get => _cardBorderBrush;
        set => SetProperty(ref _cardBorderBrush, value);
    }

    public Brush CardBackgroundBrush
    {
        get => _cardBackgroundBrush;
        set => SetProperty(ref _cardBackgroundBrush, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public Brush TitleBrush
    {
        get => _titleBrush;
        set => SetProperty(ref _titleBrush, value);
    }

    public FontWeight TitleWeight
    {
        get => _titleWeight;
        set => SetProperty(ref _titleWeight, value);
    }
}

public sealed class ScriptExecutionOperationIntervalStrategyItem
{
    public required ScriptExecutionOperationIntervalStrategy Strategy { get; init; }

    public required string DisplayName { get; init; }
}
