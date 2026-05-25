using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using OpenCvSharp;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class GameUiStateService : IGameUiStateService
{
    private static readonly Lazy<GameUiStateService> InstanceHolder = new(() => new GameUiStateService());
    private static readonly TimeSpan UiStateConfirmationWindow = TimeSpan.FromMilliseconds(500);
    private static readonly GameMapType[] CollectionExpertMaps = GameElementCatalog.Maps
        .Where(static definition => definition.Tier == MapDifficultyTier.Expert)
        .Select(static definition => definition.Type)
        .ToArray();

    private readonly GameCaptureService _gameCaptureService;
    private readonly GameStageStateService _gameStageStateService;
    private readonly GameUiDetectionConfigService _detectionConfigService;
    private readonly GameUiNavigationOcrService _navigationOcrService;
    private readonly IReadOnlyList<IGameUiRecognizer> _recognizers;
    private readonly object _stabilizationSyncRoot = new();
    private GameUiStateId? _pendingUiState;
    private DateTimeOffset _pendingUiStateSince;

    private GameUiStateService()
        : this(
            GameCaptureService.Instance,
            GameStageStateService.Instance,
            GameUiDetectionConfigService.Instance,
            GameUiNavigationOcrService.Instance,
            [
                new ConfiguredGameUiRecognizer(GameUiDetectionConfigService.Instance),
                new UnknownGameUiRecognizer()
            ])
    {
    }

    internal GameUiStateService(
        GameCaptureService gameCaptureService,
        GameStageStateService gameStageStateService,
        GameUiDetectionConfigService detectionConfigService,
        GameUiNavigationOcrService navigationOcrService,
        IReadOnlyList<IGameUiRecognizer> recognizers)
    {
        _gameCaptureService = gameCaptureService ?? throw new ArgumentNullException(nameof(gameCaptureService));
        _gameStageStateService = gameStageStateService ?? throw new ArgumentNullException(nameof(gameStageStateService));
        _detectionConfigService = detectionConfigService ?? throw new ArgumentNullException(nameof(detectionConfigService));
        _navigationOcrService = navigationOcrService ?? throw new ArgumentNullException(nameof(navigationOcrService));
        _recognizers = recognizers?.OrderByDescending(static x => x.Priority).ToArray()
            ?? throw new ArgumentNullException(nameof(recognizers));
    }

    public static GameUiStateService Instance => InstanceHolder.Value;

    public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_gameCaptureService.TryCaptureFrame(out var windowInfo, out var frame))
        {
            return Task.FromResult(new GameUiSnapshot
            {
                State = GameUiStateId.Unknown,
                Confidence = 0d,
                Summary = "Capture frame unavailable."
            });
        }

        using (frame)
        {
            return Task.FromResult(CaptureSnapshot(windowInfo, frame));
        }
    }

    public GameUiSnapshot CaptureSnapshot(
        GameWindowInfo windowInfo,
        Mat frame,
        GameStageStateSnapshot? stageState = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Empty())
        {
            return new GameUiSnapshot
            {
                State = GameUiStateId.Unknown,
                Confidence = 0d,
                StageState = stageState,
                Summary = "Source frame is empty."
            };
        }

        if (stageState is null)
        {
            _ = _gameStageStateService.TryCaptureSnapshot(frame, out stageState, out _);
        }

        _ = _detectionConfigService.Current;

        var context = new GameUiRecognitionContext
        {
            CapturedAt = DateTimeOffset.UtcNow,
            WindowInfo = windowInfo,
            Frame = frame,
            StageState = stageState
        };

        foreach (var recognizer in _recognizers)
        {
            if (recognizer.TryRecognize(context, out var snapshot))
            {
                if (snapshot.StageState is null && stageState is not null)
                {
                    snapshot = new GameUiSnapshot
                    {
                        CapturedAt = snapshot.CapturedAt,
                        State = snapshot.State,
                        Confidence = snapshot.Confidence,
                        StageState = stageState,
                        Facts = snapshot.Facts,
                        Summary = snapshot.Summary
                    };
                }

                return ApplyStabilization(EnrichSnapshot(context, snapshot));
            }
        }

        return ApplyStabilization(EnrichSnapshot(context, new GameUiSnapshot
        {
            State = GameUiStateId.Unknown,
            Confidence = 0d,
            StageState = stageState,
            Summary = "No UI recognizer matched the current frame."
        }));
    }

    private GameUiSnapshot ApplyStabilization(GameUiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_stabilizationSyncRoot)
        {
            if (_pendingUiState != snapshot.State)
            {
                _pendingUiState = snapshot.State;
                _pendingUiStateSince = snapshot.CapturedAt;
                return CreateUnconfirmedSnapshot(snapshot, TimeSpan.Zero);
            }

            var stabilizedFor = snapshot.CapturedAt - _pendingUiStateSince;
            if (stabilizedFor < UiStateConfirmationWindow)
            {
                return CreateUnconfirmedSnapshot(snapshot, stabilizedFor);
            }

            return snapshot;
        }
    }

    private static GameUiSnapshot CreateUnconfirmedSnapshot(GameUiSnapshot snapshot, TimeSpan stabilizedFor)
    {
        var stabilizedMilliseconds = Math.Max(0d, stabilizedFor.TotalMilliseconds);
        return new GameUiSnapshot
        {
            CapturedAt = snapshot.CapturedAt,
            State = GameUiStateId.Unknown,
            Confidence = 0d,
            StageState = snapshot.StageState,
            Summary = $"UI state '{snapshot.State}' is pending confirmation ({stabilizedMilliseconds:F0}/{UiStateConfirmationWindow.TotalMilliseconds:F0} ms)."
        };
    }

    private GameUiSnapshot EnrichSnapshot(GameUiRecognitionContext context, GameUiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);

        var facts = snapshot.Facts.Count == 0
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(snapshot.Facts, StringComparer.OrdinalIgnoreCase);

        if (snapshot.State == GameUiStateId.MapSearchResults &&
            TryRecognizeCollectionMap(context.Frame, out var collectionMap, out var collectionMapMatches))
        {
            if (collectionMapMatches.Count > 0)
            {
                facts["collectionMapMatches"] = collectionMapMatches;
            }

            if (collectionMap.HasValue)
            {
                facts["collectionMap"] = collectionMap.Value;
            }
        }

        if (snapshot.State == GameUiStateId.Defeat &&
            _navigationOcrService.TryLocateHomeButton(context.Frame, out var homeButtonPoint))
        {
            facts["homeButtonPoint1080p"] = homeButtonPoint;
        }

        if (facts.Count == snapshot.Facts.Count)
        {
            return snapshot;
        }

        return new GameUiSnapshot
        {
            CapturedAt = snapshot.CapturedAt,
            State = snapshot.State,
            Confidence = snapshot.Confidence,
            StageState = snapshot.StageState,
            Facts = facts,
            Summary = snapshot.Summary
        };
    }

    private bool TryRecognizeCollectionMap(
        Mat frame,
        out GameMapType? map,
        out IReadOnlyList<MapTemplateMatchResult> candidateMatches)
    {
        var recognized = _navigationOcrService.TryLocateBestMap(
            frame,
            CollectionExpertMaps,
            frame.Width,
            frame.Height,
            0,
            0,
            out var recognizedMap,
            out _,
            out _,
            out candidateMatches);

        map = recognized ? recognizedMap : null;
        return recognized || candidateMatches.Count > 0;
    }
}

internal sealed class ConfiguredGameUiRecognizer : IGameUiRecognizer
{
    private readonly GameUiDetectionConfigService _detectionConfigService;

    public ConfiguredGameUiRecognizer(GameUiDetectionConfigService detectionConfigService)
    {
        _detectionConfigService = detectionConfigService ?? throw new ArgumentNullException(nameof(detectionConfigService));
    }

    public int Priority => 1000;

    public bool TryRecognize(GameUiRecognitionContext context, out GameUiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);

        var config = _detectionConfigService.Current;
        foreach (var rule in config.Rules
                     .Where(static x => x.IsEnabled)
                     .OrderByDescending(static x => x.Priority))
        {
            if (!GameUiDetectionRuleEvaluator.IsMatch(context.Frame, config, rule))
            {
                continue;
            }

            snapshot = new GameUiSnapshot
            {
                CapturedAt = context.CapturedAt,
                State = rule.State,
                Confidence = 1d,
                StageState = context.StageState,
                Facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ruleKey"] = rule.Key,
                    ["displayName"] = rule.DisplayName,
                    ["priority"] = rule.Priority
                },
                Summary = string.IsNullOrWhiteSpace(rule.DisplayName)
                    ? $"Matched UI rule '{rule.Key}'."
                    : $"Matched UI rule '{rule.DisplayName}' ({rule.Key})."
            };

            return true;
        }

        snapshot = null!;
        return false;
    }
}

internal sealed class UnknownGameUiRecognizer : IGameUiRecognizer
{
    public int Priority => int.MinValue;

    public bool TryRecognize(GameUiRecognitionContext context, out GameUiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);

        snapshot = new GameUiSnapshot
        {
            CapturedAt = context.CapturedAt,
            State = GameUiStateId.Unknown,
            Confidence = 0d,
            StageState = context.StageState,
            Summary = "UI recognition placeholders are active; state fell back to Unknown."
        };

        return true;
    }
}
