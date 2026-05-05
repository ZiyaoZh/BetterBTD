using System.Windows;
using BetterBTD.Models;

namespace BetterBTD.Services;

public sealed class CoordinateTransformService
{
    private static readonly Lazy<CoordinateTransformService> InstanceHolder = new(() => new CoordinateTransformService());
    private const double DefaultScriptReferenceWidth = 1920d;
    private const double DefaultScriptReferenceHeight = 1080d;
    private const double DefaultAspectRatioTolerance = 0.01d;

    private CoordinateTransformService()
    {
    }

    public static CoordinateTransformService Instance => InstanceHolder.Value;

    public double ScriptReferenceWidth { get; } = DefaultScriptReferenceWidth;

    public double ScriptReferenceHeight { get; } = DefaultScriptReferenceHeight;

    public Point ToScriptCoordinate(Point clientCoordinate, GameWindowInfo windowInfo)
    {
        var safeClientWidth = windowInfo.ClientWidth <= 0 ? 1d : windowInfo.ClientWidth;
        var safeClientHeight = windowInfo.ClientHeight <= 0 ? 1d : windowInfo.ClientHeight;

        return new Point(
            clientCoordinate.X / safeClientWidth * ScriptReferenceWidth,
            clientCoordinate.Y / safeClientHeight * ScriptReferenceHeight);
    }

    public Point ToClientCoordinate(Point scriptCoordinate, GameWindowInfo windowInfo)
    {
        var safeReferenceWidth = ScriptReferenceWidth <= 0 ? DefaultScriptReferenceWidth : ScriptReferenceWidth;
        var safeReferenceHeight = ScriptReferenceHeight <= 0 ? DefaultScriptReferenceHeight : ScriptReferenceHeight;

        return new Point(
            scriptCoordinate.X / safeReferenceWidth * windowInfo.ClientWidth,
            scriptCoordinate.Y / safeReferenceHeight * windowInfo.ClientHeight);
    }

    public Point ToScreenCoordinate(Point scriptCoordinate, GameWindowInfo windowInfo)
    {
        return windowInfo.ClientToScreen(ToClientCoordinate(scriptCoordinate, windowInfo));
    }

    public bool HasReferenceAspectRatio(GameWindowInfo windowInfo, double tolerance = DefaultAspectRatioTolerance)
    {
        if (windowInfo.ClientWidth <= 0 || windowInfo.ClientHeight <= 0)
        {
            return false;
        }

        var referenceAspectRatio = ScriptReferenceWidth / ScriptReferenceHeight;
        var currentAspectRatio = windowInfo.ClientWidth / (double)windowInfo.ClientHeight;
        return Math.Abs(currentAspectRatio - referenceAspectRatio) <= tolerance;
    }
}
