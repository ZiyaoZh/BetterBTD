using Fischless.GameCapture;

namespace BetterBTD.Models;

public sealed record class GameCaptureOptions
{
    public string CaptureModeName { get; init; } = nameof(CaptureModes.WindowsGraphicsCapture);

    public int CaptureIntervalMs { get; init; } = 50;

    public bool AutoFixWin11BitBlt { get; init; }
}
