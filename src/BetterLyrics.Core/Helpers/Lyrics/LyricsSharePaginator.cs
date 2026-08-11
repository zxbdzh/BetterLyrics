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

            var parts = SplitFields(
                [line.PrimaryText, line.SecondaryText],
                maxCharacters);

            foreach (var part in parts)
            {
                yield return new LyricsLine
                {
                    StartMs = line.StartMs,
                    EndMs = line.EndMs,
                    AgentId = line.AgentId,
                    IsPrimaryHasRealSyllableInfo = false,
                    PrimaryText = part[0],
                    SecondaryText = part[1],
                    TertiaryText = string.Empty,
                };
            }
        }

        private static IReadOnlyList<string[]> SplitFields(
            IReadOnlyList<string> fieldValues,
            int maxCharacters)
        {
            List<string[]> parts = [];
            var currentPart = CreateEmptyPart(fieldValues.Count);
            var remainingCapacity = maxCharacters;

            void FlushPart()
            {
                if (currentPart.Any(value => !string.IsNullOrEmpty(value))) parts.Add(currentPart);
                currentPart = CreateEmptyPart(fieldValues.Count);
                remainingCapacity = maxCharacters;
            }

            for (var fieldIndex = 0; fieldIndex < fieldValues.Count; fieldIndex++)
            {
                var field = GetTextElements(fieldValues[fieldIndex]);
                var offset = 0;

                while (offset < field.Count)
                {
                    if (remainingCapacity <= 0)
                    {
                        FlushPart();
                        continue;
                    }

                    var charactersToTake = Math.Min(
                        field.Count - offset,
                        remainingCapacity);

                    var content = ConcatRange(field, offset, charactersToTake);
                    currentPart[fieldIndex] = content;
                    remainingCapacity -= charactersToTake;
                    offset += charactersToTake;

                    if (remainingCapacity == 0 || offset < field.Count) FlushPart();
                }
            }

            FlushPart();
            return parts;
        }

        private static string[] CreateEmptyPart(int fieldCount) =>
            Enumerable.Repeat(string.Empty, fieldCount).ToArray();

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
