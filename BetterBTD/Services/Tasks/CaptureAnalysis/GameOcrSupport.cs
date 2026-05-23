using System.Globalization;
using System.IO;
using BetterBTD.Models;
using BetterBTD.Models.GameElements;
using OpenCvSharp;
using OpenCvRect = OpenCvSharp.Rect;
using OpenCvSize = OpenCvSharp.Size;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.CaptureAnalysis;

internal static class GameOcrSupport
{
    internal static readonly OpenCvSize Reference720p = new(1280, 720);
    internal static readonly OpenCvSize Reference1080p = new(1920, 1080);

    internal static void ValidateFrameSize(int frameWidth, int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
    }

    internal static WpfPoint ToReference1080p(WpfPoint point, int frameWidth, int frameHeight)
    {
        ValidateFrameSize(frameWidth, frameHeight);
        return new WpfPoint(
            point.X * Reference1080p.Width / frameWidth,
            point.Y * Reference1080p.Height / frameHeight);
    }

    internal static string BuildDigitAssetRootPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "OcrDigits");
    }

    internal static string BuildIconAssetRootPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "OcrIcons");
    }

    internal static RawTemplate LoadTemplate(string fullPath, string symbol)
    {
        var image = Cv2.ImRead(fullPath, ImreadModes.Unchanged);
        if (image.Empty())
        {
            throw new FileNotFoundException($"Failed to load OCR template '{fullPath}'.", fullPath);
        }

        using var alphaMask = ExtractAlphaMask(image);
        var cropBounds = FindOpaqueBounds(alphaMask);
        var boundedImage = new Mat(image, cropBounds).Clone();
        using var boundedMaskSource = new Mat(alphaMask, cropBounds);
        var boundedMask = boundedMaskSource.Clone();
        return new RawTemplate(symbol, boundedImage, boundedMask);
    }

    private static Mat ExtractAlphaMask(Mat image)
    {
        if (image.Channels() < 4)
        {
            var mask = image.Channels() == 1
                ? image.Clone()
                : new Mat();

            if (image.Channels() == 3)
            {
                Cv2.CvtColor(image, mask, ColorConversionCodes.BGR2GRAY);
            }

            Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary);
            return mask;
        }

        var alphaMask = new Mat();
        Cv2.ExtractChannel(image, alphaMask, 3);
        Cv2.Threshold(alphaMask, alphaMask, 0, 255, ThresholdTypes.Binary);
        return alphaMask;
    }

    private static OpenCvRect FindOpaqueBounds(Mat mask)
    {
        using var maskCopy = mask.Clone();
        Cv2.FindContours(maskCopy, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
        {
            return new OpenCvRect(0, 0, mask.Width, mask.Height);
        }

        var left = mask.Width;
        var top = mask.Height;
        var right = 0;
        var bottom = 0;

        foreach (var contour in contours)
        {
            var bounds = Cv2.BoundingRect(contour);
            left = Math.Min(left, bounds.X);
            top = Math.Min(top, bounds.Y);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }

        return new OpenCvRect(left, top, right - left, bottom - top);
    }
}

internal static class GameOcrIconMatcher
{
    internal static bool TryLocateTargetIcon(
        TemplateMatchService templateMatchService,
        Mat captureRegion,
        IReadOnlyList<PreparedTemplate> templates,
        IReadOnlyList<double> thresholds,
        int frameWidth,
        int frameHeight,
        int captureOffsetX,
        int captureOffsetY,
        out WpfPoint centerPoint1080p,
        out TemplateMatchInfo matchInfo)
    {
        centerPoint1080p = default;
        matchInfo = default;

        GameOcrSupport.ValidateFrameSize(frameWidth, frameHeight);

        if (templates.Count == 0)
        {
            return false;
        }

        foreach (var threshold in thresholds)
        {
            if (!TryFindBestTemplateMatch(templateMatchService, captureRegion, templates, threshold, out var localMatchInfo))
            {
                continue;
            }

            matchInfo = new TemplateMatchInfo(
                captureOffsetX + localMatchInfo.X,
                captureOffsetY + localMatchInfo.Y,
                localMatchInfo.Width,
                localMatchInfo.Height,
                localMatchInfo.Score,
                localMatchInfo.Threshold);
            centerPoint1080p = GameOcrSupport.ToReference1080p(matchInfo.Center, frameWidth, frameHeight);
            return true;
        }

        return false;
    }

    private static bool TryFindBestTemplateMatch(
        TemplateMatchService templateMatchService,
        Mat captureRegion,
        IReadOnlyList<PreparedTemplate> templates,
        double threshold,
        out TemplateMatchInfo matchInfo)
    {
        matchInfo = default;
        var found = false;

        foreach (var template in templates)
        {
            if (captureRegion.Width < template.Width || captureRegion.Height < template.Height)
            {
                continue;
            }

            var candidate = templateMatchService.Match(captureRegion, template.Image, template.Mask, threshold);
            if (!candidate.IsMatch)
            {
                continue;
            }

            if (!found || candidate.Score > matchInfo.Score)
            {
                matchInfo = candidate;
                found = true;
            }
        }

        return found;
    }
}

internal enum OcrValueType
{
    Gold,
    Round
}

internal enum TemplateResolution
{
    Resolution1080p,
    Resolution720p
}

internal enum UiNavigationButtonType
{
    Home
}

internal sealed class PreparedTemplate
{
    public PreparedTemplate(string symbol, Mat image, Mat mask)
    {
        Symbol = symbol;
        Image = image;
        Mask = mask;
    }

    public string Symbol { get; }

    public Mat Image { get; }

    public Mat Mask { get; }

    public int Width => Image.Width;

    public int Height => Image.Height;
}

internal sealed class RawTemplate
{
    public RawTemplate(string symbol, Mat image, Mat mask)
    {
        Symbol = symbol;
        Image = image;
        Mask = mask;
    }

    public string Symbol { get; }

    public Mat Image { get; }

    public Mat Mask { get; }

    public PreparedTemplate Scale(double scaleX, double scaleY)
    {
        var width = Math.Max(1, (int)Math.Round(Image.Width * scaleX));
        var height = Math.Max(1, (int)Math.Round(Image.Height * scaleY));
        var interpolation = scaleX < 1d || scaleY < 1d
            ? InterpolationFlags.Area
            : InterpolationFlags.Linear;

        var scaledImage = new Mat();
        var scaledMask = new Mat();
        Cv2.Resize(Image, scaledImage, new OpenCvSize(width, height), 0d, 0d, interpolation);
        Cv2.Resize(Mask, scaledMask, new OpenCvSize(width, height), 0d, 0d, InterpolationFlags.Nearest);
        Cv2.Threshold(scaledMask, scaledMask, 0, 255, ThresholdTypes.Binary);
        return new PreparedTemplate(Symbol, scaledImage, scaledMask);
    }
}

internal sealed class TemplateCatalog
{
    public TemplateCatalog(
        TemplateResolution resolution,
        OpenCvSize baseResolution,
        IReadOnlyList<RawTemplate> goldDigits,
        IReadOnlyList<RawTemplate> roundDigits,
        RawTemplate slash)
    {
        Resolution = resolution;
        BaseResolution = baseResolution;
        GoldDigits = goldDigits;
        RoundDigits = roundDigits;
        Slash = slash;
    }

    public TemplateResolution Resolution { get; }

    public OpenCvSize BaseResolution { get; }

    public IReadOnlyList<RawTemplate> GoldDigits { get; }

    public IReadOnlyList<RawTemplate> RoundDigits { get; }

    public RawTemplate Slash { get; }
}

internal sealed class DigitTemplateRepository
{
    private readonly object _cacheSyncRoot = new();
    private readonly TemplateCatalog _catalog1080;
    private readonly TemplateCatalog _catalog720;
    private readonly Dictionary<string, IReadOnlyList<PreparedTemplate>> _digitCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PreparedTemplate> _slashCache = new(StringComparer.Ordinal);

    public DigitTemplateRepository(string assetRootPath)
    {
        _catalog1080 = LoadCatalog(assetRootPath, TemplateResolution.Resolution1080p, GameOcrSupport.Reference1080p, "1080p");
        _catalog720 = LoadCatalog(assetRootPath, TemplateResolution.Resolution720p, GameOcrSupport.Reference720p, "720p");
    }

    public IReadOnlyList<PreparedTemplate> GetDigitTemplates(OcrValueType valueType, int frameWidth, int frameHeight)
    {
        var catalog = SelectCatalog(frameWidth, frameHeight);
        var cacheKey = $"{catalog.Resolution}:{valueType}:{frameWidth}x{frameHeight}";

        lock (_cacheSyncRoot)
        {
            if (_digitCache.TryGetValue(cacheKey, out var cachedTemplates))
            {
                return cachedTemplates;
            }

            var scaleX = frameWidth / (double)catalog.BaseResolution.Width;
            var scaleY = frameHeight / (double)catalog.BaseResolution.Height;
            var sourceTemplates = valueType == OcrValueType.Gold
                ? catalog.GoldDigits
                : catalog.RoundDigits;

            var scaledTemplates = sourceTemplates
                .Select(x => x.Scale(scaleX, scaleY))
                .ToArray();

            _digitCache[cacheKey] = scaledTemplates;
            return scaledTemplates;
        }
    }

    public PreparedTemplate GetSlashTemplate(int frameWidth, int frameHeight)
    {
        var catalog = SelectCatalog(frameWidth, frameHeight);
        var cacheKey = $"{catalog.Resolution}:slash:{frameWidth}x{frameHeight}";

        lock (_cacheSyncRoot)
        {
            if (_slashCache.TryGetValue(cacheKey, out var cachedTemplate))
            {
                return cachedTemplate;
            }

            var scaleX = frameWidth / (double)catalog.BaseResolution.Width;
            var scaleY = frameHeight / (double)catalog.BaseResolution.Height;
            var scaledTemplate = catalog.Slash.Scale(scaleX, scaleY);
            _slashCache[cacheKey] = scaledTemplate;
            return scaledTemplate;
        }
    }

    private TemplateCatalog SelectCatalog(int frameWidth, int frameHeight)
    {
        var distance1080 = CalculateResolutionDistance(frameWidth, frameHeight, _catalog1080.BaseResolution);
        var distance720 = CalculateResolutionDistance(frameWidth, frameHeight, _catalog720.BaseResolution);
        return distance1080 <= distance720 ? _catalog1080 : _catalog720;
    }

    private static double CalculateResolutionDistance(int width, int height, OpenCvSize baseResolution)
    {
        var scaleX = width / (double)baseResolution.Width;
        var scaleY = height / (double)baseResolution.Height;
        return Math.Abs(Math.Log(scaleX)) + Math.Abs(Math.Log(scaleY));
    }

    private static TemplateCatalog LoadCatalog(
        string assetRootPath,
        TemplateResolution resolution,
        OpenCvSize baseResolution,
        string relativeFolder)
    {
        var goldDigits = Enumerable
            .Range(0, 10)
            .Select(index => GameOcrSupport.LoadTemplate(Path.Combine(assetRootPath, relativeFolder, "gold", $"{index}.png"), index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        var roundDigits = Enumerable
            .Range(0, 10)
            .Select(index => GameOcrSupport.LoadTemplate(Path.Combine(assetRootPath, relativeFolder, "round", $"{index}.png"), index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        var slash = GameOcrSupport.LoadTemplate(Path.Combine(assetRootPath, relativeFolder, "round", "slash.png"), "/");
        return new TemplateCatalog(resolution, baseResolution, goldDigits, roundDigits, slash);
    }
}

internal sealed class IconTemplateRepository
{
    private readonly object _cacheSyncRoot = new();
    private readonly Dictionary<HeroType, IReadOnlyList<RawTemplate>> _heroTemplates;
    private readonly Dictionary<GameMapType, RawTemplate> _mapTemplates;
    private readonly Dictionary<UiNavigationButtonType, RawTemplate> _buttonTemplates;
    private readonly Dictionary<string, IReadOnlyList<PreparedTemplate>> _heroCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PreparedTemplate> _mapCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PreparedTemplate> _buttonCache = new(StringComparer.Ordinal);

    public IconTemplateRepository(string assetRootPath)
    {
        _heroTemplates = LoadHeroTemplates(assetRootPath);
        _mapTemplates = LoadMapTemplates(assetRootPath);
        _buttonTemplates = LoadButtonTemplates(assetRootPath);
    }

    public IReadOnlyList<PreparedTemplate> GetHeroTemplates(HeroType heroType, int frameWidth, int frameHeight)
    {
        if (!_heroTemplates.TryGetValue(heroType, out var rawTemplates) || rawTemplates.Count == 0)
        {
            return [];
        }

        var cacheKey = $"hero:{heroType}:{frameWidth}x{frameHeight}";
        lock (_cacheSyncRoot)
        {
            if (_heroCache.TryGetValue(cacheKey, out var cachedTemplates))
            {
                return cachedTemplates;
            }

            var scaleX = frameWidth / (double)GameOcrSupport.Reference1080p.Width;
            var scaleY = frameHeight / (double)GameOcrSupport.Reference1080p.Height;
            var scaledTemplates = rawTemplates
                .Select(x => x.Scale(scaleX, scaleY))
                .ToArray();
            _heroCache[cacheKey] = scaledTemplates;
            return scaledTemplates;
        }
    }

    public bool TryGetMapTemplate(GameMapType mapType, int frameWidth, int frameHeight, out PreparedTemplate template)
    {
        template = null!;

        if (!_mapTemplates.TryGetValue(mapType, out var rawTemplate))
        {
            return false;
        }

        var cacheKey = $"map:{mapType}:{frameWidth}x{frameHeight}";
        lock (_cacheSyncRoot)
        {
            if (_mapCache.TryGetValue(cacheKey, out var cachedTemplate))
            {
                template = cachedTemplate;
                return true;
            }

            var scaleX = frameWidth / (double)GameOcrSupport.Reference1080p.Width;
            var scaleY = frameHeight / (double)GameOcrSupport.Reference1080p.Height;
            var scaledTemplate = rawTemplate.Scale(scaleX, scaleY);
            _mapCache[cacheKey] = scaledTemplate;
            template = scaledTemplate;
            return true;
        }
    }

    public bool TryGetButtonTemplate(UiNavigationButtonType buttonType, int frameWidth, int frameHeight, out PreparedTemplate template)
    {
        template = null!;

        if (!_buttonTemplates.TryGetValue(buttonType, out var rawTemplate))
        {
            return false;
        }

        var cacheKey = $"button:{buttonType}:{frameWidth}x{frameHeight}";
        lock (_cacheSyncRoot)
        {
            if (_buttonCache.TryGetValue(cacheKey, out var cachedTemplate))
            {
                template = cachedTemplate;
                return true;
            }

            var scaleX = frameWidth / (double)GameOcrSupport.Reference1080p.Width;
            var scaleY = frameHeight / (double)GameOcrSupport.Reference1080p.Height;
            var scaledTemplate = rawTemplate.Scale(scaleX, scaleY);
            _buttonCache[cacheKey] = scaledTemplate;
            template = scaledTemplate;
            return true;
        }
    }

    private static Dictionary<HeroType, IReadOnlyList<RawTemplate>> LoadHeroTemplates(string assetRootPath)
    {
        var heroRoot = Path.Combine(assetRootPath, "Heroes", "1080p");
        var result = new Dictionary<HeroType, IReadOnlyList<RawTemplate>>();

        foreach (var hero in Enum.GetValues<HeroType>())
        {
            var directoryPath = Path.Combine(heroRoot, hero.ToString());
            if (!Directory.Exists(directoryPath))
            {
                result[hero] = [];
                continue;
            }

            var templates = Directory
                .EnumerateFiles(directoryPath, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .Select(path => GameOcrSupport.LoadTemplate(path, $"{hero}:{Path.GetFileNameWithoutExtension(path)}"))
                .ToArray();
            result[hero] = templates;
        }

        return result;
    }

    private static Dictionary<GameMapType, RawTemplate> LoadMapTemplates(string assetRootPath)
    {
        var mapRoot = Path.Combine(assetRootPath, "Maps", "1080p");
        var result = new Dictionary<GameMapType, RawTemplate>();

        foreach (var map in Enum.GetValues<GameMapType>())
        {
            var filePath = Path.Combine(mapRoot, $"{map}.png");
            if (!File.Exists(filePath))
            {
                continue;
            }

            result[map] = GameOcrSupport.LoadTemplate(filePath, map.ToString());
        }

        return result;
    }

    private static Dictionary<UiNavigationButtonType, RawTemplate> LoadButtonTemplates(string assetRootPath)
    {
        var buttonRoot = Path.Combine(assetRootPath, "Buttons", "1080p");
        var result = new Dictionary<UiNavigationButtonType, RawTemplate>();

        foreach (var button in Enum.GetValues<UiNavigationButtonType>())
        {
            var filePath = Path.Combine(buttonRoot, $"{button}.png");
            if (!File.Exists(filePath))
            {
                continue;
            }

            result[button] = GameOcrSupport.LoadTemplate(filePath, button.ToString());
        }

        return result;
    }
}
