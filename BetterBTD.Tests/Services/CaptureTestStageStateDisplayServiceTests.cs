using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;
using BetterBTD.Services.Start.Capture;

namespace BetterBTD.Tests.Services;

public sealed class CaptureTestStageStateDisplayServiceTests
{
    [Fact]
    public void Build_IncludesMapRecognition_WhenUiStateIsMapSearchResults()
    {
        var localizationService = LocalizationService.Instance;
        var matches = new[]
        {
            new MapTemplateMatchResult(GameMapType.DarkCastle, new TemplateMatchInfo(0, 0, 10, 10, 0.97d, 0.94d)),
            new MapTemplateMatchResult(GameMapType.Infernal, new TemplateMatchInfo(5, 5, 10, 10, 0.91d, 0.94d))
        };
        var gameUiSnapshot = new GameUiSnapshot
        {
            State = GameUiStateId.MapSearchResults,
            Facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["collectionMap"] = GameMapType.DarkCastle,
                ["collectionMapMatches"] = matches
            }
        };

        var displayModel = CaptureTestStageStateDisplayService.Instance.Build(
            localizationService,
            isAvailable: true,
            failed: false,
            failureMessage: null,
            snapshot: new GameStageStateSnapshot(),
            averageReadMilliseconds: 1d,
            gameUiSnapshot);

        Assert.Contains(localizationService.T("CaptureTest.MapRecognition"), displayModel.DetailsText);
        Assert.Contains(GameElementCatalog.GetMapDisplayName(GameMapType.DarkCastle), displayModel.DetailsText);
        Assert.Contains(GameElementCatalog.GetMapDisplayName(GameMapType.Infernal), displayModel.DetailsText);
    }

    [Fact]
    public void Build_HidesMapRecognition_WhenUiStateIsNotMapSearchResults()
    {
        var localizationService = LocalizationService.Instance;
        var matches = new[]
        {
            new MapTemplateMatchResult(GameMapType.DarkCastle, new TemplateMatchInfo(0, 0, 10, 10, 0.97d, 0.94d))
        };
        var gameUiSnapshot = new GameUiSnapshot
        {
            State = GameUiStateId.MapGrid,
            Facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["collectionMap"] = GameMapType.DarkCastle,
                ["collectionMapMatches"] = matches
            }
        };

        var displayModel = CaptureTestStageStateDisplayService.Instance.Build(
            localizationService,
            isAvailable: true,
            failed: false,
            failureMessage: null,
            snapshot: new GameStageStateSnapshot(),
            averageReadMilliseconds: 1d,
            gameUiSnapshot);

        Assert.DoesNotContain(localizationService.T("CaptureTest.MapRecognition"), displayModel.DetailsText);
    }
}
