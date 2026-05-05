using System;
using System.Windows;
using System.Windows.Media;

namespace BetterBTD.Views.Windows.Overlay;

public sealed class MaskOverlayRectangleElement : MaskOverlayElement
{
    public MaskOverlayRectangleElement(
        Guid id,
        Rect bounds,
        Color strokeColor,
        double strokeThickness = 2,
        Color? fillColor = null,
        double cornerRadius = 0)
        : base(id)
    {
        Bounds = bounds;
        StrokeColor = strokeColor;
        StrokeThickness = strokeThickness;
        FillColor = fillColor;
        CornerRadius = cornerRadius;
    }

    public Rect Bounds { get; set; }

    public Color StrokeColor { get; set; }

    public double StrokeThickness { get; set; }

    public Color? FillColor { get; set; }

    public double CornerRadius { get; set; }

    public override void Draw(DrawingContext drawingContext, DpiScale dpiScale)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        Pen? pen = StrokeThickness > 0
            ? new Pen(CreateBrush(StrokeColor), StrokeThickness)
            : null;

        if (pen is not null)
        {
            pen.Freeze();
        }

        Brush? fillBrush = FillColor is { } fillColor && fillColor.A > 0 ? CreateBrush(fillColor) : null;

        if (fillBrush is not null)
        {
            fillBrush.Freeze();
        }

        drawingContext.DrawRoundedRectangle(fillBrush, pen, Bounds, CornerRadius, CornerRadius);
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
