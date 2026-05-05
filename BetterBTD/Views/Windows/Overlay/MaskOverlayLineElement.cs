using System;
using System.Windows;
using System.Windows.Media;

namespace BetterBTD.Views.Windows.Overlay;

public sealed class MaskOverlayLineElement : MaskOverlayElement
{
    public MaskOverlayLineElement(
        Guid id,
        Point startPoint,
        Point endPoint,
        Color strokeColor,
        double strokeThickness = 2,
        bool showArrowHead = false,
        double arrowHeadLength = 12,
        double arrowHeadAngle = 30)
        : base(id)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
        StrokeColor = strokeColor;
        StrokeThickness = strokeThickness;
        ShowArrowHead = showArrowHead;
        ArrowHeadLength = arrowHeadLength;
        ArrowHeadAngle = arrowHeadAngle;
    }

    public Point StartPoint { get; set; }

    public Point EndPoint { get; set; }

    public Color StrokeColor { get; set; }

    public double StrokeThickness { get; set; }

    public bool ShowArrowHead { get; set; }

    public double ArrowHeadLength { get; set; }

    public double ArrowHeadAngle { get; set; }

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

        drawingContext.DrawLine(pen, StartPoint, EndPoint);

        if (!ShowArrowHead)
        {
            return;
        }

        var direction = EndPoint - StartPoint;
        if (direction.LengthSquared < double.Epsilon)
        {
            return;
        }

        direction.Normalize();
        var arrowRadians = ArrowHeadAngle * Math.PI / 180d;

        var leftVector = Rotate(direction, Math.PI - arrowRadians) * ArrowHeadLength;
        var rightVector = Rotate(direction, Math.PI + arrowRadians) * ArrowHeadLength;

        drawingContext.DrawLine(pen, EndPoint, EndPoint + leftVector);
        drawingContext.DrawLine(pen, EndPoint, EndPoint + rightVector);
    }

    private static Vector Rotate(Vector vector, double radians)
    {
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new Vector(
            vector.X * cosine - vector.Y * sine,
            vector.X * sine + vector.Y * cosine);
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
