using Fischless.GameCapture;

namespace BetterBTD.Models;

public sealed record class GameCaptureOptions
{
    public string CaptureModeName { get; init; } = nameof(CaptureModes.BitBlt);

    public bool AutoFixWin11BitBlt { get; init; }
}
