using BetterBTD.Core.ScriptExecution.Handlers;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Shell.Localization;
using BetterBTD.ViewModels;

namespace BetterBTD.Tests.ScriptExecution;

public sealed class ScriptExecutionIntervalStrategyTests
{
    [Fact]
    public void ResolveInstructionIntervalMilliseconds_CustomStrategy_UsesExplicitInstructionOverride()
    {
        var step = new ScriptTaskFlowStep
        {
            Index = 0,
            CommandType = ScriptCommandType.MouseClick,
            Instruction = new ScriptInstructionDocument
            {
                IntervalToNextInstructionMs = 180
            }
        };

        var options = new ScriptExecutionOptions
        {
            OverrideInstructionIntervalMs = 240
        };

        var interval = ScriptInstructionHandlerSupport.ResolveInstructionIntervalMilliseconds(step, options);

        Assert.Equal(240, interval);
    }

    [Fact]
    public void ResolveInstructionIntervalMilliseconds_CommonStrategy_UsesCommonInterval()
    {
        var step = new ScriptTaskFlowStep
        {
            Index = 0,
            CommandType = ScriptCommandType.MouseClick,
            Instruction = new ScriptInstructionDocument
            {
                IntervalToNextInstructionMs = 180
            }
        };

        var options = new ScriptExecutionOptions
        {
            IntervalStrategy = ScriptExecutionOperationIntervalStrategy.CommonOperationInterval,
            CommonOperationIntervalMs = 360
        };

        var interval = ScriptInstructionHandlerSupport.ResolveInstructionIntervalMilliseconds(step, options);

        Assert.Equal(360, interval);
    }

    [Fact]
    public void ResolveOperationIntervalMilliseconds_CommonStrategy_ClampsToSupportedRange()
    {
        var options = new ScriptExecutionOptions
        {
            IntervalStrategy = ScriptExecutionOperationIntervalStrategy.CommonOperationInterval,
            CommonOperationIntervalMs = 10
        };

        var interval = ScriptInstructionHandlerSupport.ResolveOperationIntervalMilliseconds(options, 280);

        Assert.Equal(50, interval);
    }

    [Fact]
    public void ScriptExecutionWindowViewModel_DefaultsToInstructionCustomStrategy()
    {
        var viewModel = new ScriptExecutionWindowViewModel(
            LocalizationService.Instance,
            "Test Script",
            "test.btd",
            [],
            static (_, _) => Task.CompletedTask,
            static () => { });

        Assert.Equal(
            ScriptExecutionOperationIntervalStrategy.InstructionCustom,
            viewModel.SelectedIntervalStrategyValue);
        Assert.False(viewModel.ShowCommonOperationInterval);
    }

    [Fact]
    public void ScriptExecutionWindowViewModel_CommonStrategySelection_ShowsSliderAndClampsValue()
    {
        var viewModel = new ScriptExecutionWindowViewModel(
            LocalizationService.Instance,
            "Test Script",
            "test.btd",
            [],
            static (_, _) => Task.CompletedTask,
            static () => { });

        viewModel.SelectedIntervalStrategy = viewModel.IntervalStrategies[1];
        viewModel.CommonOperationIntervalMs = 5000;

        Assert.Equal(
            ScriptExecutionOperationIntervalStrategy.CommonOperationInterval,
            viewModel.SelectedIntervalStrategyValue);
        Assert.True(viewModel.ShowCommonOperationInterval);
        Assert.Equal(1000, viewModel.CommonOperationIntervalMs);
    }
}
