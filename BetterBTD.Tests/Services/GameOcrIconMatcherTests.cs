using BetterBTD.Models;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using OpenCvSharp;

namespace BetterBTD.Tests.Services;

public sealed class GameOcrIconMatcherTests
{
    [Fact]
    public void TrySelectBestCandidateMatch_SelectsHighestScoreCandidate()
    {
        var candidateMatches = new[]
        {
            new CandidateMatch<string>("first", new TemplateMatchInfo(0, 0, 10, 10, 0.91d, 0.90d)),
            new CandidateMatch<string>("best", new TemplateMatchInfo(10, 10, 10, 10, 0.97d, 0.90d)),
            new CandidateMatch<string>("below-threshold", new TemplateMatchInfo(20, 20, 10, 10, 0.89d, 0.90d))
        };

        var found = GameOcrIconMatcher.TrySelectBestCandidateMatch(
            candidateMatches,
            out var candidate,
            out var matchInfo);

        Assert.True(found);
        Assert.Equal("best", candidate);
        Assert.Equal(0.97d, matchInfo.Score, 6);
        Assert.Equal(10, matchInfo.X);
        Assert.Equal(10, matchInfo.Y);
    }

    [Fact]
    public void TrySelectBestCandidateMatch_ReturnsFalse_WhenNoCandidateMeetsThreshold()
    {
        var candidateMatches = new[]
        {
            new CandidateMatch<string>("first", new TemplateMatchInfo(0, 0, 10, 10, 0.89d, 0.90d)),
            new CandidateMatch<string>("second", new TemplateMatchInfo(10, 10, 10, 10, 0.88d, 0.90d))
        };

        var found = GameOcrIconMatcher.TrySelectBestCandidateMatch(
            candidateMatches,
            out var candidate,
            out var matchInfo);

        Assert.False(found);
        Assert.Null(candidate);
        Assert.Equal(default, matchInfo);
    }

    [Fact]
    public void ScaleReferenceRect_ScalesExpertMapRegionToCurrentFrame()
    {
        var scaledRect = GameOcrSupport.ScaleReferenceRect(new Rect(360, 520, 360, 250), 1280, 720);

        Assert.Equal(240, scaledRect.X);
        Assert.Equal(347, scaledRect.Y);
        Assert.Equal(240, scaledRect.Width);
        Assert.Equal(166, scaledRect.Height);
    }
}
