using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;
using BetterBTD.ViewModels;

namespace BetterBTD.Tests.ViewModels;

public sealed class ScriptEditorPageViewModelTests
{
    [Fact]
    public void SaveScriptDocument_RefreshesInstructionSequenceWithOptimizedInstructions()
    {
        var viewModel = new ScriptEditorPageViewModel(LocalizationService.Instance);
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.btd");

        try
        {
            viewModel.ImportScriptDocument(new ScriptDocument
            {
                Instructions =
                [
                    new ScriptInstructionDocument
                    {
                        CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                        TargetMonkeyBindingId = "dart-bind",
                        TargetMonkeyObjectId = "DartMonkey:1",
                        UpgradePath = UpgradePathType.Top.ToString(),
                        UpgradeCount = 1,
                        IntervalToNextInstructionMs = 0
                    },
                    new ScriptInstructionDocument
                    {
                        CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                        TargetMonkeyBindingId = "dart-bind",
                        TargetMonkeyObjectId = "DartMonkey:1",
                        UpgradePath = UpgradePathType.Top.ToString(),
                        UpgradeCount = 2,
                        IntervalToNextInstructionMs = 0
                    },
                    new ScriptInstructionDocument
                    {
                        CommandType = ScriptCommandType.NextRound.ToString(),
                        NextRoundAction = "SendNextRound",
                        NextRoundSendCount = 1,
                        IntervalToNextInstructionMs = 0
                    },
                    new ScriptInstructionDocument
                    {
                        CommandType = ScriptCommandType.NextRound.ToString(),
                        NextRoundAction = "SendNextRound",
                        NextRoundSendCount = 2,
                        IntervalToNextInstructionMs = 0
                    }
                ]
            });

            Assert.Equal(4, viewModel.InstructionSequence.Count);

            viewModel.SaveScriptDocument(filePath);

            Assert.Equal(filePath, viewModel.CurrentScriptFilePath);
            Assert.Equal(2, viewModel.InstructionSequence.Count);
            Assert.Equal(3, viewModel.InstructionSequence[0].UpgradeCount);
            Assert.Equal(3, viewModel.InstructionSequence[1].NextRoundSendCount);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
