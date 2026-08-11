using BetterLyrics.Core.Helpers.Lyrics;
using BetterLyrics.Core.Models.Lyrics;
using System.Globalization;
using Xunit;

namespace BetterLyrics.Core.Tests;

public sealed class LyricsSharePaginatorTests
{
    [Theory]
    [InlineData("LyricsCardAncientBookStyle", 2, 15, 30)]
    [InlineData("LyricsCardBambooSlipsStyle", 4, 15, 60)]
    [InlineData("LyricsCardMinimalStyle", 6, 240, 240)]
    [InlineData(null, 6, 240, 240)]
    public void GetLimitsForStyle_ReturnsStyleSpecificBudgets(
        string? styleKey,
        int expectedMaxLinesPerPage,
        int expectedMaxCharactersPerLine,
        int expectedMaxCharactersPerPage)
    {
        var limits = LyricsSharePaginator.GetLimitsForStyle(styleKey);

        Assert.Equal(expectedMaxLinesPerPage, limits.MaxLinesPerPage);
        Assert.Equal(expectedMaxCharactersPerLine, limits.MaxCharactersPerLine);
        Assert.Equal(expectedMaxCharactersPerPage, limits.MaxCharactersPerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Paginate_NonPositiveLineBudgetThrows(int maxCharactersPerLine)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LyricsSharePaginator.Paginate(
            [Line("text")],
            maxCharactersPerLine: maxCharactersPerLine));
    }

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
    public void Paginate_SplitsOversizedLineAndKeepsEveryPageWithinHardLimit()
    {
        var oversized = Line(new string('x', 50));

        var pages = LyricsSharePaginator.Paginate([oversized, Line("next")], 8, 10);

        Assert.All(pages, page =>
            Assert.InRange(page.Sum(LyricsSharePaginator.GetDisplayedCharacterCount), 1, 10));
        Assert.Equal(new string('x', 50) + "next", ReconstructPrimaryText(pages));
    }

    [Fact]
    public void Paginate_IndependentLineBudgetLimitsEveryFragmentAndPage()
    {
        var transcript = new string('x', 23);

        var pages = LyricsSharePaginator.Paginate(
            [Line(transcript)],
            maxLinesPerPage: 10,
            maxCharactersPerPage: 20,
            maxCharactersPerLine: 7);

        Assert.All(pages.SelectMany(page => page), fragment =>
            Assert.InRange(LyricsSharePaginator.GetDisplayedCharacterCount(fragment), 1, 7));
        Assert.All(pages, page =>
            Assert.InRange(page.Sum(LyricsSharePaginator.GetDisplayedCharacterCount), 1, 20));
        Assert.Equal(transcript, ReconstructPrimaryText(pages));
    }

    [Fact]
    public void Paginate_LineBudgetCannotExceedPageBudget()
    {
        var transcript = new string('\u9875', 13);

        var pages = LyricsSharePaginator.Paginate(
            [Line(transcript)],
            maxLinesPerPage: 10,
            maxCharactersPerPage: 5,
            maxCharactersPerLine: 10);

        Assert.Equal([5, 5, 3], pages.SelectMany(page => page)
            .Select(LyricsSharePaginator.GetDisplayedCharacterCount));
        Assert.All(pages, page =>
            Assert.InRange(page.Sum(LyricsSharePaginator.GetDisplayedCharacterCount), 1, 5));
        Assert.Equal(transcript, ReconstructPrimaryText(pages));
    }

    [Fact]
    public void Paginate_TwoThousandChineseCharacters_NeverExceedsDefaultBudget()
    {
        var transcript = new string('\u64ad', 2_000);

        var pages = LyricsSharePaginator.Paginate([Line(transcript)]);

        Assert.True(pages.Count > 1);
        Assert.All(pages, page =>
            Assert.InRange(
                page.Sum(LyricsSharePaginator.GetDisplayedCharacterCount),
                1,
                LyricsSharePaginator.DefaultMaxCharactersPerPage));
        Assert.Equal(transcript, ReconstructPrimaryText(pages));
    }

    [Fact]
    public void Paginate_NoWhitespaceUrl_SplitsWithoutLosingContent()
    {
        var url = $"https://example.com/articles/{new string('a', 1_000)}?source=podcast";

        var pages = LyricsSharePaginator.Paginate([Line(url)]);

        Assert.All(pages, page =>
            Assert.True(page.Sum(LyricsSharePaginator.GetDisplayedCharacterCount) <= 240));
        Assert.Equal(url, ReconstructPrimaryText(pages));
    }

    [Fact]
    public void Paginate_EmojiAndCombiningCharacters_SplitsOnlyAtTextElementBoundaries()
    {
        const string emoji = "\U0001F469\U0001F3FD\u200D\U0001F4BB";
        const string combinedLetter = "e\u0301";
        var transcript = string.Concat(Enumerable.Repeat(emoji + combinedLetter, 180));

        var pages = LyricsSharePaginator.Paginate([Line(transcript)], maxLinesPerPage: 6, maxCharactersPerPage: 25);

        Assert.Equal(transcript, ReconstructPrimaryText(pages));
        Assert.All(pages.SelectMany(page => page), line =>
        {
            Assert.True(LyricsSharePaginator.GetDisplayedCharacterCount(line) <= 25);
            var enumerator = StringInfo.GetTextElementEnumerator(line.PrimaryText);
            while (enumerator.MoveNext())
            {
                Assert.Contains(enumerator.GetTextElement(), new[] { emoji, combinedLetter });
            }
        });
    }

    [Fact]
    public void Paginate_PreservesExactUnicodeTextWithoutSyntheticContinuationCharacters()
    {
        const string emoji = "\U0001F469\U0001F3FD\u200D\U0001F4BB";
        const string combinedLetter = "e\u0301";
        var primary = $"\u524d\u2026{emoji}{combinedLetter}\u540e\u7eed";
        var secondary = $"\u8bd1\u2026{emoji}\u6587";

        var pages = LyricsSharePaginator.Paginate(
            [Line(primary, secondary)],
            maxLinesPerPage: 4,
            maxCharactersPerPage: 5,
            maxCharactersPerLine: 3);

        Assert.Equal(primary, ReconstructField(pages, line => line.PrimaryText));
        Assert.Equal(secondary, ReconstructField(pages, line => line.SecondaryText));
        Assert.All(pages.SelectMany(page => page), fragment =>
            Assert.InRange(LyricsSharePaginator.GetDisplayedCharacterCount(fragment), 1, 3));
    }

    [Fact]
    public void Paginate_PrimaryAndSecondaryText_ShareOneHardBudget()
    {
        var line = Line(new string('a', 12), new string('b', 12));

        var pages = LyricsSharePaginator.Paginate([line], maxLinesPerPage: 8, maxCharactersPerPage: 10);

        Assert.All(pages, page =>
            Assert.True(page.Sum(LyricsSharePaginator.GetDisplayedCharacterCount) <= 10));
        Assert.Equal(new string('a', 12), ReconstructField(pages, item => item.PrimaryText));
        Assert.Equal(new string('b', 12), ReconstructField(pages, item => item.SecondaryText));
        Assert.All(pages.SelectMany(page => page), item => Assert.Empty(item.TertiaryText));
    }

    [Fact]
    public void Paginate_TertiaryTextOnly_DoesNotCreateBlankPage()
    {
        var line = Line(string.Empty, tertiary: new string('c', 500));

        var pages = LyricsSharePaginator.Paginate([line]);

        Assert.Equal(0, LyricsSharePaginator.GetDisplayedCharacterCount(line));
        Assert.Empty(pages);
    }

    private static LyricsLine Line(string primary, string secondary = "", string tertiary = "") => new()
    {
        PrimaryText = primary,
        SecondaryText = secondary,
        TertiaryText = tertiary,
    };

    private static string ReconstructPrimaryText(IReadOnlyList<IReadOnlyList<LyricsLine>> pages) =>
        ReconstructField(pages, line => line.PrimaryText);

    private static string ReconstructField(
        IReadOnlyList<IReadOnlyList<LyricsLine>> pages,
        Func<LyricsLine, string> selector) =>
        string.Concat(pages.SelectMany(page => page).Select(selector));
}
