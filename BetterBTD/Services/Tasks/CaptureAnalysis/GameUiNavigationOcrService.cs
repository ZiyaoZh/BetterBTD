using BetterBTD.Models;
using BetterBTD.Models.GameElements;
using OpenCvSharp;
using System.IO;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.CaptureAnalysis;

public sealed class GameUiNavigationOcrService
{
    private static readonly Lazy<GameUiNavigationOcrService> InstanceHolder = new(() => new GameUiNavigationOcrService());
    private static readonly double[] HeroThresholds = [0.92d, 0.88d, 0.84d, 0.80d];
    private static readonly double[] MapThresholds = [0.94d, 0.90d, 0.86d, 0.82d];
    private static readonly double[] ButtonThresholds = [0.94d, 0.90d, 0.86d, 0.82d];

    private readonly object _syncRoot = new();
    private readonly TemplateMatchService _templateMatchService;

    private IconTemplateRepository? _iconRepository;

    private GameUiNavigationOcrService()
    {
        _templateMatchService = TemplateMatchService.Instance;
    }

    public static GameUiNavigationOcrService Instance => InstanceHolder.Value;

    public bool IsAvailable => TryEnsureIconRepository(out _);

    public bool TryLocateHero(
        Mat frame,
        HeroType heroType,
        out WpfPoint centerPoint1080p)
    {
        return TryLocateHero(frame, heroType, frame.Width, frame.Height, 0, 0, out centerPoint1080p, out _);
    }

    public bool TryLocateHero(
        Mat captureRegion,
        HeroType heroType,
        int frameWidth,
        int frameHeight,
        out WpfPoint centerPoint1080p)
    {
        return TryLocateHero(captureRegion, heroType, frameWidth, frameHeight, 0, 0, out centerPoint1080p, out _);
    }

    public bool TryLocateHero(
        Mat captureRegion,
        HeroType heroType,
        int frameWidth,
        int frameHeight,
        int captureOffsetX,
        int captureOffsetY,
        out WpfPoint centerPoint1080p,
        out TemplateMatchInfo matchInfo)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        centerPoint1080p = default;
        matchInfo = default;

        if (captureRegion.Empty())
        {
            return false;
        }

        if (!TryEnsureIconRepository(out var repository))
        {
            return false;
        }

        var templates = repository.GetHeroTemplates(heroType, frameWidth, frameHeight);
        return GameOcrIconMatcher.TryLocateTargetIcon(
            _templateMatchService,
            captureRegion,
            templates,
            HeroThresholds,
            frameWidth,
            frameHeight,
            captureOffsetX,
            captureOffsetY,
            out centerPoint1080p,
            out matchInfo);
    }

    public bool TryLocateMap(
        Mat frame,
        GameMapType mapType,
        out WpfPoint centerPoint1080p)
    {
        return TryLocateMap(frame, mapType, frame.Width, frame.Height, 0, 0, out centerPoint1080p, out _);
    }

    public bool TryLocateMap(
        Mat captureRegion,
        GameMapType mapType,
        int frameWidth,
        int frameHeight,
        out WpfPoint centerPoint1080p)
    {
        return TryLocateMap(captureRegion, mapType, frameWidth, frameHeight, 0, 0, out centerPoint1080p, out _);
    }

    public bool TryLocateMap(
        Mat captureRegion,
        GameMapType mapType,
        int frameWidth,
        int frameHeight,
        int captureOffsetX,
        int captureOffsetY,
        out WpfPoint centerPoint1080p,
        out TemplateMatchInfo matchInfo)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        centerPoint1080p = default;
        matchInfo = default;

        if (captureRegion.Empty())
        {
            return false;
        }

        if (!TryEnsureIconRepository(out var repository))
        {
            return false;
        }

        if (!repository.TryGetMapTemplate(mapType, frameWidth, frameHeight, out var template))
        {
            return false;
        }

        return GameOcrIconMatcher.TryLocateTargetIcon(
            _templateMatchService,
            captureRegion,
            [template],
            MapThresholds,
            frameWidth,
            frameHeight,
            captureOffsetX,
            captureOffsetY,
            out centerPoint1080p,
            out matchInfo);
    }

    public bool TryLocateHomeButton(
        Mat frame,
        out WpfPoint centerPoint1080p)
    {
        return TryLocateHomeButton(frame, frame.Width, frame.Height, 0, 0, out centerPoint1080p, out _);
    }

    public bool TryLocateHomeButton(
        Mat captureRegion,
        int frameWidth,
        int frameHeight,
        out WpfPoint centerPoint1080p)
    {
        return TryLocateHomeButton(captureRegion, frameWidth, frameHeight, 0, 0, out centerPoint1080p, out _);
    }

    public bool TryLocateHomeButton(
        Mat captureRegion,
        int frameWidth,
        int frameHeight,
        int captureOffsetX,
        int captureOffsetY,
        out WpfPoint centerPoint1080p,
        out TemplateMatchInfo matchInfo)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        centerPoint1080p = default;
        matchInfo = default;

        if (captureRegion.Empty())
        {
            return false;
        }

        if (!TryEnsureIconRepository(out var repository))
        {
            return false;
        }

        if (!repository.TryGetButtonTemplate(UiNavigationButtonType.Home, frameWidth, frameHeight, out var template))
        {
            return false;
        }

        return GameOcrIconMatcher.TryLocateTargetIcon(
            _templateMatchService,
            captureRegion,
            [template],
            ButtonThresholds,
            frameWidth,
            frameHeight,
            captureOffsetX,
            captureOffsetY,
            out centerPoint1080p,
            out matchInfo);
    }

    private bool TryEnsureIconRepository(out IconTemplateRepository repository)
    {
        lock (_syncRoot)
        {
            if (_iconRepository is not null)
            {
                repository = _iconRepository;
                return true;
            }

            var assetRootPath = GameOcrSupport.BuildIconAssetRootPath();
            if (!Directory.Exists(assetRootPath))
            {
                repository = null!;
                return false;
            }

            try
            {
                _iconRepository = new IconTemplateRepository(assetRootPath);
                repository = _iconRepository;
                return true;
            }
            catch
            {
                repository = null!;
                return false;
            }
        }
    }
}
