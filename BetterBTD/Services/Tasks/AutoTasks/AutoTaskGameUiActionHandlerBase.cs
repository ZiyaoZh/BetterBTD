using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Core.Config;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using BetterBTD.Services.Tasks.Input;
using OpenCvRect = OpenCvSharp.Rect;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.AutoTasks;

internal abstract class AutoTaskGameUiActionHandlerBase : IGameUiTaskActionHandler
{
    private static readonly WpfPoint HeroSelectionScrollPoint = new(250, 560);

    protected AutoTaskGameUiActionHandlerBase(
        ScriptInputSimulationService inputSimulationService,
        GameCaptureService gameCaptureService,
        GameUiNavigationOcrService navigationOcrService)
    {
        InputSimulationService = inputSimulationService ?? throw new ArgumentNullException(nameof(inputSimulationService));
        GameCaptureService = gameCaptureService ?? throw new ArgumentNullException(nameof(gameCaptureService));
        NavigationOcrService = navigationOcrService ?? throw new ArgumentNullException(nameof(navigationOcrService));
    }

    public abstract AutoTaskKind Kind { get; }

    protected ScriptInputSimulationService InputSimulationService { get; }

    protected GameCaptureService GameCaptureService { get; }

    protected GameUiNavigationOcrService NavigationOcrService { get; }

    public abstract Task<GameUiActionExecutionResult> ExecuteAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default);

    protected GameUiActionExecutionResult Click(
        GameUiNavigationStep step,
        WpfPoint scriptPoint,
        string message)
    {
        InputSimulationService.PrepareTargetWindowForInput();
        InputSimulationService.ClickMouseAtScriptCoordinate(scriptPoint);
        return Success(step, message, step.PostActionDelayMs);
    }

    protected GameUiActionExecutionResult PressEscape(
        GameUiNavigationStep step,
        string message)
    {
        InputSimulationService.PrepareTargetWindowForInput();
        InputSimulationService.PressKey(KeyId.Escape);
        return Success(step, message, step.PostActionDelayMs);
    }

    protected static GameUiActionExecutionResult Success(
        GameUiNavigationStep step,
        string message,
        int delayMs)
    {
        return new GameUiActionExecutionResult
        {
            Succeeded = true,
            Message = string.IsNullOrWhiteSpace(message) ? step.Description : message,
            RecommendedDelayMs = delayMs > 0 ? delayMs : step.PostActionDelayMs
        };
    }

    protected async Task<GameUiActionExecutionResult> ExecuteHeroSelectionAsync(
        GameUiNavigationStep step,
        HeroType hero,
        Action markHeroSelected,
        string successMessage,
        string searchContinuationMessage,
        CancellationToken cancellationToken)
    {
        if (!GameCaptureService.TryCaptureFrame(out _, out var frame))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = "Failed to capture the hero selection screen.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        using (frame)
        {
            if (NavigationOcrService.TryLocateHero(frame, hero, out var heroPoint))
            {
                InputSimulationService.PrepareTargetWindowForInput();
                InputSimulationService.ClickMouseAtScriptCoordinate(heroPoint);
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                InputSimulationService.ClickMouseAtScriptCoordinate(new WpfPoint(1120, 620));
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                InputSimulationService.ClickMouseAtScriptCoordinate(new WpfPoint(80, 55));
                markHeroSelected();
                return Success(step, successMessage, 800);
            }
        }

        InputSimulationService.PrepareTargetWindowForInput();
        InputSimulationService.MoveMouseToScriptCoordinate(HeroSelectionScrollPoint);
        InputSimulationService.ScrollMouseWheelVertical(-50);
        return Success(step, searchContinuationMessage, 600);
    }

    protected async Task<GameUiActionExecutionResult> ExecuteDefeatReturnAsync(
        GameUiNavigationStep step,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return await ExecuteHomeButtonClickAsync(
                step,
                snapshot,
                "defeat screen",
                "Returned to the main menu after defeat.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    protected async Task<GameUiActionExecutionResult> ExecuteHomeButtonClickAsync(
        GameUiNavigationStep step,
        GameUiSnapshot snapshot,
        string screenDescription,
        string successMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetHomeButtonPoint(snapshot, out var homeButtonPoint))
        {
            return Click(step, homeButtonPoint, successMessage);
        }

        if (!GameCaptureService.TryCaptureFrame(out _, out var frame))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = $"Failed to capture the {screenDescription} for home button recognition.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        using (frame)
        {
            if (NavigationOcrService.TryLocateHomeButton(frame, out var recognizedHomeButtonPoint))
            {
                return Click(step, recognizedHomeButtonPoint, successMessage);
            }
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return new GameUiActionExecutionResult
        {
            Succeeded = false,
            Message = $"Failed to locate the {screenDescription} home button.",
            RecommendedDelayMs = step.PostActionDelayMs
        };
    }

    protected async Task OpenChestsAsync(
        IReadOnlyList<WpfPoint> chestPoints,
        int reopenDelayMs,
        int betweenChestDelayMs,
        CancellationToken cancellationToken)
    {
        InputSimulationService.PrepareTargetWindowForInput();

        foreach (var chestPoint in chestPoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputSimulationService.ClickMouseAtScriptCoordinate(chestPoint);
            await Task.Delay(reopenDelayMs, cancellationToken).ConfigureAwait(false);
            InputSimulationService.ClickMouseAtScriptCoordinate(chestPoint);
            await Task.Delay(betweenChestDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    protected static bool TryGetModeSelectionPoint(StageMode mode, out WpfPoint point)
    {
        switch (mode)
        {
            case StageMode.Standard:
                point = new WpfPoint(630, 590);
                return true;
            case StageMode.PrimaryOnly:
                point = new WpfPoint(960, 450);
                return true;
            case StageMode.Deflation:
                point = new WpfPoint(1300, 450);
                return true;
            case StageMode.MilitaryOnly:
                point = new WpfPoint(960, 450);
                return true;
            case StageMode.Apopalypse:
                point = new WpfPoint(1300, 450);
                return true;
            case StageMode.Reverse:
                point = new WpfPoint(960, 750);
                return true;
            case StageMode.MagicOnly:
                point = new WpfPoint(960, 450);
                return true;
            case StageMode.DoubleHpMoabs:
                point = new WpfPoint(1300, 450);
                return true;
            case StageMode.HalfCash:
                point = new WpfPoint(1600, 450);
                return true;
            case StageMode.AlternateBloonsRounds:
                point = new WpfPoint(960, 750);
                return true;
            case StageMode.Impoppable:
                point = new WpfPoint(1300, 750);
                return true;
            case StageMode.CHIMPS:
                point = new WpfPoint(1600, 750);
                return true;
            default:
                point = default;
                return false;
        }
    }

    protected static OpenCvRect ScaleReferenceRect(OpenCvRect referenceRect, int frameWidth, int frameHeight)
    {
        var x = (int)Math.Round(referenceRect.X / 1920d * frameWidth);
        var y = (int)Math.Round(referenceRect.Y / 1080d * frameHeight);
        var right = (int)Math.Round(referenceRect.Right / 1920d * frameWidth);
        var bottom = (int)Math.Round(referenceRect.Bottom / 1080d * frameHeight);

        x = Math.Clamp(x, 0, Math.Max(0, frameWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frameHeight - 1));
        right = Math.Clamp(right, x + 1, frameWidth);
        bottom = Math.Clamp(bottom, y + 1, frameHeight);

        return new OpenCvRect(x, y, right - x, bottom - y);
    }

    private static bool TryGetHomeButtonPoint(GameUiSnapshot snapshot, out WpfPoint point)
    {
        if (snapshot.Facts.TryGetValue("homeButtonPoint1080p", out var rawPoint) && rawPoint is WpfPoint typedPoint)
        {
            point = typedPoint;
            return true;
        }

        point = default;
        return false;
    }
}
