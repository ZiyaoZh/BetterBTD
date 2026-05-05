using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BetterBTD.Views.Windows.Overlay;

public sealed class MaskOverlayTextElement : MaskOverlayElement
{
    public MaskOverlayTextElement(
        Guid id,
        Point location,
        string text,
        Color foregroundColor,
        double fontSize = 16,
        Color? backgroundColor = null,
        Thickness? padding = null,
        FontFamily? fontFamily = null,
        FontWeight? fontWeight = null)
        : base(id)
    {
        Location = location;
        Text = text;
        ForegroundColor = foregroundColor;
        FontSize = fontSize;
        BackgroundColor = backgroundColor;
        Padding = padding ?? new Thickness(8, 4, 8, 4);
        FontFamily = fontFamily ?? new FontFamily("Segoe UI");
        FontWeight = fontWeight ?? FontWeights.SemiBold;
    }

    public Point Location { get; set; }

    public string Text { get; set; }

    public Color ForegroundColor { get; set; }

    public double FontSize { get; set; }

    public Color? BackgroundColor { get; set; }

    public Thickness Padding { get; set; }

    public FontFamily FontFamily { get; set; }

    public FontStyle FontStyle { get; set; } = FontStyles.Normal;

    public FontWeight FontWeight { get; set; }

    public FontStretch FontStretch { get; set; } = FontStretches.Normal;

    public double CornerRadius { get; set; } = 6;

    public override void Draw(DrawingContext drawingContext, DpiScale dpiScale)
    {
        var text = Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            CreateBrush(ForegroundColor),
            dpiScale.PixelsPerDip);

        var backgroundWidth = formattedText.WidthIncludingTrailingWhitespace + Padding.Left + Padding.Right;
        var backgroundHeight = formattedText.Height + Padding.Top + Padding.Bottom;

        if (BackgroundColor is { } backgroundColor && backgroundColor.A > 0)
        {
            var backgroundRect = new Rect(Location.X, Location.Y, backgroundWidth, backgroundHeight);
            var backgroundBrush = CreateBrush(backgroundColor);
            drawingContext.DrawRoundedRectangle(backgroundBrush, null, backgroundRect, CornerRadius, CornerRadius);
        }

        drawingContext.DrawText(formattedText, new Point(Location.X + Padding.Left, Location.Y + Padding.Top));
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
