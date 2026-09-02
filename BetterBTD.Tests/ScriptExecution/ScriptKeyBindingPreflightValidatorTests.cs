using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Tests.ScriptExecution;

public sealed class ScriptKeyBindingPreflightValidatorTests
{
    [Fact]
    public void Validate_UnconfiguredBindings_ReturnsDistinctIssuesInFirstUseOrder()
    {
        var keyBindings = new KeyBindingsConfig();
        keyBindings.TowerPlacement.DartMonkey.Key = KeyId.None;
        keyBindings.General.Sell.Key = KeyId.Unknown;
        var taskFlow = CreateTaskFlow(
            Instruction(ScriptCommandType.PlaceMonkey, selectedMonkeyTower: "DartMonkey"),
            Instruction(ScriptCommandType.PlaceMonkey, selectedMonkeyTower: "DartMonkey"),
            Instruction(ScriptCommandType.SellMonkey));

        var issues = ScriptKeyBindingPreflightValidator.Validate(taskFlow, keyBindings);

        Assert.Collection(
            issues,
            issue =>
            {
                Assert.Equal("TowerPlacement.DartMonkey", issue.ConfigPropertyPath);
                Assert.Equal(0, issue.FirstStepIndex);
            },
            issue =>
            {
                Assert.Equal("General.Sell", issue.ConfigPropertyPath);
                Assert.Equal(2, issue.FirstStepIndex);
            });
    }

    [Fact]
    public void Validate_StartStepIndex_IgnoresBindingsUsedOnlyByEarlierSteps()
    {
        var keyBindings = new KeyBindingsConfig();
        keyBindings.TowerPlacement.DartMonkey.Key = KeyId.None;
        var taskFlow = CreateTaskFlow(
            Instruction(ScriptCommandType.PlaceMonkey, selectedMonkeyTower: "DartMonkey"),
            Instruction(ScriptCommandType.MouseClick));

        var issues = ScriptKeyBindingPreflightValidator.Validate(taskFlow, keyBindings, startStepIndex: 1);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_HeroInventoryInstruction_ReportsHeroAndInventoryBindings()
    {
        var keyBindings = new KeyBindingsConfig();
        keyBindings.General.Hero.Key = KeyId.None;
        keyBindings.HeroInventory.Inventory3.Key = KeyId.None;
        var taskFlow = CreateTaskFlow(new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.PlaceHeroInventory.ToString(),
            SelectedInventoryItem = "Inventory3"
        });

        var paths = ScriptKeyBindingPreflightValidator
            .Validate(taskFlow, keyBindings)
            .Select(issue => issue.ConfigPropertyPath);

        Assert.Equal(["General.Hero", "HeroInventory.Inventory3"], paths);
    }

    private static ScriptInstructionDocument Instruction(
        ScriptCommandType commandType,
        string selectedMonkeyTower = "")
    {
        return new ScriptInstructionDocument
        {
            CommandType = commandType.ToString(),
            SelectedMonkeyTower = selectedMonkeyTower
        };
    }

    private static ScriptTaskFlow CreateTaskFlow(params ScriptInstructionDocument[] instructions)
    {
        var document = new ScriptDocument { Instructions = [.. instructions] };
        return new ScriptTaskFlow
        {
            Document = document,
            Steps = instructions.Select((instruction, index) => new ScriptTaskFlowStep
            {
                Index = index,
                CommandType = Enum.Parse<ScriptCommandType>(instruction.CommandType),
                Instruction = instruction
            }).ToArray(),
            MonkeyObjectsByBindingId = new Dictionary<string, ScriptMonkeyObjectDocument>()
        };
    }
}
