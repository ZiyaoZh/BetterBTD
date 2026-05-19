using BetterBTD.Models.AutoTasks;
using OpenCvSharp;

namespace BetterBTD.Services.Tasks.AutoTasks;

internal static class GameUiDetectionRuleEvaluator
{
    public static bool IsMatch(Mat frame, GameUiDetectionConfig config, GameUiDetectionRule rule)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(rule);

        if (frame.Empty() || !rule.IsEnabled || rule.AllOf.Count == 0)
        {
            return false;
        }

        foreach (var condition in rule.AllOf)
        {
            if (!IsConditionMatch(frame, config, condition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsConditionMatch(Mat frame, GameUiDetectionConfig config, GameUiColorCondition condition)
    {
        var expectedColor = ParseHexColor(condition.ColorHex);
        var actualPoint = ScaleReferencePoint(
            condition.X,
            condition.Y,
            config.ReferenceWidth,
            config.ReferenceHeight,
            frame.Width,
            frame.Height);
        var actualColor = ReadPixel(frame, actualPoint.X, actualPoint.Y);
        var tolerance = condition.Tolerance ?? config.DefaultTolerance;

        var isEqual =
            Math.Abs(actualColor.R - expectedColor.R) <= tolerance &&
            Math.Abs(actualColor.G - expectedColor.G) <= tolerance &&
            Math.Abs(actualColor.B - expectedColor.B) <= tolerance;

        return condition.Operator switch
        {
            GameUiColorComparisonOperator.Equals => isEqual,
            GameUiColorComparisonOperator.NotEquals => !isEqual,
            _ => false
        };
    }

    private static (int X, int Y) ScaleReferencePoint(
        int referenceX,
        int referenceY,
        int referenceWidth,
        int referenceHeight,
        int actualWidth,
        int actualHeight)
    {
        var x = ScaleReferenceCoordinate(referenceX, referenceWidth, actualWidth);
        var y = ScaleReferenceCoordinate(referenceY, referenceHeight, actualHeight);
        return (x, y);
    }

    private static int ScaleReferenceCoordinate(int coordinate, int referenceSize, int actualSize)
    {
        if (referenceSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(referenceSize));
        }

        if (actualSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualSize));
        }

        var scaled = (int)Math.Round(coordinate / (double)referenceSize * actualSize);
        return Math.Clamp(scaled, 0, Math.Max(0, actualSize - 1));
    }

    private static RgbColor ParseHexColor(string hexColor)
    {
        var normalized = (hexColor ?? string.Empty).Trim();
        if (normalized.StartsWith('#'))
        {
            normalized = normalized[1..];
        }

        if (normalized.Length != 6)
        {
            throw new FormatException($"Invalid RGB hex color '{hexColor}'.");
        }

        return new RgbColor(
            Convert.ToInt32(normalized[..2], 16),
            Convert.ToInt32(normalized.Substring(2, 2), 16),
            Convert.ToInt32(normalized.Substring(4, 2), 16));
    }

    private static RgbColor ReadPixel(Mat frame, int x, int y)
    {
        return frame.Channels() switch
        {
            1 =>
                new RgbColor(
                    frame.At<byte>(y, x),
                    frame.At<byte>(y, x),
                    frame.At<byte>(y, x)),
            3 =>
                ConvertBgr(frame.At<Vec3b>(y, x)),
            4 =>
                ConvertBgra(frame.At<Vec4b>(y, x)),
            _ => throw new NotSupportedException($"Unsupported frame channel count: {frame.Channels()}.")
        };
    }

    private static RgbColor ConvertBgr(Vec3b value)
    {
        return new RgbColor(value.Item2, value.Item1, value.Item0);
    }

    private static RgbColor ConvertBgra(Vec4b value)
    {
        return new RgbColor(value.Item2, value.Item1, value.Item0);
    }

    private readonly record struct RgbColor(int R, int G, int B);
}
