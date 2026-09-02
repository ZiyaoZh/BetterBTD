using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;
using BetterBTD.Services.MyScripts;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Tests.AutoTasks;

public sealed class AutoTaskDependencyPreflightServiceTests
{
    [Fact]
    public void ValidateKeyBindings_InspectsEveryDistinctScriptAndAggregatesIssues()
    {
        var loadedPaths = new List<string>();
        var service = new AutoTaskDependencyPreflightService(path =>
        {
            loadedPaths.Add(path);
            return path.EndsWith("first.btd", StringComparison.OrdinalIgnoreCase)
                ? CreateTaskFlow(ScriptCommandType.PlaceMonkey, selectedMonkeyTower: "DartMonkey")
                : CreateTaskFlow(ScriptCommandType.SellMonkey);
        });
        var keyBindings = new KeyBindingsConfig();
        keyBindings.TowerPlacement.DartMonkey.Key = KeyId.None;
        keyBindings.General.Sell.Key = KeyId.None;
        var firstPath = Path.GetFullPath("first.btd");
        var secondPath = Path.GetFullPath("second.btd");

        var issues = service.ValidateKeyBindings(
            [firstPath, secondPath, firstPath.ToUpperInvariant()],
            keyBindings);

        Assert.Equal([firstPath, secondPath], loadedPaths);
        Assert.Equal(
            ["TowerPlacement.DartMonkey", "General.Sell"],
            issues.Select(issue => issue.KeyBindingIssue.ConfigPropertyPath));
        Assert.Equal(["first", "second"], issues.Select(issue => issue.ScriptDisplayName));
    }

    [Fact]
    public void ValidateKeyBindings_LaterScriptMissingBinding_IsStillReported()
    {
        var service = new AutoTaskDependencyPreflightService(path =>
            path.EndsWith("third.btd", StringComparison.OrdinalIgnoreCase)
                ? CreateTaskFlow(ScriptCommandType.ActivateAbility, selectedAbility: "ActivatedAbility1")
                : CreateTaskFlow(ScriptCommandType.MouseClick));
        var keyBindings = new KeyBindingsConfig();
        keyBindings.Abilities.ActivatedAbility1.Key = KeyId.Unknown;

        var issues = service.ValidateKeyBindings(
            ["first.btd", "second.btd", "third.btd"],
            keyBindings);

        var issue = Assert.Single(issues);
        Assert.Equal("third", issue.ScriptDisplayName);
        Assert.Equal("Abilities.ActivatedAbility1", issue.KeyBindingIssue.ConfigPropertyPath);
    }

    [Fact]
    public async Task ValidateKeyBindingsAsync_RequestUsesLibraryDisplayNameForSlotAndPreferredPaths()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"BetterBTD-preflight-{Guid.NewGuid():N}");
        var libraryDirectory = Path.Combine(tempDirectory, "library");
        var sourceFilePath = Path.Combine(tempDirectory, "Friendly Script.btd");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var document = new ScriptDocument
            {
                Instructions =
                [
                    new ScriptInstructionDocument
                    {
                        CommandType = ScriptCommandType.SellMonkey.ToString()
                    }
                ]
            };
            ScriptDocumentService.Instance.Save(sourceFilePath, document);
            var library = new ManagedScriptLibraryService(
                libraryDirectory,
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var asset = library.ImportScript(sourceFilePath);
            var requiredSlotId = ManagedScriptSlotIdFactory.CreateGoldBalloonSlotId(GameMapType.MonkeyMeadow);
            library.SetBinding(requiredSlotId, asset.ScriptId);
            var service = new AutoTaskDependencyPreflightService(
                ScriptTaskFlowService.Instance.LoadFromFile,
                library);
            var keyBindings = new KeyBindingsConfig();
            keyBindings.General.Sell.Key = KeyId.None;
            var request = new AutoTaskRequest
            {
                Kind = AutoTaskKind.GoldBalloon,
                StageTarget = new StageEntryTarget
                {
                    Map = GameMapType.MonkeyMeadow,
                    Difficulty = StageDifficulty.Easy,
                    Mode = StageMode.Standard
                },
                RequiredScriptSlotIds =
                [
                    requiredSlotId,
                    ManagedScriptSlotIdFactory.CreateGoldBalloonSlotId(GameMapType.TreeStump)
                ]
            };

            var issues = await service.ValidateKeyBindingsAsync(request, keyBindings);
            var preferredPathIssues = await service.ValidateKeyBindingsAsync(
                new AutoTaskRequest
                {
                    Kind = AutoTaskKind.LoopStage,
                    StageTarget = request.StageTarget,
                    PreferredScriptPath = asset.StoredFilePath
                },
                keyBindings);

            var issue = Assert.Single(issues);
            Assert.Equal("Friendly Script", issue.ScriptDisplayName);
            Assert.Equal("General.Sell", issue.KeyBindingIssue.ConfigPropertyPath);
            Assert.Equal("Friendly Script", Assert.Single(preferredPathIssues).ScriptDisplayName);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static ScriptTaskFlow CreateTaskFlow(
        ScriptCommandType commandType,
        string selectedMonkeyTower = "",
        string selectedAbility = "")
    {
        var instruction = new ScriptInstructionDocument
        {
            CommandType = commandType.ToString(),
            SelectedMonkeyTower = selectedMonkeyTower,
            SelectedActivatedAbility = selectedAbility
        };
        return new ScriptTaskFlow
        {
            Document = new ScriptDocument { Instructions = [instruction] },
            Steps =
            [
                new ScriptTaskFlowStep
                {
                    Index = 0,
                    CommandType = commandType,
                    Instruction = instruction
                }
            ],
            MonkeyObjectsByBindingId = new Dictionary<string, ScriptMonkeyObjectDocument>()
        };
    }
}
