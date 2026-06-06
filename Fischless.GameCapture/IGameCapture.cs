using OpenCvSharp;

namespace Fischless.GameCapture;

public interface IGameCapture : IDisposable
{
    public bool IsCapturing { get; }

    public void Start(nint hWnd, Dictionary<string, object>? settings = null);

    public Mat? Capture();

    public void Stop();
}

public readonly record struct GameCaptureFrameMetadata(
    long SourceSequence,
    DateTimeOffset CapturedAt,
    ulong? NativeUpdateId = null);

public interface IGameCaptureFrameMetadataProvider
{
    bool TryGetFrameMetadata(out GameCaptureFrameMetadata metadata);
}
