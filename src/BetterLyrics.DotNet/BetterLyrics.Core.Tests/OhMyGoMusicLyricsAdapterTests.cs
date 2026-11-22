using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Implementations.Services.GsmtcService;
using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Entities;
using Xunit;

namespace BetterLyrics.Core.Tests;

public class OhMyGoMusicLyricsAdapterTests
{
    [Fact]
    public void ExportsNormalizedLyricsWithTranslationAndOnlyRealWordTimes()
    {
        var line = new LyricsLine
        {
            StartMs = 1234, EndMs = 3000, PrimaryText = "hello world", IsPrimaryHasRealSyllableInfo = true,
            PrimarySyllables = [new() { StartMs = 1234, EndMs = 2000, Text = "hello " }, new() { StartMs = 2000, EndMs = 3000, Text = "world" }]
        };
        var source = new LyricsCacheItem
        {
            Raw = "<tt>original format</tt>",
            LyricsDataArr = [new([line]), new([new() { StartMs = 1234, PrimaryText = "你好世界" }]) { TrackType = LyricsTrackType.Translation }]
        };
        var output = OhMyGoMusicLyricsAdapter.ToWriteModel(source);
        Assert.Equal("[00:01.234]hello world\n", output.Lrc);
        Assert.Equal("[00:01.234]你好世界\n", output.Translation);
        Assert.Equal([1234, 2000], output.Lines.Single().Words.Select(word => word.Start));
        line.IsPrimaryHasRealSyllableInfo = false;
        Assert.Empty(OhMyGoMusicLyricsAdapter.ToWriteModel(source).Lines.Single().Words);
    }

    [Fact]
    public void TimedLyricsSurviveCacheCloneAndDatabaseRoundTrip()
    {
        var source = new OhMyGoMusicLyrics
        {
            Lines = [new() { Start = 1234, End = 3000, Text = "hello world",
                Words = [new() { Start = 1234, End = 2000, Text = "hello " }, new() { Start = 2000, End = 3000, Text = "world" }] }]
        };
        var cached = (LyricsCacheItem)OhMyGoMusicLyricsAdapter.ToCacheItem(source).Clone();
        using var database = new LiteDB.LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<LyricsCacheItem>("lyrics");
        collection.Insert(cached);
        var reloaded = collection.FindAll().Single();
        var words = reloaded.TimedLyrics!.Lines.Single().Words;
        Assert.Equal(3000, words[^1].End);
        Assert.Equal("hello ", words[0].Text);
        Assert.Equal("hello world", string.Concat(words.Select(word => word.Text)));
        Assert.Equal("[00:01.234]hello world", reloaded.Raw?.TrimEnd());
    }

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
