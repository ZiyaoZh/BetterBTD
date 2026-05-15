using System.Windows;
using BetterBTD.Helpers;
using BetterBTD.Models;
using Fischless.GameCapture;

namespace BetterBTD.Services.Tasks.Input;

public sealed class CoordinateTransformService
{
    private static readonly Lazy<CoordinateTransformService> InstanceHolder = new(() => new CoordinateTransformService());
    private const double DefaultScriptReferenceWidth = 1920d;
    private const double DefaultScriptReferenceHeight = 1080d;
    private const double DefaultAspectRatioTolerance = 0.01d;

    private CoordinateTransformService()
    {
        _gameCaptureService = GameCaptureService.Instance;
    }

    public static CoordinateTransformService Instance => InstanceHolder.Value;

    public double ScriptReferenceWidth { get; } = DefaultScriptReferenceWidth;

    public double ScriptReferenceHeight { get; } = DefaultScriptReferenceHeight;

    public Point ToScriptCoordinate(Point referenceCoordinate, GameWindowInfo windowInfo)
    {
        var referenceBounds = GetReferenceBounds(windowInfo);
        var safeReferenceWidth = referenceBounds.Width <= 0 ? 1d : referenceBounds.Width;
        var safeReferenceHeight = referenceBounds.Height <= 0 ? 1d : referenceBounds.Height;

        return new Point(
            referenceCoordinate.X / safeReferenceWidth * ScriptReferenceWidth,
            referenceCoordinate.Y / safeReferenceHeight * ScriptReferenceHeight);
    }

    public Point ToReferenceCoordinate(Point scriptCoordinate, GameWindowInfo windowInfo)
    {
        var safeReferenceWidth = ScriptReferenceWidth <= 0 ? DefaultScriptReferenceWidth : ScriptReferenceWidth;
        var safeReferenceHeight = ScriptReferenceHeight <= 0 ? DefaultScriptReferenceHeight : ScriptReferenceHeight;
        var referenceBounds = GetReferenceBounds(windowInfo);

        return new Point(
            scriptCoordinate.X / safeReferenceWidth * referenceBounds.Width,
            scriptCoordinate.Y / safeReferenceHeight * referenceBounds.Height);
    }

    public Point ToReferenceCoordinateFromScreen(Point screenCoordinate, GameWindowInfo windowInfo)
    {
        var referenceBounds = GetReferenceBounds(windowInfo);
        return new Point(
            screenCoordinate.X - referenceBounds.Left,
            screenCoordinate.Y - referenceBounds.Top);
    }

    public Point ToScreenCoordinate(Point scriptCoordinate, GameWindowInfo windowInfo)
    {
        var referenceBounds = GetReferenceBounds(windowInfo);
        var referenceCoordinate = ToReferenceCoordinate(scriptCoordinate, windowInfo);
        return new Point(
            referenceBounds.Left + referenceCoordinate.X,
            referenceBounds.Top + referenceCoordinate.Y);
    }

    public NativeWindowBounds GetReferenceBounds(GameWindowInfo windowInfo)
    {
        if (IsSharedSurfaceMode() &&
            NativeWindowHelper.TryGetRawWindowBounds(windowInfo.Handle, out var rawWindowBounds))
        {
            return new NativeWindowBounds(
                rawWindowBounds.Left,
                rawWindowBounds.Top + rawWindowBounds.Height - windowInfo.ClientHeight,
                windowInfo.ClientWidth,
                windowInfo.ClientHeight);
        }

        return windowInfo.ClientBounds;
    }

    public bool HasReferenceAspectRatio(GameWindowInfo windowInfo, double tolerance = DefaultAspectRatioTolerance)
    {
        var referenceBounds = GetReferenceBounds(windowInfo);
        if (referenceBounds.Width <= 0 || referenceBounds.Height <= 0)
        {
            return false;
        }

        var referenceAspectRatio = ScriptReferenceWidth / ScriptReferenceHeight;
        var currentAspectRatio = referenceBounds.Width / (double)referenceBounds.Height;
        return Math.Abs(currentAspectRatio - referenceAspectRatio) <= tolerance;
    }

    private bool IsSharedSurfaceMode()
    {
        var captureModeName = _gameCaptureService.CurrentOptions.CaptureModeName;
        return Enum.TryParse<CaptureModes>(captureModeName, true, out var captureMode) &&
               captureMode == CaptureModes.DwmGetDxSharedSurface;
    }

    private readonly GameCaptureService _gameCaptureService;
}

