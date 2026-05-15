using System;
using System.Drawing;
using System.Drawing.Imaging;
using BetterBTD.Models;
using OpenCvSharp;

namespace BetterBTD.Services.Tasks.CaptureAnalysis;

public sealed class TemplateMatchService
{
    private static readonly Lazy<TemplateMatchService> InstanceHolder = new(() => new TemplateMatchService());
    private const double DefaultThreshold = 0.8d;

    private TemplateMatchService()
    {
    }

    public static TemplateMatchService Instance => InstanceHolder.Value;

    public TemplateMatchInfo Match(Bitmap sourceImage, Bitmap templateImage, double threshold = DefaultThreshold)
    {
        return Match(sourceImage, templateImage, null, threshold);
    }

    public TemplateMatchInfo Match(Bitmap sourceImage, Bitmap templateImage, Bitmap? templateMask, double threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(templateImage);

        using var sourceMat = ConvertBitmapToMat(sourceImage);
        using var templateMat = ConvertBitmapToMat(templateImage);
        var maskMat = templateMask is null ? null : ConvertBitmapToMat(templateMask);

        try
        {
            return Match(sourceMat, templateMat, maskMat, threshold);
        }
        finally
        {
            maskMat?.Dispose();
        }
    }

    public TemplateMatchInfo Match(Mat sourceImage, Mat templateImage, double threshold = DefaultThreshold)
    {
        return Match(sourceImage, templateImage, null, threshold);
    }

    public TemplateMatchInfo Match(Mat sourceImage, Mat templateImage, Mat? templateMask, double threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(templateImage);
        using var result = CreateMatchResult(sourceImage, templateImage, templateMask);
        Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);
        return new TemplateMatchInfo(maxLocation.X, maxLocation.Y, templateImage.Width, templateImage.Height, maxValue, threshold);
    }

    public bool TryMatch(Bitmap sourceImage, Bitmap templateImage, out TemplateMatchInfo matchInfo, double threshold = DefaultThreshold)
    {
        matchInfo = Match(sourceImage, templateImage, threshold);
        return matchInfo.IsMatch;
    }

    public bool TryMatch(Bitmap sourceImage, Bitmap templateImage, Bitmap? templateMask, out TemplateMatchInfo matchInfo, double threshold = DefaultThreshold)
    {
        matchInfo = Match(sourceImage, templateImage, templateMask, threshold);
        return matchInfo.IsMatch;
    }

    public bool TryMatch(Mat sourceImage, Mat templateImage, out TemplateMatchInfo matchInfo, double threshold = DefaultThreshold)
    {
        matchInfo = Match(sourceImage, templateImage, threshold);
        return matchInfo.IsMatch;
    }

    public bool TryMatch(Mat sourceImage, Mat templateImage, Mat? templateMask, out TemplateMatchInfo matchInfo, double threshold = DefaultThreshold)
    {
        matchInfo = Match(sourceImage, templateImage, templateMask, threshold);
        return matchInfo.IsMatch;
    }

    public Mat CreateMatchResult(Mat sourceImage, Mat templateImage, Mat? templateMask = null)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(templateImage);

        if (sourceImage.Empty())
        {
            throw new ArgumentException("Source image cannot be empty.", nameof(sourceImage));
        }

        if (templateImage.Empty())
        {
            throw new ArgumentException("Template image cannot be empty.", nameof(templateImage));
        }

        if (sourceImage.Width < templateImage.Width || sourceImage.Height < templateImage.Height)
        {
            throw new ArgumentException("Template image cannot be larger than the source image.", nameof(templateImage));
        }

        using var preparedSource = PrepareForTemplateMatch(sourceImage);
        using var preparedTemplate = PrepareForTemplateMatch(templateImage);
        var preparedMask = PrepareMask(templateMask, new OpenCvSharp.Size(preparedTemplate.Width, preparedTemplate.Height));

        try
        {
            var result = new Mat();
            if (preparedMask is null)
            {
                Cv2.MatchTemplate(preparedSource, preparedTemplate, result, TemplateMatchModes.CCoeffNormed);
            }
            else
            {
                Cv2.MatchTemplate(preparedSource, preparedTemplate, result, TemplateMatchModes.CCorrNormed, preparedMask);
            }

            return result;
        }
        finally
        {
            preparedMask?.Dispose();
        }
    }

    private static Mat PrepareForTemplateMatch(Mat image)
    {
        if (image.Channels() == 1)
        {
            return image.Clone();
        }

        var grayscale = new Mat();
        var colorConversionCode = image.Channels() switch
        {
            3 => ColorConversionCodes.BGR2GRAY,
            4 => ColorConversionCodes.BGRA2GRAY,
            _ => throw new NotSupportedException($"Unsupported channel count {image.Channels()} for template matching.")
        };

        Cv2.CvtColor(image, grayscale, colorConversionCode);
        return grayscale;
    }

    private static Mat? PrepareMask(Mat? templateMask, OpenCvSharp.Size expectedSize)
    {
        if (templateMask is null)
        {
            return null;
        }

        if (templateMask.Empty())
        {
            throw new ArgumentException("Template mask cannot be empty.", nameof(templateMask));
        }

        if (templateMask.Width != expectedSize.Width || templateMask.Height != expectedSize.Height)
        {
            throw new ArgumentException("Template mask size must match the template image size.", nameof(templateMask));
        }

        Mat mask;
        switch (templateMask.Channels())
        {
            case 1:
                mask = templateMask.Clone();
                break;
            case 3:
                mask = new Mat();
                Cv2.CvtColor(templateMask, mask, ColorConversionCodes.BGR2GRAY);
                break;
            case 4:
                mask = new Mat();
                Cv2.ExtractChannel(templateMask, mask, 3);
                break;
            default:
                throw new NotSupportedException($"Unsupported channel count {templateMask.Channels()} for template mask.");
        }

        Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary);
        return mask;
    }

    private static Mat ConvertBitmapToMat(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

        try
        {
            var stride = bitmapData.Stride;
            var buffer = bitmapData.Scan0;
            if (stride < 0)
            {
                stride = -stride;
                buffer = IntPtr.Add(buffer, bitmapData.Stride * (bitmapData.Height - 1));
            }

            var matType = bitmap.PixelFormat switch
            {
                PixelFormat.Format24bppRgb => MatType.CV_8UC3,
                PixelFormat.Format32bppArgb => MatType.CV_8UC4,
                PixelFormat.Format32bppRgb => MatType.CV_8UC4,
                PixelFormat.Format32bppPArgb => MatType.CV_8UC4,
                _ => throw new NotSupportedException($"Unsupported bitmap pixel format {bitmap.PixelFormat}.")
            };

            using var mat = Mat.FromPixelData(bitmap.Height, bitmap.Width, matType, buffer, stride);
            return mat.Clone();
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }
}

