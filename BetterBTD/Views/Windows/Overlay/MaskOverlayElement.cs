using System;
using System.Windows;
using System.Windows.Media;

namespace BetterBTD.Views.Windows.Overlay;

public abstract class MaskOverlayElement
{
    protected MaskOverlayElement(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }

    public bool IsVisible { get; set; } = true;

    public abstract void Draw(DrawingContext drawingContext, DpiScale dpiScale);
}
