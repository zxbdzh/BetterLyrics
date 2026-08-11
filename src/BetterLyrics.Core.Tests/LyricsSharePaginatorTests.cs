using BetterLyrics.Core.Helpers.Lyrics;
using BetterLyrics.Core.Models.Lyrics;
using Xunit;

namespace BetterLyrics.Core.Tests;

public sealed class LyricsSharePaginatorTests
{
    [Fact]
    public void Paginate_EmptySelection_ReturnsNoPages()
    {
        var pages = LyricsSharePaginator.Paginate([]);

        Assert.Empty(pages);
    }

    [Fact]
    public void Paginate_SplitsAtLineLimitAndPreservesOrder()
    {
        var lines = Enumerable.Range(1, 5).Select(index => Line($"line-{index}")).ToList();

        var pages = LyricsSharePaginator.Paginate(lines, maxLinesPerPage: 2, maxCharactersPerPage: 100);

        Assert.Equal([2, 2, 1], pages.Select(page => page.Count));
        Assert.Equal(lines, pages.SelectMany(page => page));
    }

    [Fact]
    public void Paginate_DefaultLimitKeepsShareCardsCompact()
    {
        var lines = Enumerable.Range(1, 7).Select(index => Line($"line-{index}")).ToList();

        var pages = LyricsSharePaginator.Paginate(lines);

        Assert.Equal([6, 1], pages.Select(page => page.Count));
    }

    [Fact]
    public void Paginate_SplitsLongTranscriptByDisplayedCharacterBudget()
    {
        var lines = new[]
        {
            Line("123456", "abcd"),
            Line("12345"),
            Line("last"),
        };

        var pages = LyricsSharePaginator.Paginate(lines, maxLinesPerPage: 8, maxCharactersPerPage: 12);

        Assert.Equal([1, 2], pages.Select(page => page.Count));
        Assert.Same(lines[0], pages[0][0]);
        Assert.Same(lines[1], pages[1][0]);
        Assert.Same(lines[2], pages[1][1]);
    }

    [Fact]
    public void Paginate_KeepsOversizedLineIntact()
    {
        var oversized = Line(new string('x', 50));

        var pages = LyricsSharePaginator.Paginate([oversized, Line("next")], 8, 10);

        Assert.Equal(2, pages.Count);
        Assert.Single(pages[0]);
        Assert.Same(oversized, pages[0][0]);
    }

    private static LyricsLine Line(string primary, string secondary = "", string tertiary = "") => new()
    {
        PrimaryText = primary,
        SecondaryText = secondary,
        TertiaryText = tertiary,
    };
}
