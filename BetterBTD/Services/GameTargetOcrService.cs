using System.IO;
using System.Globalization;
using System.Text;
using OpenCvSharp;
using OpenCvRect = OpenCvSharp.Rect;
using OpenCvSize = OpenCvSharp.Size;

namespace BetterBTD.Services;

public sealed class GameTargetOcrService
{
    private static readonly Lazy<GameTargetOcrService> InstanceHolder = new(() => new GameTargetOcrService());
    private static readonly double[] GoldThresholds = [0.90d, 0.84d, 0.78d];
    private static readonly double[] RoundThresholds = [0.90d, 0.84d, 0.78d];
    private const double GoldOneScoreDelta = 0.04d;
    private const int GoldOneTopTolerance = 2;
    private static readonly OpenCvSize Reference720p = new(1280, 720);
    private static readonly OpenCvSize Reference1080p = new(1920, 1080);

    private readonly object _syncRoot = new();
    private readonly TemplateMatchService _templateMatchService;

    private DigitTemplateRepository? _repository;
    private string? _repositoryUnavailableReason;

    private GameTargetOcrService()
    {
        _templateMatchService = TemplateMatchService.Instance;
    }

    public static GameTargetOcrService Instance => InstanceHolder.Value;

    public bool IsAvailable => TryEnsureRepository(out _);

    public bool TryReadGold(Mat captureRegion, int frameWidth, int frameHeight, out int gold)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        gold = 0;

        if (captureRegion.Empty())
        {
            return false;
        }

        if (frameWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        }

        if (frameHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameHeight));
        }

        if (!TryEnsureRepository(out var repository))
        {
            return false;
        }

        var templates = repository.GetDigitTemplates(OcrValueType.Gold, frameWidth, frameHeight);

        if (!TryRecognizeDigits(captureRegion, templates, GoldThresholds, out var text))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out gold);
    }

    private bool TryReadGold(Mat captureRegion, int frameWidth, int frameHeight, out int gold, out string diagnostics)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        gold = 0;

        if (!TryEnsureRepository(out var repository))
        {
            diagnostics = BuildRepositoryUnavailableDiagnostics("Gold");
            return false;
        }

        var templates = repository.GetDigitTemplates(OcrValueType.Gold, frameWidth, frameHeight);
        var templateSelection = repository.GetSelectionDescription(frameWidth, frameHeight);

        if (!TryRecognizeDigits(captureRegion, templates, GoldThresholds, out var text, out var recognitionDiagnostics))
        {
            diagnostics = $"Gold: failed | RegionSize={captureRegion.Width}x{captureRegion.Height} | Templates={templateSelection} | {recognitionDiagnostics}";
            return false;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out gold))
        {
            diagnostics = $"Gold: parse failed | RegionSize={captureRegion.Width}x{captureRegion.Height} | Templates={templateSelection} | Text='{text}' | {recognitionDiagnostics}";
            return false;
        }

        diagnostics = $"Gold: success | Value={gold} | RegionSize={captureRegion.Width}x{captureRegion.Height} | Templates={templateSelection} | Text='{text}' | {recognitionDiagnostics}";
        return true;
    }

    public bool TryReadRound(Mat captureRegion, int frameWidth, int frameHeight, out int round)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        round = 0;

        if (captureRegion.Empty())
        {
            return false;
        }

        if (frameWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        }

        if (frameHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameHeight));
        }

        if (!TryEnsureRepository(out var repository))
        {
            return false;
        }

        var digitTemplates = repository.GetDigitTemplates(OcrValueType.Round, frameWidth, frameHeight);
        var slashTemplate = repository.GetSlashTemplate(frameWidth, frameHeight);

        if (!TryRecognizeRoundDigits(captureRegion, digitTemplates, slashTemplate, out var text))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out round);
    }

    private bool TryReadRound(Mat captureRegion, int frameWidth, int frameHeight, out int round, out string diagnostics)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        round = 0;

        if (!TryEnsureRepository(out var repository))
        {
            diagnostics = BuildRepositoryUnavailableDiagnostics("Round");
            return false;
        }

        var digitTemplates = repository.GetDigitTemplates(OcrValueType.Round, frameWidth, frameHeight);
        var slashTemplate = repository.GetSlashTemplate(frameWidth, frameHeight);
        var templateSelection = repository.GetSelectionDescription(frameWidth, frameHeight);

        if (!TryRecognizeRoundDigits(captureRegion, digitTemplates, slashTemplate, out var text, out var recognitionDiagnostics))
        {
            diagnostics = $"Round: failed | RegionSize={captureRegion.Width}x{captureRegion.Height} | Templates={templateSelection} | {recognitionDiagnostics}";
            return false;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out round))
        {
            diagnostics = $"Round: parse failed | RegionSize={captureRegion.Width}x{captureRegion.Height} | Templates={templateSelection} | Text='{text}' | {recognitionDiagnostics}";
            return false;
        }

        diagnostics = $"Round: success | Value={round} | RegionSize={captureRegion.Width}x{captureRegion.Height} | Templates={templateSelection} | Text='{text}' | {recognitionDiagnostics}";
        return true;
    }

    private bool TryRecognizeDigits(
        Mat captureRegion,
        IReadOnlyList<PreparedTemplate> templates,
        IReadOnlyList<double> thresholds,
        out string text)
    {
        text = string.Empty;

        foreach (var threshold in thresholds)
        {
            var candidates = FilterGoldCandidates(CollectCandidates(captureRegion, templates, threshold), threshold);
            if (candidates.Count == 0)
            {
                continue;
            }

            text = BuildDigitText(candidates);
            if (!string.IsNullOrEmpty(text))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryRecognizeDigits(
        Mat captureRegion,
        IReadOnlyList<PreparedTemplate> templates,
        IReadOnlyList<double> thresholds,
        out string text,
        out string diagnostics)
    {
        text = string.Empty;
        var attempts = new List<string>(thresholds.Count);

        foreach (var threshold in thresholds)
        {
            var rawCandidates = CollectCandidates(captureRegion, templates, threshold);
            var candidates = FilterGoldCandidates(rawCandidates, threshold);
            var candidateText = candidates.Count == 0 ? string.Empty : BuildDigitText(candidates);
            attempts.Add(
                $"threshold={threshold:F2}, rawCandidates={rawCandidates.Count}, filteredCandidates={candidates.Count}, text='{candidateText}', raw=[{FormatCandidates(rawCandidates)}], filtered=[{FormatCandidates(candidates)}]");

            if (!string.IsNullOrEmpty(candidateText))
            {
                text = candidateText;
                diagnostics = string.Join(" | ", attempts);
                return true;
            }
        }

        diagnostics = string.Join(" | ", attempts);
        return false;
    }

    private bool TryRecognizeRoundDigits(
        Mat captureRegion,
        IReadOnlyList<PreparedTemplate> digitTemplates,
        PreparedTemplate slashTemplate,
        out string text)
    {
        text = string.Empty;

        foreach (var threshold in RoundThresholds)
        {
            var digitCandidates = CollectCandidates(captureRegion, digitTemplates, threshold);
            if (digitCandidates.Count == 0)
            {
                continue;
            }

            double? slashCenterX = TryFindSlashCandidate(captureRegion, slashTemplate, threshold, out var detectedSlashCandidate)
                ? detectedSlashCandidate!.CenterX
                : null;

            var filteredCandidates = FilterRoundCandidates(digitCandidates, slashCenterX);
            text = BuildDigitText(filteredCandidates);
            if (!string.IsNullOrEmpty(text))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryRecognizeRoundDigits(
        Mat captureRegion,
        IReadOnlyList<PreparedTemplate> digitTemplates,
        PreparedTemplate slashTemplate,
        out string text,
        out string diagnostics)
    {
        text = string.Empty;
        var attempts = new List<string>(RoundThresholds.Length);

        foreach (var threshold in RoundThresholds)
        {
            var digitCandidates = CollectCandidates(captureRegion, digitTemplates, threshold);
            if (digitCandidates.Count == 0)
            {
                attempts.Add($"threshold={threshold:F2}, digitCandidates=0, matches=[]");
                continue;
            }

            var hasSlash = TryFindSlashCandidate(captureRegion, slashTemplate, threshold, out var detectedSlashCandidate);
            double? slashCenterX = hasSlash ? detectedSlashCandidate!.CenterX : null;
            var filteredCandidates = FilterRoundCandidates(digitCandidates, slashCenterX);
            var candidateText = BuildDigitText(filteredCandidates);
            attempts.Add(
                $"threshold={threshold:F2}, digitCandidates={digitCandidates.Count}, filteredCandidates={filteredCandidates.Count}, slash={(hasSlash ? FormatCandidate(detectedSlashCandidate!) : "none")}, text='{candidateText}', raw=[{FormatCandidates(digitCandidates)}], filtered=[{FormatCandidates(filteredCandidates)}]");

            if (!string.IsNullOrEmpty(candidateText))
            {
                text = candidateText;
                diagnostics = string.Join(" | ", attempts);
                return true;
            }
        }

        diagnostics = string.Join(" | ", attempts);
        return false;
    }

    private List<OcrCandidate> CollectCandidates(Mat captureRegion, IReadOnlyList<PreparedTemplate> templates, double threshold)
    {
        var candidates = new List<OcrCandidate>();

        foreach (var template in templates)
        {
            if (captureRegion.Width < template.Width || captureRegion.Height < template.Height)
            {
                continue;
            }

            using var matchResult = _templateMatchService.CreateMatchResult(captureRegion, template.Image, template.Mask);
            using var working = matchResult.Clone();
            var maxIterations = Math.Max(1, working.Width * working.Height);

            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                Cv2.MinMaxLoc(working, out _, out var maxValue, out _, out var maxLocation);
                if (!double.IsFinite(maxValue) || maxValue < threshold)
                {
                    break;
                }

                var bounds = new OpenCvRect(maxLocation.X, maxLocation.Y, template.Width, template.Height);
                candidates.Add(new OcrCandidate(template.Symbol, bounds, maxValue));

                using var suppressionRegion = new Mat(working, ClampRect(bounds, working.Width, working.Height));
                suppressionRegion.SetTo(Scalar.All(0));
            }
        }

        return SuppressCandidates(candidates);
    }

    private bool TryFindSlashCandidate(Mat captureRegion, PreparedTemplate slashTemplate, double threshold, out OcrCandidate? bestMatch)
    {
        bestMatch = null;

        var candidates = CollectCandidates(captureRegion, [slashTemplate], threshold);
        if (candidates.Count == 0)
        {
            return false;
        }

        bestMatch = candidates.OrderByDescending(x => x.Score).First();
        return true;
    }

    private static string FormatCandidates(IReadOnlyList<OcrCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", OrderCandidates(candidates).Select(FormatCandidate));
    }

    private static string FormatCandidate(OcrCandidate candidate)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{candidate.Symbol}@{FormatRect(candidate.Bounds)}:{candidate.Score:F3}");
    }

    private static List<OcrCandidate> FilterGoldCandidates(IReadOnlyList<OcrCandidate> candidates, double threshold)
    {
        var ordered = OrderCandidates(candidates);
        if (ordered.Count == 0)
        {
            return ordered;
        }

        var nonOneCandidates = ordered
            .Where(x => !string.Equals(x.Symbol, "1", StringComparison.Ordinal))
            .ToList();

        if (nonOneCandidates.Count == 0)
        {
            return ordered;
        }

        var oneScoreThreshold = Math.Max(
            threshold,
            nonOneCandidates.Average(x => x.Score) - GoldOneScoreDelta);

        var topYValues = nonOneCandidates
            .Select(x => x.Bounds.Y)
            .OrderBy(x => x)
            .ToArray();
        var dominantTopY = topYValues[topYValues.Length / 2];

        return ordered
            .Where(candidate =>
                !string.Equals(candidate.Symbol, "1", StringComparison.Ordinal) ||
                (candidate.Score >= oneScoreThreshold &&
                 Math.Abs(candidate.Bounds.Y - dominantTopY) <= GoldOneTopTolerance))
            .ToList();
    }

    private static List<OcrCandidate> FilterRoundCandidates(IReadOnlyList<OcrCandidate> candidates, double? slashCenterX)
    {
        var ordered = OrderCandidates(candidates);
        if (ordered.Count == 0)
        {
            return ordered;
        }

        if (slashCenterX.HasValue)
        {
            var averageWidth = ordered.Average(x => x.Bounds.Width);
            if (slashCenterX.Value > ordered[0].CenterX && slashCenterX.Value < ordered[^1].CenterX + averageWidth)
            {
                return ordered
                    .Where(x => x.CenterX < slashCenterX.Value)
                    .ToList();
            }
        }

        if (ordered.Count <= 1)
        {
            return ordered;
        }

        var widestGap = double.MinValue;
        var splitIndex = -1;
        var meanWidth = ordered.Average(x => x.Bounds.Width);
        for (var index = 0; index < ordered.Count - 1; index++)
        {
            var gap = ordered[index + 1].Bounds.X - ordered[index].Bounds.Right;
            if (gap > widestGap)
            {
                widestGap = gap;
                splitIndex = index;
            }
        }

        if (widestGap >= Math.Max(2d, meanWidth * 0.55d) && splitIndex >= 0)
        {
            return ordered.Take(splitIndex + 1).ToList();
        }

        return ordered;
    }

    private static string BuildDigitText(IReadOnlyList<OcrCandidate> candidates)
    {
        var ordered = OrderCandidates(candidates);
        if (ordered.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        OcrCandidate? previous = null;
        foreach (var candidate in ordered)
        {
            if (previous is not null)
            {
                var overlapAllowance = Math.Min(previous.Bounds.Width, candidate.Bounds.Width) * 0.30d;
                if (candidate.Bounds.X < previous.Bounds.Right - overlapAllowance)
                {
                    continue;
                }
            }

            builder.Append(candidate.Symbol);
            previous = candidate;
        }

        return builder.ToString();
    }

    private static List<OcrCandidate> OrderCandidates(IReadOnlyList<OcrCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var centerYValues = candidates
            .Select(x => x.CenterY)
            .OrderBy(x => x)
            .ToArray();

        var medianCenterY = centerYValues[centerYValues.Length / 2];
        var averageHeight = candidates.Average(x => x.Bounds.Height);
        var verticalTolerance = Math.Max(4d, averageHeight * 0.45d);

        return candidates
            .Where(x => Math.Abs(x.CenterY - medianCenterY) <= verticalTolerance)
            .OrderBy(x => x.Bounds.X)
            .ThenByDescending(x => x.Score)
            .ToList();
    }

    private static List<OcrCandidate> SuppressCandidates(IReadOnlyList<OcrCandidate> candidates)
    {
        var kept = new List<OcrCandidate>();

        foreach (var candidate in candidates.OrderByDescending(x => x.Score))
        {
            if (kept.Any(existing => RepresentsSameGlyph(existing, candidate)))
            {
                continue;
            }

            kept.Add(candidate);
        }

        return kept;
    }

    private static bool RepresentsSameGlyph(OcrCandidate left, OcrCandidate right)
    {
        var overlap = GetIntersection(left.Bounds, right.Bounds);
        if (overlap.Width > 0 && overlap.Height > 0)
        {
            var overlapArea = overlap.Width * overlap.Height;
            var leftArea = left.Bounds.Width * left.Bounds.Height;
            var rightArea = right.Bounds.Width * right.Bounds.Height;
            var overlapRatio = overlapArea / (double)Math.Min(leftArea, rightArea);
            if (overlapRatio >= 0.45d)
            {
                return true;
            }
        }

        var deltaX = Math.Abs(left.CenterX - right.CenterX);
        var deltaY = Math.Abs(left.CenterY - right.CenterY);
        return deltaX <= Math.Min(left.Bounds.Width, right.Bounds.Width) * 0.35d &&
               deltaY <= Math.Min(left.Bounds.Height, right.Bounds.Height) * 0.35d;
    }

    private static OpenCvRect GetIntersection(OpenCvRect left, OpenCvRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottomEdge = Math.Min(left.Bottom, right.Bottom);
        var width = Math.Max(0, rightEdge - x);
        var height = Math.Max(0, bottomEdge - y);
        return new OpenCvRect(x, y, width, height);
    }

    private static OpenCvRect ClampRect(OpenCvRect rect, int width, int height)
    {
        var x = Math.Clamp(rect.X, 0, width - 1);
        var y = Math.Clamp(rect.Y, 0, height - 1);
        var right = Math.Clamp(rect.Right, x + 1, width);
        var bottom = Math.Clamp(rect.Bottom, y + 1, height);
        return new OpenCvRect(x, y, right - x, bottom - y);
    }

    private static string FormatRect(OpenCvRect rect)
    {
        return $"({rect.X},{rect.Y},{rect.Width},{rect.Height})";
    }

    private static string BuildAssetRootPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "OcrDigits");
    }

    private string BuildRepositoryUnavailableDiagnostics(string targetName)
    {
        var reason = string.IsNullOrWhiteSpace(_repositoryUnavailableReason)
            ? "Unknown repository initialization failure."
            : _repositoryUnavailableReason;
        return $"{targetName}: repository unavailable | AssetRoot={BuildAssetRootPath()} | Reason={reason}";
    }

    private bool TryEnsureRepository(out DigitTemplateRepository repository)
    {
        lock (_syncRoot)
        {
            if (_repository is not null)
            {
                repository = _repository;
                return true;
            }

            var assetRootPath = BuildAssetRootPath();
            if (!Directory.Exists(assetRootPath))
            {
                _repositoryUnavailableReason = $"Directory not found: {assetRootPath}";
                repository = null!;
                return false;
            }

            try
            {
                _repository = new DigitTemplateRepository(assetRootPath);
                _repositoryUnavailableReason = null;
                repository = _repository;
                return true;
            }
            catch (Exception ex)
            {
                _repositoryUnavailableReason = ex.ToString();
                repository = null!;
                return false;
            }
        }
    }

    private enum OcrValueType
    {
        Gold,
        Round
    }

    private enum TemplateResolution
    {
        Resolution1080p,
        Resolution720p
    }

    private sealed record OcrCandidate(string Symbol, OpenCvRect Bounds, double Score)
    {
        public double CenterX => Bounds.X + Bounds.Width / 2d;

        public double CenterY => Bounds.Y + Bounds.Height / 2d;
    }

    private sealed class PreparedTemplate
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

    private sealed class RawTemplate
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

    private sealed class TemplateCatalog
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

    private sealed class DigitTemplateRepository
    {
        private readonly object _cacheSyncRoot = new();
        private readonly TemplateCatalog _catalog1080;
        private readonly TemplateCatalog _catalog720;
        private readonly Dictionary<string, IReadOnlyList<PreparedTemplate>> _digitCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PreparedTemplate> _slashCache = new(StringComparer.Ordinal);

        public DigitTemplateRepository(string assetRootPath)
        {
            _catalog1080 = LoadCatalog(assetRootPath, TemplateResolution.Resolution1080p, Reference1080p, "1080p");
            _catalog720 = LoadCatalog(assetRootPath, TemplateResolution.Resolution720p, Reference720p, "720p");
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

        public string GetSelectionDescription(int frameWidth, int frameHeight)
        {
            var catalog = SelectCatalog(frameWidth, frameHeight);
            var scaleX = frameWidth / (double)catalog.BaseResolution.Width;
            var scaleY = frameHeight / (double)catalog.BaseResolution.Height;
            return $"{FormatResolution(catalog.Resolution)} base={catalog.BaseResolution.Width}x{catalog.BaseResolution.Height} scale={scaleX:F3}x{scaleY:F3}";
        }

        private TemplateCatalog SelectCatalog(int frameWidth, int frameHeight)
        {
            var distance1080 = CalculateResolutionDistance(frameWidth, frameHeight, _catalog1080.BaseResolution);
            var distance720 = CalculateResolutionDistance(frameWidth, frameHeight, _catalog720.BaseResolution);
            return distance1080 <= distance720 ? _catalog1080 : _catalog720;
        }

        private static string FormatResolution(TemplateResolution resolution)
        {
            return resolution switch
            {
                TemplateResolution.Resolution1080p => "1080p",
                TemplateResolution.Resolution720p => "720p",
                _ => resolution.ToString()
            };
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
                .Select(index => LoadTemplate(assetRootPath, Path.Combine(relativeFolder, "gold", $"{index}.png"), index.ToString(CultureInfo.InvariantCulture)))
                .ToArray();

            var roundDigits = Enumerable
                .Range(0, 10)
                .Select(index => LoadTemplate(assetRootPath, Path.Combine(relativeFolder, "round", $"{index}.png"), index.ToString(CultureInfo.InvariantCulture)))
                .ToArray();

            var slash = LoadTemplate(assetRootPath, Path.Combine(relativeFolder, "round", "slash.png"), "/");
            return new TemplateCatalog(resolution, baseResolution, goldDigits, roundDigits, slash);
        }

        private static RawTemplate LoadTemplate(string assetRootPath, string relativePath, string symbol)
        {
            var fullPath = Path.Combine(assetRootPath, relativePath);
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


}
