using System.Collections.Generic;
using OpenCvSharp;

namespace BetterBTD.Models;

public sealed class CaptureTestOverlayLayout
{
    public required Rect GoldRegion { get; init; }

    public required Rect RoundRegion { get; init; }

    public IReadOnlyList<CaptureTestOverlayPointGroup> PointGroups { get; init; } = [];
}

public sealed class CaptureTestOverlayPointGroup
{
    public required string Id { get; init; }

    public required string LabelKey { get; init; }

    public IReadOnlyList<Point> Points { get; init; } = [];
}
