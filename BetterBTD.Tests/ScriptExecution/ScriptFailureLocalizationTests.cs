using BetterBTD.Core.ScriptExecution;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.ViewModels;

namespace BetterBTD.Tests.ScriptExecution;

public sealed class ScriptFailureLocalizationTests
{
    [Theory]
    [InlineData("zh-CN", "第 3 步「放置猴子」", "仍无法在 (960, 540) 附近放置“飞镖猴”", "尝试次数：3")]
    [InlineData("en-US", "step 3 (Place)", "Failed to place 'Dart' near (960, 540)", "Attempts: 3")]
    public void FailedResult_UsesUiLanguageAndPreservesDiagnosticMessage(
        string language, string expectedStep, string expectedReason, string expectedAttempts)
    {
        var localization = LocalizationService.Instance;
        var previousLanguage = localization.LanguageCode;
        const string diagnostic = "Original placement diagnostic.";
        var result = new ScriptExecutionResult
        {
            Status = ScriptExecutionStatus.Failed,
            ExecutedStepCount = 2,
            LastCompletedStepIndex = 1,
            Failure = new ScriptExecutionFailureDetails
            {
                StepIndex = 2,
                CommandType = "PlaceMonkey",
                Checkpoint = "PlaceMonkeyClick",
                Attempt = 3,
                Message = diagnostic,
                MessageKey = "Editor.Runtime.Error.PlacementOffsetFailed",
                MessageArguments = ["Tower:DartMonkey", "(960, 540)"]
            }
        };

        try
        {
            localization.SetLanguage(language);
            var viewModel = new ScriptExecutionWindowViewModel(
                localization, "Test", "test.btd", [], static (_, _) => Task.CompletedTask, static () => { });
            viewModel.ApplyResult(result);

            Assert.Contains(expectedStep, viewModel.StatusText);
            Assert.Contains(expectedReason, viewModel.StatusText);
            Assert.Contains(expectedAttempts, viewModel.StatusText);
            Assert.DoesNotContain(diagnostic, viewModel.StatusText);
            Assert.DoesNotContain("PlaceMonkeyClick", viewModel.StatusText);
            Assert.Equal(diagnostic, result.Failure.Message);
        }
        finally
        {
            localization.SetLanguage(previousLanguage);
        }
    }

    [Fact]
    public void SameFailure_CanBePresentedInEitherLanguage_WithoutReexecutingScript()
    {
        var localization = LocalizationService.Instance;
        var previousLanguage = localization.LanguageCode;
        var exception = new ScriptExecutionException(
            "Failed to read the current 'Top' path level.", 0, "UpgradeMonkey", "UpgradeMonkeyPanel",
            messageKey: "Editor.Runtime.Error.UpgradeLevelUnreadable", messageArguments: ["Top"]);
        try
        {
            localization.SetLanguage("zh-CN");
            Assert.Equal("无法读取“上路”路径的当前等级。", localization.FormatScriptError(exception));
            localization.SetLanguage("en-US");
            Assert.Equal("Failed to read the current 'Top' path level.", localization.FormatScriptError(exception));
        }
        finally
        {
            localization.SetLanguage(previousLanguage);
        }
    }

    [Theory]
    [InlineData("zh-CN", "第 5 步使用了不支持的指令类型“FutureCommand”。")]
    [InlineData("en-US", "Step 5 uses unsupported script command type 'FutureCommand'.")]
    public void StartupException_UsesMetadataWithoutChangingExceptionType(string language, string expected)
    {
        var localization = LocalizationService.Instance;
        var previousLanguage = localization.LanguageCode;
        var exception = ScriptExecutionException.WithLocalization(
            new InvalidDataException("Original unsupported command diagnostic."),
            "Editor.Runtime.Error.UnsupportedCommand", "FutureCommand", 5);
        try
        {
            localization.SetLanguage(language);
            Assert.Equal(expected, localization.FormatScriptError(exception));
            Assert.IsType<InvalidDataException>(exception);
            Assert.Equal("Original unsupported command diagnostic.", exception.Message);
        }
        finally
        {
            localization.SetLanguage(previousLanguage);
        }
    }

    [Fact]
    public void UnexpectedException_ShowsLocalizedFallback_AndKeepsDetailsInRuntimeLog()
    {
        var localization = LocalizationService.Instance;
        var previousLanguage = localization.LanguageCode;
        try
        {
            localization.SetLanguage("zh-CN");
            var viewModel = new ScriptExecutionWindowViewModel(
                localization, "Test", "test.btd", [], static (_, _) => Task.CompletedTask, static () => { });
            viewModel.ApplyUnexpectedException(new InvalidOperationException("Low-level English detail"));

            Assert.Contains("发生未预期的错误", viewModel.StatusText);
            Assert.DoesNotContain("Low-level English detail", viewModel.StatusText);
            Assert.Contains("Low-level English detail", viewModel.LogText);
        }
        finally
        {
            localization.SetLanguage(previousLanguage);
        }
    }
}
