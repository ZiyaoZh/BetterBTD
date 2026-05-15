using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution.Handlers;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;
using BetterBTD.Tests.TestDoubles;
using System.Windows.Input;

namespace BetterBTD.Tests.ScriptExecution.Handlers;

public sealed class NextRoundInstructionHandlerTests
{
    [Fact]
    public async Task HandleAsync_SendNextRound_RepeatsHotkeyExpectedTimes()
    {
        var keyBindings = ConfigurationService.Instance.Current.KeyBindings;
        var original = new HotkeyBinding
        {
            Modifiers = keyBindings.General.SendNextRound.Modifiers,
            Key = keyBindings.General.SendNextRound.Key
        };

        keyBindings.General.SendNextRound = new HotkeyBinding
        {
            Modifiers = ModifierKeys.Control,
            Key = KeyId.Space
        };

        try
        {
            var input = new RecordingScriptInputService();
            var runtimeServices = new ScriptExecutionRuntimeServices
            {
                Capture = new NullScriptCaptureService(),
                Input = input,
                GameStageState = new QueueGameStageStateService(Array.Empty<GameStageStateSnapshot?>())
            };

            var instruction = new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.NextRound.ToString(),
                NextRoundAction = "SendNextRound",
                NextRoundSendCount = 3,
                NextRoundOperationIntervalMilliseconds = 200
            };

            var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices);
            var handler = new NextRoundInstructionHandler();

            await handler.HandleAsync(context, CancellationToken.None);

            Assert.Equal(new[] { KeyId.LeftCtrl }, input.KeyDownEvents);
            Assert.Equal(new[] { KeyId.LeftCtrl }, input.KeyUpEvents);
            Assert.Equal(new[] { KeyId.Space, KeyId.Space, KeyId.Space }, input.PressedKeys);
        }
        finally
        {
            keyBindings.General.SendNextRound = original;
        }
    }
}
