using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Implementations.Services.GsmtcService;
using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models.Lyrics;
using Xunit;

namespace BetterLyrics.Core.Tests;

public class OhMyGoMusicLyricsAdapterTests
{
    [Fact]
    public void BuildsLrcFromTimedSourceLinesAndKeepsTranslations()
    {
        var source = new OhMyGoMusicLyrics
        {
            TrackId = 7,
            Lines =
            [
                new OhMyGoMusicLyricLine
                {
                    Start = 1234,
                    End = 3000,
                    Text = "hello world",
                    Translation = "你好世界"
                }
            ]
        };

        var item = OhMyGoMusicLyricsAdapter.ToCacheItem(source);

        Assert.Equal(LyricsProvider.OhMyGoMusic, item.Provider);
        Assert.Equal("[00:01.234]hello world\n", item.Raw);
        Assert.Equal("[00:01.234]你好世界\n", item.Translation);
    }

    [Fact]
    public void AppliesOnlyActualSourceWordTimes()
    {
        var source = new OhMyGoMusicLyrics
        {
            TrackId = 7,
            Lines =
            [
                new OhMyGoMusicLyricLine
                {
                    Start = 1000,
                    End = 3000,
                    Text = "hello world",
                    Words =
                    [
                        new OhMyGoMusicLyricWord { Start = 1000, End = 2000, Text = "hello " },
                        new OhMyGoMusicLyricWord { Start = 2000, End = 3000, Text = "world" }
                    ]
                }
            ]
        };
        var line = new LyricsLine { StartMs = 1000, PrimaryText = "hello world" };
        var parsed = new LyricsData([line]);

        OhMyGoMusicLyricsAdapter.ApplyWordTimes(parsed, source);

        Assert.True(line.IsPrimaryHasRealSyllableInfo);
        Assert.Equal(3000, line.EndMs);
        Assert.Equal([1000, 2000], line.PrimarySyllables.Select(word => word.StartMs));
        Assert.Equal([2000, 3000], line.PrimarySyllables.Select(word => word.EndMs));
    }

    [Fact]
    public void MatchesWordTimesToRoundedLrcLineStarts()
    {
        var source = new OhMyGoMusicLyrics
        {
            TrackId = 7,
            Lrc = "[00:01.23]hello world",
            Lines =
            [
                new OhMyGoMusicLyricLine
                {
                    Start = 1234,
                    Text = "hello world",
                    Words = [new OhMyGoMusicLyricWord { Start = 1234, End = 2000, Text = "hello world" }]
                }
            ]
        };
        var line = new LyricsLine { StartMs = 1230, PrimaryText = "hello world" };

        OhMyGoMusicLyricsAdapter.ApplyWordTimes(new LyricsData([line]), source);

        Assert.True(line.IsPrimaryHasRealSyllableInfo);
        Assert.Equal(1234, Assert.Single(line.PrimarySyllables).StartMs);
    }

    [Fact]
    public void PreservesLineEndsWithoutClaimingEstimatedWordsAreReal()
    {
        var source = new OhMyGoMusicLyrics
        {
            Lines = [new OhMyGoMusicLyricLine { Start = 5000, End = 8000, Text = "line timing only" }]
        };
        var line = new LyricsLine { StartMs = 5000, EndMs = 5994, PrimaryText = "line timing only" };

        OhMyGoMusicLyricsAdapter.ApplyWordTimes(new LyricsData([line]), source);

        Assert.Equal(8000, line.EndMs);
        Assert.False(line.IsPrimaryHasRealSyllableInfo);
    }
}
