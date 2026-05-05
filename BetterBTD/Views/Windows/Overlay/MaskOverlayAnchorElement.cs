using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BetterBTD.Views.Windows.Overlay;

public sealed class MaskOverlayAnchorElement : MaskOverlayElement
{
    public MaskOverlayAnchorElement(
        Guid id,
        Point center,
        Color strokeColor,
        double strokeThickness = 2,
        double crosshairLength = 10,
        double gapRadius = 4,
        double ringRadius = 0,
        string? label = null,
        Color? labelForegroundColor = null,
        Color? labelBackgroundColor = null)
        : base(id)
    {
        Center = center;
        StrokeColor = strokeColor;
        StrokeThickness = strokeThickness;
        CrosshairLength = crosshairLength;
        GapRadius = gapRadius;
        RingRadius = ringRadius;
        Label = label;
        LabelForegroundColor = labelForegroundColor ?? Colors.White;
        LabelBackgroundColor = labelBackgroundColor ?? Color.FromArgb(180, 16, 24, 39);
    }

    public Point Center { get; set; }

    public Color StrokeColor { get; set; }

    public double StrokeThickness { get; set; }

    public double CrosshairLength { get; set; }

    public double GapRadius { get; set; }

    public double RingRadius { get; set; }

    public string? Label { get; set; }

    public double LabelFontSize { get; set; } = 12;

    public double LabelOffsetX { get; set; } = 12;

    public double LabelOffsetY { get; set; } = -12;

    public Thickness LabelPadding { get; set; } = new(6, 3, 6, 3);

    public Color LabelForegroundColor { get; set; }

    public Color LabelBackgroundColor { get; set; }

    public override void Draw(DrawingContext drawingContext, DpiScale dpiScale)
    {
        if (StrokeThickness <= 0)
        {
            return;
        }

        var pen = new Pen(CreateBrush(StrokeColor), StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        var outerLength = GapRadius + CrosshairLength;

        drawingContext.DrawLine(
            pen,
            new Point(Center.X - outerLength, Center.Y),
            new Point(Center.X - GapRadius, Center.Y));
        drawingContext.DrawLine(
            pen,
            new Point(Center.X + GapRadius, Center.Y),
            new Point(Center.X + outerLength, Center.Y));
        drawingContext.DrawLine(
            pen,
            new Point(Center.X, Center.Y - outerLength),
            new Point(Center.X, Center.Y - GapRadius));
        drawingContext.DrawLine(
            pen,
            new Point(Center.X, Center.Y + GapRadius),
            new Point(Center.X, Center.Y + outerLength));

        if (RingRadius > 0)
        {
            drawingContext.DrawEllipse(null, pen, Center, RingRadius, RingRadius);
        }

        if (!string.IsNullOrWhiteSpace(Label))
        {
            DrawLabel(drawingContext, dpiScale);
        }
    }

    private void DrawLabel(DrawingContext drawingContext, DpiScale dpiScale)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var formattedText = new FormattedText(
            Label!,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            LabelFontSize,
            CreateBrush(LabelForegroundColor),
            dpiScale.PixelsPerDip);

        var origin = new Point(Center.X + LabelOffsetX, Center.Y + LabelOffsetY);
        var backgroundRect = new Rect(
            origin.X,
            origin.Y,
            formattedText.WidthIncludingTrailingWhitespace + LabelPadding.Left + LabelPadding.Right,
            formattedText.Height + LabelPadding.Top + LabelPadding.Bottom);

        drawingContext.DrawRoundedRectangle(
            CreateBrush(LabelBackgroundColor),
            null,
            backgroundRect,
            6,
            6);

        drawingContext.DrawText(
            formattedText,
            new Point(origin.X + LabelPadding.Left, origin.Y + LabelPadding.Top));
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
