using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Settings;
using System.Text.Json;
using Xunit;

namespace BetterLyrics.Core.Tests;

public class LyricsLineTests
{
    [Fact]
    public void BuildsTimelineAfterTextAndSyllablesAreAssigned()
    {
        var line = new LyricsLine
        {
            PrimaryText = "你好世界",
            PrimarySyllables =
            [
                new BaseLyrics { Text = "你好", StartIndex = 0, StartMs = 1000, EndMs = 1400 },
                new BaseLyrics { Text = "世界", StartIndex = 2, StartMs = 1500, EndMs = 1900 }
            ]
        };

        line.RebuildPrimaryChars();

        Assert.Equal(["你", "好", "世", "界"], line.PrimaryChars.Select(character => character.Text));
        Assert.Equal([1000, 1200, 1500, 1700], line.PrimaryChars.Select(character => character.StartMs));
        Assert.Equal(1900, line.PrimaryChars[^1].EndMs);
    }

    [Fact]
    public void RebuildReplacesPreviousTimeline()
    {
        var line = new LyricsLine
        {
            PrimaryText = "AB",
            PrimarySyllables = [new BaseLyrics { Text = "AB", StartMs = 0, EndMs = 200 }]
        };
        line.RebuildPrimaryChars();
        line.PrimaryText = "C";
        line.PrimarySyllables = [new BaseLyrics { Text = "C", StartMs = 1000, EndMs = 1100 }];

        line.RebuildPrimaryChars();

        var character = Assert.Single(line.PrimaryChars);
        Assert.Equal("C", character.Text);
        Assert.Equal(1000, character.StartMs);
    }

    [Fact]
    public void DoesNotSplitSupplementaryUnicodeCharacters()
    {
        var line = new LyricsLine
        {
            PrimaryText = "A😀中",
            PrimarySyllables = [new BaseLyrics { Text = "A😀中", StartMs = 1000, EndMs = 1400 }]
        };

        line.RebuildPrimaryChars();

        Assert.Equal(["A", "😀", "中"], line.PrimaryChars.Select(character => character.Text));
        Assert.Equal([0, 1, 3], line.PrimaryChars.Select(character => character.StartIndex));
        Assert.Equal(1400, line.PrimaryChars[^1].EndMs);
    }

    [Fact]
    public void InvalidRangesAndShortSyllablesRemainSafe()
    {
        var line = new LyricsLine
        {
            PrimaryText = "AB",
            PrimarySyllables =
            [
                new BaseLyrics { Text = "AB", StartMs = 1000, EndMs = 1001 },
                new BaseLyrics { Text = "outside", StartIndex = 100, StartMs = 2000 }
            ]
        };

        line.RebuildPrimaryChars();

        Assert.Equal(2, line.PrimaryChars.Count);
        Assert.All(line.PrimaryChars, character => Assert.True(character.EndMs > character.StartMs));
    }

    [Fact]
    public void ExistingHaloSettingsRoundTripWithTheSameNames()
    {
        const string savedSettings = """
            {"Enable":true,"LyricMode":2,"AutoScroll":false,"ScrollThreshold":24,
             "Typewriter":false,"TypewriterSpeed":80,"TypewriterSync":true,
             "LatencyCompMs":160,"TextAlignment":2,"AlternateSplit":true,"AlternateInterval":2500}
            """;
        var settings = JsonSerializer.Deserialize<HaloPixelBarSettings>(savedSettings)!;

        var restored = JsonSerializer.Deserialize<HaloPixelBarSettings>(JsonSerializer.Serialize(settings))!;

        Assert.True(restored.Enable);
        Assert.True(restored.TypewriterSync);
        Assert.False(restored.Typewriter);
        Assert.Equal(2, restored.TextAlignment);
        Assert.Equal(160, restored.LatencyCompMs);
        Assert.Equal(2500, restored.AlternateInterval);
    }
}
