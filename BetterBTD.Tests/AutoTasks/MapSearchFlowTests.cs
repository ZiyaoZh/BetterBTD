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
    public void Request_DefaultsToSecondModeButton()
    {
        var request = new AutoTaskRequest
        {
            Kind = AutoTaskKind.Collection,
            StageTarget = CreateTarget()
        };
        Assert.Equal(MapSearchModeButtonPosition.Second, request.MapSearchModeButtonPosition);
    }

    [Fact]
    public void PixelSample_UsesPerChannelTolerance()
    {
        Assert.False(new GameUiPixelSample(70, 90, 110, 50).HasChangedFrom(InitialPixel));
        Assert.True(ChangedPixel.HasChangedFrom(InitialPixel));
    }

    [Fact]
    public void ResultReadiness_ConfirmsImmediately_AndKeepsConfirmationWhenPixelReturns()
    {
        var state = CreateState(AutoTaskKind.Collection);
        state.SetProperty(CollectionAutoTaskStateKeys.MapSearchPixelBaseline, InitialPixel);

        Assert.True(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(ChangedPixel),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchConfirmed));
        Assert.True(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(InitialPixel),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchConfirmed));
        Assert.True(state.TryGetProperty<bool>(CollectionAutoTaskStateKeys.MapSearchConfirmed, out var confirmed));
        Assert.True(confirmed);
        Assert.False(MapSearchFlowState.IsResultReady(
            state,
            new GameUiSnapshot { State = GameUiStateId.Unknown },
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchConfirmed));
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
            CollectionAutoTaskStateKeys.MapSearchConfirmed));
        Assert.False(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(
                ChangedPixel,
                MapSearchFlowState.CollectionMapFact,
                GameMapType.DarkCastle,
                firstCapture + TimeSpan.FromSeconds(1)),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchConfirmed));
    }

    [Fact]
    public void ResultReadiness_AcceptsLegacyResultPixelImmediately_WhenStartingWithoutBaseline()
    {
        var state = CreateState(AutoTaskKind.Collection);
        var firstCapture = DateTimeOffset.UtcNow;

        Assert.True(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(
                LegacyResultPixel,
                MapSearchFlowState.CollectionMapFact,
                GameMapType.DarkCastle,
                firstCapture),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchConfirmed));
        Assert.True(MapSearchFlowState.IsResultReady(
            state,
            CreateMapSearchSnapshot(
                LegacyResultPixel,
                MapSearchFlowState.CollectionMapFact,
                GameMapType.DarkCastle,
                firstCapture + TimeSpan.FromSeconds(1)),
            CollectionAutoTaskStateKeys.MapSearchPixelBaseline,
            CollectionAutoTaskStateKeys.MapSearchConfirmed));
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
        Assert.False(state.TryGetProperty<GameMapType>(GetRecognizedMapKey(kind), out _));
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Strategy_UsesRecognizedMapImmediately_WhenStartingOnLegacyResultPixelWithoutBaseline(AutoTaskKind kind)
    {
        var state = CreateState(kind);
        var mapFact = kind == AutoTaskKind.Collection
            ? MapSearchFlowState.CollectionMapFact
            : MapSearchFlowState.GoldBalloonMapFact;
        var recognizedMap = kind == AutoTaskKind.Collection
            ? GameMapType.DarkCastle
            : GameMapType.MonkeyMeadow;

        SetScriptContext(state, kind, recognizedMap);

        var decision = await CreateStrategy(kind).DecideNextAsync(
            state,
            CreateMapSearchSnapshot(LegacyResultPixel, mapFact, recognizedMap));

        Assert.Equal(AutoTaskDecisionKind.Navigate, decision.Kind);
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
    public async Task Strategy_WaitsAfterConfirmedPixelChange_WhenOnlyMapCandidatesExist(AutoTaskKind kind)
    {
        var state = CreateState(kind);
        var baselineKey = GetBaselineKey(kind);
        var confirmedKey = kind == AutoTaskKind.Collection
            ? CollectionAutoTaskStateKeys.MapSearchConfirmed
            : GoldBalloonAutoTaskStateKeys.MapSearchConfirmed;
        var matchesFact = kind == AutoTaskKind.Collection
            ? MapSearchFlowState.CollectionMapMatchesFact
            : MapSearchFlowState.GoldBalloonMapMatchesFact;
        state.SetProperty(baselineKey, InitialPixel);
        state.SetProperty(confirmedKey, true);
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
    [InlineData(AutoTaskKind.Collection, MapSearchModeButtonPosition.First, 1275d)]
    [InlineData(AutoTaskKind.Collection, MapSearchModeButtonPosition.Second, 1350d)]
    [InlineData(AutoTaskKind.Collection, MapSearchModeButtonPosition.Third, 1425d)]
    [InlineData(AutoTaskKind.GoldBalloon, MapSearchModeButtonPosition.First, 1275d)]
    [InlineData(AutoTaskKind.GoldBalloon, MapSearchModeButtonPosition.Second, 1350d)]
    [InlineData(AutoTaskKind.GoldBalloon, MapSearchModeButtonPosition.Third, 1425d)]
    public async Task Handler_RepeatsConfiguredSearchButton_AndEntersRecognizedResult(
        AutoTaskKind kind,
        MapSearchModeButtonPosition position,
        double expectedX)
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var inputService = new ScriptInputSimulationService(CreateInputEnvironment(), dispatcher);
        var handler = CreateHandler(kind, inputService);
        var state = CreateState(kind, position);
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
        var resultSnapshot = CreateMapSearchSnapshot(ChangedPixel, mapFact, recognizedMap);
        var resultStep = GameUiNavigator.Instance.GetNextStep(CreateTarget(), resultSnapshot);
        var mapResult = await handler.ExecuteAsync(resultStep, state, resultSnapshot);

        Assert.True(mapResult.Succeeded);
        var clickPoints = dispatcher.Commands
            .Where(static command => command.Type == InputSimulationCommandType.MoveMouseToVirtualDesktop)
            .Select(static command => (command.X, command.Y))
            .ToArray();
        Assert.Equal(
            [(expectedX, 45d), (expectedX, 45d), (expectedX, 45d), (expectedX, 45d), (540d, 650d)],
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
    public async Task Handler_EntersLegacyResultImmediately_WhenStartingWithoutBaseline(AutoTaskKind kind)
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var inputService = new ScriptInputSimulationService(CreateInputEnvironment(), dispatcher);
        var handler = CreateHandler(kind, inputService);
        var state = CreateState(kind);
        var mapFact = kind == AutoTaskKind.Collection
            ? MapSearchFlowState.CollectionMapFact
            : MapSearchFlowState.GoldBalloonMapFact;
        var recognizedMap = kind == AutoTaskKind.Collection
            ? GameMapType.DarkCastle
            : GameMapType.MonkeyMeadow;
        var snapshot = CreateMapSearchSnapshot(LegacyResultPixel, mapFact, recognizedMap);
        var step = GameUiNavigator.Instance.GetNextStep(CreateTarget(), snapshot);

        var result = await handler.ExecuteAsync(step, state, snapshot);

        Assert.True(result.Succeeded);
        var clickPoint = Assert.Single(
            dispatcher.Commands,
            static command => command.Type == InputSimulationCommandType.MoveMouseToVirtualDesktop);
        Assert.Equal((540d, 650d), (clickPoint.X, clickPoint.Y));
        Assert.False(state.TryGetProperty<GameUiPixelSample>(GetBaselineKey(kind), out _));
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Handler_WaitsForMapAfterConfirmation_WithoutRepeatingModeButton(AutoTaskKind kind)
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var handler = CreateHandler(kind, new ScriptInputSimulationService(CreateInputEnvironment(), dispatcher));
        var state = CreateState(kind);
        var initialSnapshot = CreateMapSearchSnapshot(InitialPixel);
        var step = GameUiNavigator.Instance.GetNextStep(CreateTarget(), initialSnapshot);

        await handler.ExecuteAsync(step, state, initialSnapshot);
        await handler.ExecuteAsync(step, state, CreateMapSearchSnapshot(ChangedPixel));
        await handler.ExecuteAsync(step, state, CreateMapSearchSnapshot(InitialPixel));

        var modeClick = Assert.Single(dispatcher.Commands,
            static command => command.Type == InputSimulationCommandType.MoveMouseToVirtualDesktop);
        Assert.Equal((1350d, 45d), (modeClick.X, modeClick.Y));
        Assert.False(state.TryGetProperty<GameMapType>(GetRecognizedMapKey(kind), out _));

        var recognizedMap = GetInitialMap(kind);
        var recognizedSnapshot = CreateMapSearchSnapshot(InitialPixel, GetMapFact(kind), recognizedMap);
        await handler.ExecuteAsync(step, state, recognizedSnapshot);
        var clicks = dispatcher.Commands
            .Where(static command => command.Type == InputSimulationCommandType.MoveMouseToVirtualDesktop)
            .Select(static command => (command.X, command.Y))
            .ToArray();
        Assert.Equal([(1350d, 45d), (540d, 650d)], clicks);
        Assert.True(state.TryGetProperty<GameMapType>(GetRecognizedMapKey(kind), out var storedMap));
        Assert.Equal(recognizedMap, storedMap);
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Search_KeepsAcceptedMapAndScript_WhenLaterFramesChangeOrLoseRecognition(AutoTaskKind kind)
    {
        var state = CreateState(kind);
        state.SetProperty(GetBaselineKey(kind), InitialPixel);
        var recognizedMap = GetInitialMap(kind);
        SetScriptContext(state, kind, recognizedMap);
        var strategy = CreateStrategy(kind);
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var handler = CreateHandler(kind, new ScriptInputSimulationService(CreateInputEnvironment(), dispatcher));
        var firstSnapshot = CreateMapSearchSnapshot(ChangedPixel, GetMapFact(kind), recognizedMap);

        var firstDecision = await strategy.DecideNextAsync(state, firstSnapshot);
        Assert.Equal(AutoTaskDecisionKind.Navigate, firstDecision.Kind);
        await handler.ExecuteAsync(GameUiNavigator.Instance.GetNextStep(CreateTarget(), firstSnapshot), state, firstSnapshot);

        await strategy.DecideNextAsync(state, new GameUiSnapshot { State = GameUiStateId.Unknown });
        foreach (var laterSnapshot in new[]
                 {
                     CreateMapSearchSnapshot(InitialPixel, GetMapFact(kind), GameMapType.DarkDungeon),
                     CreateMapSearchSnapshot(InitialPixel)
                 })
        {
            var decision = await strategy.DecideNextAsync(state, laterSnapshot);
            Assert.Equal(AutoTaskDecisionKind.Navigate, decision.Kind);
            await handler.ExecuteAsync(GameUiNavigator.Instance.GetNextStep(CreateTarget(), laterSnapshot), state, laterSnapshot);
            Assert.True(state.TryGetProperty<GameMapType>(GetRecognizedMapKey(kind), out var storedMap));
            Assert.Equal(recognizedMap, storedMap);
            Assert.Equal(recognizedMap, GetScriptContextMap(state, kind));
        }

        var clicks = dispatcher.Commands
            .Where(static command => command.Type == InputSimulationCommandType.MoveMouseToVirtualDesktop)
            .Select(static command => (command.X, command.Y))
            .ToArray();
        Assert.Equal([(540d, 650d), (540d, 650d), (540d, 650d)], clicks);
    }

    [Theory]
    [InlineData(AutoTaskKind.Collection)]
    [InlineData(AutoTaskKind.GoldBalloon)]
    public async Task Strategy_ClearsSearchConfirmationAndMapOnExit_AndAcceptsNextSearch(AutoTaskKind kind)
    {
        var state = CreateState(kind);
        state.SetProperty(GetBaselineKey(kind), InitialPixel);
        var strategy = CreateStrategy(kind);
        var initialMap = GetInitialMap(kind);
        SetScriptContext(state, kind, initialMap);
        await strategy.DecideNextAsync(state, CreateMapSearchSnapshot(ChangedPixel, GetMapFact(kind), initialMap));

        await strategy.DecideNextAsync(state, new GameUiSnapshot { State = GameUiStateId.DifficultySelect });

        Assert.False(state.TryGetProperty<GameUiPixelSample>(GetBaselineKey(kind), out _));
        Assert.False(state.TryGetProperty<bool>(GetConfirmedKey(kind), out _));
        Assert.False(state.TryGetProperty<GameMapType>(GetRecognizedMapKey(kind), out _));
        var nextMap = kind == AutoTaskKind.Collection ? GameMapType.Infernal : GameMapType.Logs;
        SetScriptContext(state, kind, nextMap);
        var decision = await strategy.DecideNextAsync(
            state, CreateMapSearchSnapshot(LegacyResultPixel, GetMapFact(kind), nextMap));

        Assert.Equal(AutoTaskDecisionKind.Navigate, decision.Kind);
        Assert.True(state.TryGetProperty<GameMapType>(GetRecognizedMapKey(kind), out var storedMap));
        Assert.Equal(nextMap, storedMap);
        Assert.Equal(nextMap, GetScriptContextMap(state, kind));
    }

    private static GameMapType GetInitialMap(AutoTaskKind kind) =>
        kind == AutoTaskKind.Collection ? GameMapType.DarkCastle : GameMapType.MonkeyMeadow;

    private static string GetMapFact(AutoTaskKind kind) =>
        kind == AutoTaskKind.Collection ? MapSearchFlowState.CollectionMapFact : MapSearchFlowState.GoldBalloonMapFact;

    private static string GetRecognizedMapKey(AutoTaskKind kind) =>
        kind == AutoTaskKind.Collection ? CollectionAutoTaskStateKeys.RecognizedMap : GoldBalloonAutoTaskStateKeys.RecognizedMap;

    private static string GetConfirmedKey(AutoTaskKind kind) =>
        kind == AutoTaskKind.Collection ? CollectionAutoTaskStateKeys.MapSearchConfirmed : GoldBalloonAutoTaskStateKeys.MapSearchConfirmed;

    private static void SetScriptContext(AutoTaskRuntimeState state, AutoTaskKind kind, GameMapType map)
    {
        if (kind == AutoTaskKind.Collection)
        {
            state.SetProperty(CollectionAutoTaskStateKeys.ResolvedScriptContext, new CollectionAutoTaskScriptContext
            {
                Map = map,
                Difficulty = StageDifficulty.Hard,
                Mode = StageMode.Standard,
                Hero = HeroType.Quincy,
                FilePath = "collection.json"
            });
        }
        else
        {
            state.SetProperty(GoldBalloonAutoTaskStateKeys.ResolvedScriptContext, new GoldBalloonAutoTaskScriptContext
            {
                Map = map,
                Difficulty = StageDifficulty.Hard,
                Mode = StageMode.Standard,
                Hero = HeroType.Quincy,
                FilePath = "gold-balloon.json"
            });
        }
    }

    private static GameMapType GetScriptContextMap(AutoTaskRuntimeState state, AutoTaskKind kind)
    {
        if (kind == AutoTaskKind.Collection)
        {
            Assert.True(state.TryGetProperty<CollectionAutoTaskScriptContext>(CollectionAutoTaskStateKeys.ResolvedScriptContext, out var context));
            return context.Map;
        }

        Assert.True(state.TryGetProperty<GoldBalloonAutoTaskScriptContext>(GoldBalloonAutoTaskStateKeys.ResolvedScriptContext, out var goldBalloonContext));
        return goldBalloonContext.Map;
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

    private static AutoTaskRuntimeState CreateState(
        AutoTaskKind kind,
        MapSearchModeButtonPosition position = MapSearchModeButtonPosition.Second)
    {
        return new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = kind,
            MapSearchModeButtonPosition = position,
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
