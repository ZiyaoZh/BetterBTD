using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace BetterBTD.Views.Windows.Overlay;

public sealed class MaskOverlaySurface : FrameworkElement
{
    private readonly List<MaskOverlayElement> _elements = new();
    private readonly Dictionary<Guid, MaskOverlayElement> _elementMap = new();

    private double _scaleFactor = 1d;

    public double ScaleFactor
    {
        get => _scaleFactor;
        set
        {
            var nextScale = value > 0 ? value : 1d;
            if (Math.Abs(_scaleFactor - nextScale) < 0.0001d)
            {
                return;
            }

            _scaleFactor = nextScale;
            InvalidateVisual();
        }
    }

    public Guid AddElement(MaskOverlayElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (_elementMap.ContainsKey(element.Id))
        {
            RemoveElement(element.Id);
        }

        _elements.Add(element);
        _elementMap[element.Id] = element;
        InvalidateVisual();
        return element.Id;
    }

    public bool RemoveElement(Guid id)
    {
        if (!_elementMap.TryGetValue(id, out var element))
        {
            return false;
        }

        _elementMap.Remove(id);
        _elements.Remove(element);
        InvalidateVisual();
        return true;
    }

    public void ClearElements()
    {
        if (_elements.Count == 0)
        {
            return;
        }

        _elements.Clear();
        _elementMap.Clear();
        InvalidateVisual();
    }

    public bool UpdateElement<T>(Guid id, Action<T> updateAction) where T : MaskOverlayElement
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        if (!_elementMap.TryGetValue(id, out var element) || element is not T typedElement)
        {
            return false;
        }

        updateAction(typedElement);
        InvalidateVisual();
        return true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var dpiScale = VisualTreeHelper.GetDpi(this);
        var scaleFactor = _scaleFactor <= 0 ? 1d : _scaleFactor;

        drawingContext.PushTransform(new ScaleTransform(1d / scaleFactor, 1d / scaleFactor));

        foreach (var element in _elements.Where(x => x.IsVisible))
        {
            element.Draw(drawingContext, dpiScale);
        }

        drawingContext.Pop();
    }
}
