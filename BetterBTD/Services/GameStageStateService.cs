using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptExecution;
using OpenCvSharp;

namespace BetterBTD.Services;

public sealed class GameStageStateService : IGameStageStateService
{
    private static readonly Lazy<GameStageStateService> InstanceHolder = new(() => new GameStageStateService());

    private readonly GameCaptureService _gameCaptureService;
    private readonly GameTargetOcrService _gameTargetOcrService;

    private GameStageStateService()
    {
        _gameCaptureService = GameCaptureService.Instance;
        _gameTargetOcrService = GameTargetOcrService.Instance;
    }

    public static GameStageStateService Instance => InstanceHolder.Value;

    public bool IsAvailable => _gameTargetOcrService.IsAvailable;

    public Task<GameStageStateSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TryCaptureSnapshot(out var snapshot, out _) ? snapshot : null);
    }

    public bool TryCaptureSnapshot(out GameStageStateSnapshot snapshot)
    {
        return TryCaptureSnapshot(out snapshot, out _);
    }

    public bool TryCaptureSnapshot(out GameStageStateSnapshot snapshot, out string failureMessage)
    {
        snapshot = new GameStageStateSnapshot();
        failureMessage = "Capture frame unavailable.";

        if (!_gameCaptureService.TryCaptureFrame(out var frame))
        {
            return false;
        }

        using (frame)
        {
            return TryCaptureSnapshot(frame, out snapshot, out failureMessage);
        }
    }

    public bool TryCaptureSnapshot(Mat frame, out GameStageStateSnapshot snapshot)
    {
        return TryCaptureSnapshot(frame, out snapshot, out _);
    }

    public bool TryCaptureSnapshot(Mat frame, out GameStageStateSnapshot snapshot, out string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Empty())
        {
            snapshot = new GameStageStateSnapshot();
            failureMessage = "Source frame is empty.";
            return false;
        }

        var hasGold = _gameTargetOcrService.TryReadGold(frame, out var gold);
        var hasRound = _gameTargetOcrService.TryReadRound(frame, out var round);

        snapshot = new GameStageStateSnapshot
        {
            IsInLevel = DetectIsInLevel(frame),
            Gold = hasGold ? gold : null,
            Round = hasRound ? round : null,
            RightUpgradePanel = ReadRightUpgradePanelState(frame),
            LeftUpgradePanel = ReadLeftUpgradePanelState(frame),
            IsPlacingMonkey = DetectIsPlacingMonkey(frame),
            CanPlaceHero = DetectCanPlaceHero(frame),
            StageTarget = string.Empty
        };

        failureMessage = hasGold || hasRound
            ? string.Empty
            : "Gold/Round OCR failed.";

        return hasGold || hasRound;
    }

    private static bool? DetectIsInLevel(Mat frame)
    {
        _ = frame;
        return null;
    }

    private static GameStageUpgradePanelState ReadRightUpgradePanelState(Mat frame)
    {
        _ = frame;
        return GameStageUpgradePanelState.Empty;
    }

    private static GameStageUpgradePanelState ReadLeftUpgradePanelState(Mat frame)
    {
        _ = frame;
        return GameStageUpgradePanelState.Empty;
    }

    private static bool? DetectIsPlacingMonkey(Mat frame)
    {
        _ = frame;
        return null;
    }

    private static bool? DetectCanPlaceHero(Mat frame)
    {
        _ = frame;
        return null;
    }
}
