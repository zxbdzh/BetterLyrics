using BetterLyrics.Core.Models.Lyrics;

namespace BetterLyrics.Core.Helpers.Lyrics
{
    public static class LyricsSharePaginator
    {
        public const int DefaultMaxLinesPerPage = 6;
        public const int DefaultMaxCharactersPerPage = 240;

        public static IReadOnlyList<IReadOnlyList<LyricsLine>> Paginate(
            IEnumerable<LyricsLine> lyrics,
            int maxLinesPerPage = DefaultMaxLinesPerPage,
            int maxCharactersPerPage = DefaultMaxCharactersPerPage)
        {
            ArgumentNullException.ThrowIfNull(lyrics);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxLinesPerPage, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxCharactersPerPage, 1);

            List<IReadOnlyList<LyricsLine>> pages = [];
            List<LyricsLine> currentPage = [];
            var currentCharacterCount = 0;

            foreach (var line in lyrics)
            {
                var lineCharacterCount = Math.Max(
                    1,
                    line.PrimaryText.Length + line.SecondaryText.Length + line.TertiaryText.Length);
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
    }
}
