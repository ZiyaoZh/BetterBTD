using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Core.AutoTasks.Strategies;
using BetterBTD.Core.Simulator;
using BetterBTD.Helpers;
using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.AutoTasks;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using BetterBTD.Services.Tasks.Input;
using BetterBTD.Tests.TestDoubles;
using OpenCvSharp;

namespace BetterBTD.Tests.AutoTasks;

public sealed class MapSearchFlowTests
{
    private static readonly GameUiPixelSample InitialPixel = new(20, 40, 60, 50);
    private static readonly GameUiPixelSample ChangedPixel = new(80, 40, 60, 50);
    private static readonly GameUiPixelSample LegacyResultPixel = new(0x40, 0x9F, 0xFF, 50);

    [Fact]
    public void PixelSample_UsesPerChannelTolerance()
    {
        Assert.False(new GameUiPixelSample(70, 90, 110, 50).HasChangedFrom(InitialPixel));
        Assert.True(ChangedPixel.HasChangedFrom(InitialPixel));
    }

    [Fact]
    public void ResultReadiness_RequiresStablePixelChange_AndResetsWhenPixelReturns()
    {
        var state = CreateState(AutoTaskKind.Collection);
        state.SetProperty(CollectionAutoTaskStateKeys.MapSearchPixelBaseline, InitialPixel);
        var changedAt = DateTimeOffset.UtcNow;

        Assert.False(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(ChangedPixel, capturedAt: changedAt),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchPixelChangedSince));
        Assert.True(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(ChangedPixel, capturedAt: changedAt + TimeSpan.FromSeconds(1)),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchPixelChangedSince));
        Assert.False(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(InitialPixel, capturedAt: changedAt + TimeSpan.FromSeconds(2)),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchPixelChangedSince));
        Assert.False(state.TryGetProperty<DateTimeOffset>(
            CollectionAutoTaskStateKeys.MapSearchPixelChangedSince,
            out _));
    }

    [Fact]
    public void ResultReadiness_RequiresPixelBaseline_EvenWhenMapIsRecognized()
    {
        var state = CreateState(AutoTaskKind.Collection);
        var firstCapture = DateTimeOffset.UtcNow;

        Assert.False(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(
                ChangedPixel,
                MapSearchFlowState.CollectionMapFact,
                GameMapType.DarkCastle,
                firstCapture),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchPixelChangedSince));
        Assert.False(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(
                ChangedPixel,
                MapSearchFlowState.CollectionMapFact,
                GameMapType.DarkCastle,
                firstCapture + TimeSpan.FromSeconds(1)),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchPixelChangedSince));
    }

    [Fact]
    public void ResultReadiness_AcceptsStableLegacyResultPixel_WhenStartingWithoutBaseline()
    {
        var state = CreateState(AutoTaskKind.Collection);
        var firstCapture = DateTimeOffset.UtcNow;

        Assert.False(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(
                LegacyResultPixel,
                MapSearchFlowState.CollectionMapFact,
                GameMapType.DarkCastle,
                firstCapture),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchPixelChangedSince));
        Assert.True(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(
                LegacyResultPixel,
                MapSearchFlowState.CollectionMapFact,
                GameMapType.DarkCastle,
                firstCapture + TimeSpan.FromSeconds(1)),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchPixelChangedSince));
    }

    [Fact]
    public void StateService_EnrichesMapSearchWithTransitionPixelSample()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "BetterBTD.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var configService = new GameUiDetectionConfigService(
                Path.Combine(tempDirectory, "game_ui_detection_rules.json"));
            using var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
            SetPixel(frame, 962, 837, 0x12, 0x34, 0x56);
            var recognizer = new StaticGameUiRecognizer(GameUiStateId.MapSearch);
            var service = new GameUiStateService(
                GameCaptureService.Instance,
                GameStageStateService.Instance,
                configService,
                GameUiNavigationOcrService.Instance,
                [recognizer]);

            _ = service.CaptureSnapshot(default, frame, new GameStageStateSnapshot());
            recognizer.CapturedAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
            var snapshot = service.CaptureSnapshot(default, frame, new GameStageStateSnapshot());

            Assert.Equal(GameUiStateId.MapSearch, snapshot.State);
            Assert.True(MapSearchFlowState.TryGetPixelSample(snapshot, out var sample));
            Assert.Equal(new GameUiPixelSample(0x12, 0x34, 0x56, 50), sample);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Navigator_UsesSingleMapSearchState_ForSearchAndRecognizedMapActions()
    {
        var target = CreateTarget();

        var searchStep = GameUiNavigator.Instance.GetNextStep(
            target,
            CreateMapSearchSnapshot(InitialPixel));
        var resultStep = GameUiNavigator.Instance.GetNextStep(
            target,
            CreateMapSearchSnapshot(
                ChangedPixel,
                MapSearchFlowState.CollectionMapFact,
                GameMapType.DarkCastle));

        Assert.Equal(GameUiActionKind.SelectMapCategory, searchStep.ActionKind);
        Assert.Equal(GameUiActionKind.SelectMap, resultStep.ActionKind);
        Assert.Contains(GameUiStateId.MapSearch, searchStep.ExpectedNextStates);
        Assert.Contains(GameUiStateId.DifficultySelect, resultStep.ExpectedNextStates);
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Strategy_ContinuesSearchUntilPixelChanges_ThenWaitsForExplicitMap(AutoTaskKind kind)
    {
        var state = CreateState(kind);
        var baselineKey = GetBaselineKey(kind);
        state.SetProperty(baselineKey, InitialPixel);
        var strategy = CreateStrategy(kind);

        var searchDecision = await strategy.DecideNextAsync(
            state,
            CreateMapSearchSnapshot(InitialPixel));
        var resultDecision = await strategy.DecideNextAsync(
            state,
            CreateMapSearchSnapshot(ChangedPixel));

        Assert.Equal(AutoTaskDecisionKind.Navigate, searchDecision.Kind);
        Assert.Equal(AutoTaskDecisionKind.Wait, resultDecision.Kind);
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Strategy_ContinuesSearch_WhenMapIsRecognizedBeforePixelChanges(AutoTaskKind kind)
    {
        var state = CreateState(kind);
        state.SetProperty(GetBaselineKey(kind), InitialPixel);
        var mapFact = kind == AutoTaskKind.Collection
            ? MapSearchFlowState.CollectionMapFact
            : MapSearchFlowState.GoldBalloonMapFact;
        var recognizedMap = kind == AutoTaskKind.Collection
            ? GameMapType.DarkCastle
            : GameMapType.MonkeyMeadow;

        var decision = await CreateStrategy(kind).DecideNextAsync(
            state,
            CreateMapSearchSnapshot(InitialPixel, mapFact, recognizedMap));

        Assert.Equal(AutoTaskDecisionKind.Navigate, decision.Kind);
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Strategy_Waits_WhenStartingOnLegacyResultPixelWithoutBaseline(AutoTaskKind kind)
    {
        var state = CreateState(kind);
        var mapFact = kind == AutoTaskKind.Collection
            ? MapSearchFlowState.CollectionMapFact
            : MapSearchFlowState.GoldBalloonMapFact;
        var recognizedMap = kind == AutoTaskKind.Collection
            ? GameMapType.DarkCastle
            : GameMapType.MonkeyMeadow;

        var decision = await CreateStrategy(kind).DecideNextAsync(
            state,
            CreateMapSearchSnapshot(LegacyResultPixel, mapFact, recognizedMap));

        Assert.Equal(AutoTaskDecisionKind.Wait, decision.Kind);
        Assert.False(state.TryGetProperty<GameUiPixelSample>(GetBaselineKey(kind), out _));
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection, false)]
    [InlineData(AutoTaskKind.Collection, true)]
    [InlineData(AutoTaskKind.GoldBalloon, false)]
    [InlineData(AutoTaskKind.GoldBalloon, true)]
    public async Task Strategy_WaitsForExplicitMap_WhenStartingOnLegacyResultPixel(
        AutoTaskKind kind,
        bool includeCandidates)
    {
        var state = CreateState(kind);
        var firstCapture = DateTimeOffset.UtcNow;
        var firstSnapshot = CreateMapSearchSnapshot(LegacyResultPixel, capturedAt: firstCapture);
        var secondSnapshot = CreateMapSearchSnapshot(
            LegacyResultPixel,
            capturedAt: firstCapture + TimeSpan.FromSeconds(1));
        if (includeCandidates)
        {
            var matchesFact = kind == AutoTaskKind.Collection
                ? MapSearchFlowState.CollectionMapMatchesFact
                : MapSearchFlowState.GoldBalloonMapMatchesFact;
            var candidateMap = kind == AutoTaskKind.Collection
                ? GameMapType.DarkCastle
                : GameMapType.MonkeyMeadow;
            var candidates = new[]
            {
                new MapTemplateMatchResult(
                    candidateMap,
                    new TemplateMatchInfo(360, 207, 360, 250, 0.90d, 0.94d))
            };
            firstSnapshot = WithFact(firstSnapshot, matchesFact, candidates);
            secondSnapshot = WithFact(secondSnapshot, matchesFact, candidates);
        }

        var strategy = CreateStrategy(kind);
        var firstDecision = await strategy.DecideNextAsync(state, firstSnapshot);
        var stableDecision = await strategy.DecideNextAsync(state, secondSnapshot);

        Assert.Equal(AutoTaskDecisionKind.Wait, firstDecision.Kind);
        Assert.Equal(AutoTaskDecisionKind.Wait, stableDecision.Kind);
        Assert.False(state.TryGetProperty<GameUiPixelSample>(GetBaselineKey(kind), out _));
    }

    [Fact]
    public void CaptureTestDisplay_ShowsCandidateOnlyMapMatches_OnMapSearch()
    {
        var localization = LocalizationService.Instance;
        var previousLanguage = localization.LanguageCode;
        localization.SetLanguage("en-US");

        try
        {
            var candidate = new MapTemplateMatchResult(
                GameMapType.DarkCastle,
                new TemplateMatchInfo(360, 520, 360, 250, 0.91d, 0.94d));
            var snapshot = CreateMapSearchSnapshot(InitialPixel);
            var facts = new Dictionary<string, object?>(snapshot.Facts, StringComparer.OrdinalIgnoreCase)
            {
                [MapSearchFlowState.CollectionMapMatchesFact] = new[] { candidate }
            };

            var display = CaptureTestStageStateDisplayService.Instance.Build(
                localization,
                isAvailable: true,
                failed: false,
                failureMessage: null,
                snapshot: null,
                averageReadMilliseconds: 1d,
                gameUiSnapshot: new GameUiSnapshot
                {
                    State = GameUiStateId.MapSearch,
                    Facts = facts
                });

            Assert.Contains("Map Recognition: -- -> Dark Castle 91.00%", display.DetailsText);
        }
        finally
        {
            localization.SetLanguage(previousLanguage);
        }
    }

    [Fact]
    public void CaptureTestDisplay_PrefersExplicitGoldBalloonMap_OverCollectionCandidates()
    {
        var localization = LocalizationService.Instance;
        var previousLanguage = localization.LanguageCode;
        localization.SetLanguage("en-US");

        try
        {
            var collectionCandidate = new MapTemplateMatchResult(
                GameMapType.DarkCastle,
                new TemplateMatchInfo(360, 520, 360, 250, 0.91d, 0.94d));
            var goldBalloonMatch = new MapTemplateMatchResult(
                GameMapType.MonkeyMeadow,
                new TemplateMatchInfo(360, 207, 360, 250, 0.95d, 0.94d));
            var snapshot = CreateMapSearchSnapshot(InitialPixel);
            var facts = new Dictionary<string, object?>(snapshot.Facts, StringComparer.OrdinalIgnoreCase)
            {
                [MapSearchFlowState.CollectionMapMatchesFact] = new[] { collectionCandidate },
                [MapSearchFlowState.GoldBalloonMapFact] = GameMapType.MonkeyMeadow,
                [MapSearchFlowState.GoldBalloonMapMatchesFact] = new[] { goldBalloonMatch }
            };

            var display = CaptureTestStageStateDisplayService.Instance.Build(
                localization,
                isAvailable: true,
                failed: false,
                failureMessage: null,
                snapshot: null,
                averageReadMilliseconds: 1d,
                gameUiSnapshot: new GameUiSnapshot
                {
                    State = GameUiStateId.MapSearch,
                    Facts = facts
                });

            Assert.Contains("Map Recognition: Monkey Meadow (95.00%)", display.DetailsText);
            Assert.DoesNotContain("Dark Castle", display.DetailsText);
        }
        finally
        {
            localization.SetLanguage(previousLanguage);
        }
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Strategy_WaitsAfterStablePixelChange_WhenOnlyMapCandidatesExist(AutoTaskKind kind)
    {
        var state = CreateState(kind);
        var baselineKey = GetBaselineKey(kind);
        var changedSinceKey = kind == AutoTaskKind.Collection
            ? CollectionAutoTaskStateKeys.MapSearchPixelChangedSince
            : GoldBalloonAutoTaskStateKeys.MapSearchPixelChangedSince;
        var matchesFact = kind == AutoTaskKind.Collection
            ? MapSearchFlowState.CollectionMapMatchesFact
            : MapSearchFlowState.GoldBalloonMapMatchesFact;
        state.SetProperty(baselineKey, InitialPixel);
        state.SetProperty(changedSinceKey, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2));
        var snapshot = CreateMapSearchSnapshot(ChangedPixel);
        var facts = new Dictionary<string, object?>(snapshot.Facts, StringComparer.OrdinalIgnoreCase)
        {
            [matchesFact] = Array.Empty<MapTemplateMatchResult>()
        };

        var decision = await CreateStrategy(kind).DecideNextAsync(
            state,
            new GameUiSnapshot
            {
                State = GameUiStateId.MapSearch,
                Facts = facts
            });

        Assert.Equal(AutoTaskDecisionKind.Wait, decision.Kind);
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Handler_PreservesSearchClickFallback_AndEntersRecognizedResult(AutoTaskKind kind)
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var inputService = new ScriptInputSimulationService(CreateInputEnvironment(), dispatcher);
        var handler = CreateHandler(kind, inputService);
        var state = CreateState(kind);
        var initialSnapshot = CreateMapSearchSnapshot(InitialPixel);
        var initialStep = GameUiNavigator.Instance.GetNextStep(CreateTarget(), initialSnapshot);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var result = await handler.ExecuteAsync(initialStep, state, initialSnapshot);
            Assert.True(result.Succeeded);
        }

        var mapFact = kind == AutoTaskKind.Collection
            ? MapSearchFlowState.CollectionMapFact
            : MapSearchFlowState.GoldBalloonMapFact;
        var recognizedMap = kind == AutoTaskKind.Collection
            ? GameMapType.DarkCastle
            : GameMapType.MonkeyMeadow;
        state.SetProperty(
            kind == AutoTaskKind.Collection
                ? CollectionAutoTaskStateKeys.MapSearchPixelChangedSince
                : GoldBalloonAutoTaskStateKeys.MapSearchPixelChangedSince,
            DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2));
        var resultSnapshot = CreateMapSearchSnapshot(ChangedPixel, mapFact, recognizedMap);
        var resultStep = GameUiNavigator.Instance.GetNextStep(CreateTarget(), resultSnapshot);
        var mapResult = await handler.ExecuteAsync(resultStep, state, resultSnapshot);

        Assert.True(mapResult.Succeeded);
        var clickPoints = dispatcher.Commands
            .Where(static command => command.Type == InputSimulationCommandType.MoveMouseToVirtualDesktop)
            .Select(static command => (command.X, command.Y))
            .ToArray();
        Assert.Equal(
            [(1350d, 45d), (1350d, 45d), (1350d, 45d), (1275d, 45d), (540d, 650d)],
            clickPoints);

        var attemptsKey = kind == AutoTaskKind.Collection
            ? CollectionAutoTaskStateKeys.MapSearchAttempts
            : GoldBalloonAutoTaskStateKeys.MapSearchAttempts;
        var recognizedMapKey = kind == AutoTaskKind.Collection
            ? CollectionAutoTaskStateKeys.RecognizedMap
            : GoldBalloonAutoTaskStateKeys.RecognizedMap;
        Assert.True(state.TryGetProperty<int>(attemptsKey, out var attempts));
        Assert.Equal(0, attempts);
        Assert.True(state.TryGetProperty<GameMapType>(recognizedMapKey, out var storedMap));
        Assert.Equal(recognizedMap, storedMap);
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Handler_EntersStableLegacyResult_WhenStartingWithoutBaseline(AutoTaskKind kind)
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var inputService = new ScriptInputSimulationService(CreateInputEnvironment(), dispatcher);
        var handler = CreateHandler(kind, inputService);
        var state = CreateState(kind);
        var changedSinceKey = kind == AutoTaskKind.Collection
            ? CollectionAutoTaskStateKeys.MapSearchPixelChangedSince
            : GoldBalloonAutoTaskStateKeys.MapSearchPixelChangedSince;
        var mapFact = kind == AutoTaskKind.Collection
            ? MapSearchFlowState.CollectionMapFact
            : MapSearchFlowState.GoldBalloonMapFact;
        var recognizedMap = kind == AutoTaskKind.Collection
            ? GameMapType.DarkCastle
            : GameMapType.MonkeyMeadow;
        var snapshot = CreateMapSearchSnapshot(LegacyResultPixel, mapFact, recognizedMap);
        state.SetProperty(changedSinceKey, snapshot.CapturedAt - TimeSpan.FromSeconds(1));
        var step = GameUiNavigator.Instance.GetNextStep(CreateTarget(), snapshot);

        var result = await handler.ExecuteAsync(step, state, snapshot);

        Assert.True(result.Succeeded);
        var clickPoint = Assert.Single(
            dispatcher.Commands,
            static command => command.Type == InputSimulationCommandType.MoveMouseToVirtualDesktop);
        Assert.Equal((540d, 650d), (clickPoint.X, clickPoint.Y));
        Assert.False(state.TryGetProperty<GameUiPixelSample>(GetBaselineKey(kind), out _));
    }

    private static IAutoTaskStrategy CreateStrategy(AutoTaskKind kind)
    {
        return kind switch
        {
            AutoTaskKind.Collection => new CollectionAutoTaskStrategy(),
            AutoTaskKind.GoldBalloon => new GoldBalloonAutoTaskStrategy(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static IGameUiTaskActionHandler CreateHandler(
        AutoTaskKind kind,
        ScriptInputSimulationService inputService)
    {
        return kind switch
        {
            AutoTaskKind.Collection => new CollectionGameUiActionHandler(
                inputService,
                GameCaptureService.Instance,
                GameUiNavigationOcrService.Instance),
            AutoTaskKind.GoldBalloon => new GoldBalloonGameUiActionHandler(
                inputService,
                GameCaptureService.Instance,
                GameUiNavigationOcrService.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static AutoTaskRuntimeState CreateState(AutoTaskKind kind)
    {
        return new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = kind,
            StageTarget = CreateTarget()
        });
    }

    private static string GetBaselineKey(AutoTaskKind kind)
    {
        return kind == AutoTaskKind.Collection
            ? CollectionAutoTaskStateKeys.MapSearchPixelBaseline
            : GoldBalloonAutoTaskStateKeys.MapSearchPixelBaseline;
    }

    private static GameUiSnapshot CreateMapSearchSnapshot(
        GameUiPixelSample pixel,
        string? mapFact = null,
        GameMapType map = default,
        DateTimeOffset? capturedAt = null)
    {
        var facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [MapSearchFlowState.PixelSampleFact] = pixel
        };
        if (!string.IsNullOrWhiteSpace(mapFact))
        {
            facts[mapFact] = map;
        }

        return new GameUiSnapshot
        {
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow,
            State = GameUiStateId.MapSearch,
            Facts = facts
        };
    }

    private static GameUiSnapshot WithFact(GameUiSnapshot snapshot, string factKey, object value)
    {
        return new GameUiSnapshot
        {
            CapturedAt = snapshot.CapturedAt,
            State = snapshot.State,
            Confidence = snapshot.Confidence,
            StageState = snapshot.StageState,
            Facts = new Dictionary<string, object?>(snapshot.Facts, StringComparer.OrdinalIgnoreCase)
            {
                [factKey] = value
            },
            Summary = snapshot.Summary
        };
    }

    private static StageEntryTarget CreateTarget()
    {
        return new StageEntryTarget
        {
            Map = GameMapType.DarkCastle,
            Difficulty = StageDifficulty.Hard,
            Mode = StageMode.Standard
        };
    }

    private static FakeScriptInputSimulationEnvironment CreateInputEnvironment()
    {
        var bounds = new NativeWindowBounds(0, 0, 1920, 1080);
        return new FakeScriptInputSimulationEnvironment(
            new GameWindowInfo(nint.Zero, "Test Window", bounds, bounds, 1d));
    }

    private static void SetPixel(Mat frame, int x, int y, byte red, byte green, byte blue)
    {
        frame.Set(y, x, new Vec3b(blue, green, red));
    }

    private sealed class StaticGameUiRecognizer : IGameUiRecognizer
    {
        private readonly GameUiStateId _state;

        public StaticGameUiRecognizer(GameUiStateId state)
        {
            _state = state;
        }

        public int Priority => 1;

        public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

        public bool TryRecognize(GameUiRecognitionContext context, out GameUiSnapshot snapshot)
        {
            snapshot = new GameUiSnapshot
            {
                CapturedAt = CapturedAt,
                State = _state,
                Confidence = 1d
            };
            return true;
        }
    }
}
