using BetterBTD.Core.Simulator;
using Fischless.WindowsInput;
using CvPoint = OpenCvSharp.Point;
using System.Windows;

namespace BetterBTD.Helpers.Extensions;

public static class ClickExtension
{
    public static void Click(this CvPoint point)
    {
        Click(point.X, point.Y);
    }

    public static IMouseSimulator Click(double x, double y)
    {
        var absoluteCoordinate = ToVirtualDesktopAbsoluteCoordinate(new Point(x, y));
        return Simulation.SendInput.Mouse
            .MoveMouseToPositionOnVirtualDesktop(absoluteCoordinate.X, absoluteCoordinate.Y)
            .LeftButtonDown()
            .Sleep(50)
            .LeftButtonUp();
    }

    public static IMouseSimulator Move(double x, double y)
    {
        var absoluteCoordinate = ToVirtualDesktopAbsoluteCoordinate(new Point(x, y));
        return Simulation.SendInput.Mouse.MoveMouseToPositionOnVirtualDesktop(absoluteCoordinate.X, absoluteCoordinate.Y);
    }

    public static IMouseSimulator Move(CvPoint point)
    {
        return Move(point.X, point.Y);
    }

    private static Point ToVirtualDesktopAbsoluteCoordinate(Point screenCoordinate)
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var width = Math.Max(1d, SystemParameters.VirtualScreenWidth);
        var height = Math.Max(1d, SystemParameters.VirtualScreenHeight);

        return new Point(
            ScaleToAbsoluteCoordinate(screenCoordinate.X, left, width),
            ScaleToAbsoluteCoordinate(screenCoordinate.Y, top, height));
    }

    private static double ScaleToAbsoluteCoordinate(double coordinate, double origin, double length)
    {
        if (length <= 1d)
        {
            return 0d;
        }

        var normalized = (coordinate - origin) * 65535d / (length - 1d);
        return Math.Clamp(normalized, 0d, 65535d);
    }
}
