using System.Globalization;
using System.IO;
using System.Text;
using OpenCvSharp;
using OpenCvRect = OpenCvSharp.Rect;

namespace BetterBTD.Services.Tasks.CaptureAnalysis;

public sealed class GameStageChallengeOcrService
{
    private static readonly Lazy<GameStageChallengeOcrService> InstanceHolder = new(() => new GameStageChallengeOcrService());
    private static readonly double[] GoldThresholds = [0.90d, 0.84d, 0.78d];
    private static readonly double[] RoundThresholds = [0.90d, 0.84d, 0.78d];
    private const double GoldOneScoreDelta = 0.04d;
    private const int GoldOneTopTolerance = 2;

    private readonly object _syncRoot = new();
    private readonly TemplateMatchService _templateMatchService;

    private DigitTemplateRepository? _digitRepository;

    private GameStageChallengeOcrService()
    {
        _templateMatchService = TemplateMatchService.Instance;
    }

    public static GameStageChallengeOcrService Instance => InstanceHolder.Value;

    public bool IsAvailable => TryEnsureDigitRepository(out _);

    public bool TryReadGold(Mat captureRegion, int frameWidth, int frameHeight, out int gold)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        gold = 0;

        if (captureRegion.Empty())
        {
            return false;
        }

        GameOcrSupport.ValidateFrameSize(frameWidth, frameHeight);

        if (!TryEnsureDigitRepository(out var repository))
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

    public bool TryReadRound(Mat captureRegion, int frameWidth, int frameHeight, out int round)
    {
        ArgumentNullException.ThrowIfNull(captureRegion);
        round = 0;

        if (captureRegion.Empty())
        {
            return false;
        }

        GameOcrSupport.ValidateFrameSize(frameWidth, frameHeight);

        if (!TryEnsureDigitRepository(out var repository))
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

    private bool TryEnsureDigitRepository(out DigitTemplateRepository repository)
    {
        lock (_syncRoot)
        {
            if (_digitRepository is not null)
            {
                repository = _digitRepository;
                return true;
            }

            var assetRootPath = GameOcrSupport.BuildDigitAssetRootPath();
            if (!Directory.Exists(assetRootPath))
            {
                repository = null!;
                return false;
            }

            try
            {
                _digitRepository = new DigitTemplateRepository(assetRootPath);
                repository = _digitRepository;
                return true;
            }
            catch
            {
                repository = null!;
                return false;
            }
        }
    }

    private sealed record OcrCandidate(string Symbol, OpenCvRect Bounds, double Score)
    {
        public double CenterX => Bounds.X + Bounds.Width / 2d;

        public double CenterY => Bounds.Y + Bounds.Height / 2d;
    }
}
