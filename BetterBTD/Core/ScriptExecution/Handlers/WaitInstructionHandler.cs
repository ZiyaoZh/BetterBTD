using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class WaitInstructionHandler : ScriptInstructionHandlerBase
{
    private const int IndefiniteWaitTimeoutMilliseconds = int.MaxValue;
    private const int WaitPollIntervalMilliseconds = 100;

    public override ScriptCommandType CommandType => ScriptCommandType.Wait;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        var waitMode = instruction.WaitMode;

        switch (waitMode)
        {
            case nameof(WaitModeType.Time):
                await ScriptExecutionOperations.DelayAsync(
                    context,
                    instruction.WaitTimeMilliseconds,
                    "WaitTime",
                    cancellationToken).ConfigureAwait(false);
                break;
            case nameof(WaitModeType.Gold):
                await ScriptExecutionOperations.WaitUntilAsync(
                    context,
                    new ScriptWaitOptions
                    {
                        TimeoutMilliseconds = IndefiniteWaitTimeoutMilliseconds,
                        PollIntervalMilliseconds = WaitPollIntervalMilliseconds,
                        Description = $"gold >= {instruction.WaitGoldAmount}"
                    },
                    async token =>
                    {
                        var gold = await context.RuntimeServices.GameStageState
                            .GetGoldAsync(token)
                            .ConfigureAwait(false);
                        return gold.HasValue && gold.Value >= instruction.WaitGoldAmount;
                    },
                    cancellationToken).ConfigureAwait(false);
                break;
            case nameof(WaitModeType.Round):
                await ScriptExecutionOperations.WaitUntilAsync(
                    context,
                    new ScriptWaitOptions
                    {
                        TimeoutMilliseconds = IndefiniteWaitTimeoutMilliseconds,
                        PollIntervalMilliseconds = WaitPollIntervalMilliseconds,
                        Description = $"round >= {instruction.WaitRoundCount}"
                    },
                    async token =>
                    {
                        var round = await context.RuntimeServices.GameStageState
                            .GetRoundAsync(token)
                            .ConfigureAwait(false);
                        return round.HasValue && round.Value >= instruction.WaitRoundCount;
                    },
                    cancellationToken).ConfigureAwait(false);
                break;
            case nameof(WaitModeType.CoordinateColor):
            {
                if (!TryParseRgbHex(instruction.WaitColorHex, out var expectedR, out var expectedG, out var expectedB))
                {
                    throw ScriptInstructionHandlerSupport.CreateExecutionException(
                        context,
                        "WaitColorHex",
                        $"Unsupported wait color '{instruction.WaitColorHex}'. Expected format '#RRGGBB'.",
                        messageKey: "Editor.Runtime.Error.InvalidWaitColor",
                        messageArguments: [instruction.WaitColorHex]);
                }

                var targetCoordinate = new WpfPoint(instruction.WaitColorCoordinateX, instruction.WaitColorCoordinateY);
                await ScriptExecutionOperations.WaitUntilAsync(
                    context,
                    new ScriptWaitOptions
                    {
                        TimeoutMilliseconds = IndefiniteWaitTimeoutMilliseconds,
                        PollIntervalMilliseconds = WaitPollIntervalMilliseconds,
                        Description = $"color at {ScriptInstructionHandlerSupport.FormatPoint(targetCoordinate)} matches {instruction.WaitColorHex}"
                    },
                    token => context.RuntimeServices.GameStageState.IsCoordinateColorMatchAsync(
                        targetCoordinate,
                        expectedR,
                        expectedG,
                        expectedB,
                        instruction.WaitColorTolerance,
                        token),
                    cancellationToken).ConfigureAwait(false);
                break;
            }
            default:
                throw ScriptInstructionHandlerSupport.CreateExecutionException(
                    context,
                    "WaitMode",
                    $"Unsupported wait mode '{waitMode}'.",
                    messageKey: "Editor.Runtime.Error.UnsupportedWaitMode",
                    messageArguments: [waitMode]);
        }
    }

    private static bool TryParseRgbHex(string? value, out int r, out int g, out int b)
    {
        r = 0;
        g = 0;
        b = 0;

        var text = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length != 6)
        {
            return false;
        }

        if (!int.TryParse(text[..2], System.Globalization.NumberStyles.HexNumber, null, out r) ||
            !int.TryParse(text.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g) ||
            !int.TryParse(text.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
        {
            r = 0;
            g = 0;
            b = 0;
            return false;
        }

        return true;
    }
}
