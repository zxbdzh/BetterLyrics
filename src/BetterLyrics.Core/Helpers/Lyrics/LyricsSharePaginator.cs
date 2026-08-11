using BetterLyrics.Core.Models.Lyrics;
using System.Globalization;
using System.Text;

namespace BetterLyrics.Core.Helpers.Lyrics
{
    public sealed record LyricsSharePaginationLimits(
        int MaxLinesPerPage,
        int MaxCharactersPerLine,
        int MaxCharactersPerPage);

    public static class LyricsSharePaginator
    {
        public const int DefaultMaxLinesPerPage = 6;
        public const int DefaultMaxCharactersPerPage = 240;
        public const int DefaultMaxCharactersPerLine = 240;

        public static LyricsSharePaginationLimits GetLimitsForStyle(string? styleKey) =>
            styleKey switch
            {
                "LyricsCardAncientBookStyle" => new(2, 15, 30),
                "LyricsCardBambooSlipsStyle" => new(4, 15, 60),
                _ => new(
                    DefaultMaxLinesPerPage,
                    DefaultMaxCharactersPerLine,
                    DefaultMaxCharactersPerPage),
            };

        public static IReadOnlyList<IReadOnlyList<LyricsLine>> Paginate(
            IEnumerable<LyricsLine> lyrics,
            int maxLinesPerPage = DefaultMaxLinesPerPage,
            int maxCharactersPerPage = DefaultMaxCharactersPerPage,
            int maxCharactersPerLine = DefaultMaxCharactersPerLine)
        {
            ArgumentNullException.ThrowIfNull(lyrics);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxLinesPerPage, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxCharactersPerPage, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxCharactersPerLine, 1);

            List<IReadOnlyList<LyricsLine>> pages = [];
            List<LyricsLine> currentPage = [];
            var currentCharacterCount = 0;
            var effectiveMaxCharactersPerLine = Math.Min(
                maxCharactersPerLine,
                maxCharactersPerPage);

            foreach (var line in lyrics.SelectMany(line =>
                         SplitOversizedLine(line, effectiveMaxCharactersPerLine)))
            {
                var lineCharacterCount = GetDisplayedCharacterCount(line);
                var pageIsFull = currentPage.Count >= maxLinesPerPage ||
                                 currentCharacterCount + lineCharacterCount > maxCharactersPerPage;

                if (currentPage.Count > 0 && pageIsFull)
                {
                    pages.Add(currentPage);
                    currentPage = [];
                    currentCharacterCount = 0;
                }

                currentPage.Add(line);
                currentCharacterCount += lineCharacterCount;
            }

            if (currentPage.Count > 0) pages.Add(currentPage);
            return pages;
        }

        public static int GetDisplayedCharacterCount(LyricsLine line)
        {
            ArgumentNullException.ThrowIfNull(line);

            return CountTextElements(line.PrimaryText) +
                   CountTextElements(line.SecondaryText);
        }

        private static IEnumerable<LyricsLine> SplitOversizedLine(LyricsLine line, int maxCharacters)
        {
            var displayedCharacterCount = GetDisplayedCharacterCount(line);
            if (displayedCharacterCount == 0) yield break;

            if (displayedCharacterCount <= maxCharacters)
            {
                yield return line;
                yield break;
            }

            var parts = SplitPairedFields(
                line.PrimaryText,
                line.SecondaryText,
                maxCharacters);

            foreach (var part in parts)
            {
                yield return new LyricsLine
                {
                    StartMs = line.StartMs,
                    EndMs = line.EndMs,
                    AgentId = line.AgentId,
                    IsPrimaryHasRealSyllableInfo = false,
                    PrimaryText = part.Primary,
                    SecondaryText = part.Secondary,
                    TertiaryText = string.Empty,
                };
            }
        }

        private static IReadOnlyList<(string Primary, string Secondary)> SplitPairedFields(
            string primaryValue,
            string secondaryValue,
            int maxCharacters)
        {
            var primary = GetTextElements(primaryValue);
            var secondary = GetTextElements(secondaryValue);
            if (primary.Count == 0)
            {
                return ChunkTextElements(secondary, maxCharacters)
                    .Select(value => (string.Empty, value))
                    .ToList();
            }
            if (secondary.Count == 0)
            {
                return ChunkTextElements(primary, maxCharacters)
                    .Select(value => (value, string.Empty))
                    .ToList();
            }

            if (maxCharacters == 1)
            {
                return
                [
                    .. ChunkTextElements(primary, 1).Select(value => (value, string.Empty)),
                    .. ChunkTextElements(secondary, 1).Select(value => (string.Empty, value)),
                ];
            }

            var totalCharacters = primary.Count + secondary.Count;
            var primaryBudget = Math.Clamp(
                maxCharacters * primary.Count / totalCharacters,
                1,
                maxCharacters - 1);
            var secondaryBudget = maxCharacters - primaryBudget;
            var primaryParts = ChunkTextElements(primary, primaryBudget);
            var secondaryParts = ChunkTextElements(secondary, secondaryBudget);
            var partCount = Math.Max(primaryParts.Count, secondaryParts.Count);
            List<(string Primary, string Secondary)> parts = [];
            for (var index = 0; index < partCount; index++)
            {
                parts.Add((
                    index < primaryParts.Count ? primaryParts[index] : string.Empty,
                    index < secondaryParts.Count ? secondaryParts[index] : string.Empty));
            }
            return parts;
        }

        private static IReadOnlyList<string> ChunkTextElements(
            IReadOnlyList<string> values,
            int chunkSize)
        {
            List<string> chunks = [];
            for (var offset = 0; offset < values.Count; offset += chunkSize)
            {
                chunks.Add(ConcatRange(
                    values,
                    offset,
                    Math.Min(chunkSize, values.Count - offset)));
            }
            return chunks;
        }

        private static string ConcatRange(
            IReadOnlyList<string> values,
            int offset,
            int count)
        {
            StringBuilder builder = new();
            for (var index = offset; index < offset + count; index++)
            {
                builder.Append(values[index]);
            }
            return builder.ToString();
        }

        private static IReadOnlyList<string> GetTextElements(string value)
        {
            List<string> elements = [];
            var enumerator = StringInfo.GetTextElementEnumerator(value ?? string.Empty);
            while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
            return elements;
        }

        private static int CountTextElements(string value) =>
            new StringInfo(value ?? string.Empty).LengthInTextElements;
    }
}
