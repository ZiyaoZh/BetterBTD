using BetterBTD.Models.GameElements;

namespace BetterBTD.Models;

public sealed record MapTemplateMatchResult(
    GameMapType MapType,
    TemplateMatchInfo MatchInfo);
