namespace BetterLyrics.Core.Helpers.Lyrics
{
    public sealed record LyricsShareBatchExportProgress(
        int PageIndex,
        int ProcessedCount,
        int TotalCount);

    public sealed record LyricsShareBatchExportFailure(
        int PageIndex,
        Exception Error);

    public sealed record LyricsShareBatchExportResult(
        IReadOnlyList<int> CompletedPageIndices,
        IReadOnlyList<LyricsShareBatchExportFailure> Failures,
        bool IsCanceled);

    public static class LyricsShareBatchExporter
    {
        public static async Task<LyricsShareBatchExportResult> ExportAsync(
            IReadOnlyList<int> pageIndices,
            Func<int, CancellationToken, Task> exportPageAsync,
            IProgress<LyricsShareBatchExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(pageIndices);
            ArgumentNullException.ThrowIfNull(exportPageAsync);

            if (pageIndices.Any(pageIndex => pageIndex < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndices));
            }

            List<int> completedPageIndices = [];
            List<LyricsShareBatchExportFailure> failures = [];
            var processedCount = 0;

            foreach (var pageIndex in pageIndices)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new(completedPageIndices, failures, true);
                }

                try
                {
                    await exportPageAsync(pageIndex, cancellationToken);
                    completedPageIndices.Add(pageIndex);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new(completedPageIndices, failures, true);
                }
                catch (Exception ex)
                {
                    failures.Add(new(pageIndex, ex));
                }

                processedCount++;
                progress?.Report(new(pageIndex, processedCount, pageIndices.Count));
            }

            return new(completedPageIndices, failures, false);
        }
    }
}
