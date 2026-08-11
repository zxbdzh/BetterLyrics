using BetterLyrics.Core.Helpers.Lyrics;
using Xunit;

namespace BetterLyrics.Core.Tests;

public sealed class LyricsShareBatchExporterTests
{
    [Fact]
    public async Task ExportAsync_PreservesRequestedPageOrderAndReportsProgress()
    {
        List<int> exported = [];
        List<LyricsShareBatchExportProgress> progress = [];

        var result = await LyricsShareBatchExporter.ExportAsync(
            [3, 1, 4],
            (pageIndex, _) =>
            {
                exported.Add(pageIndex);
                return Task.CompletedTask;
            },
            new ImmediateProgress<LyricsShareBatchExportProgress>(progress.Add));

        Assert.Equal([3, 1, 4], exported);
        Assert.Equal([3, 1, 4], result.CompletedPageIndices);
        Assert.Empty(result.Failures);
        Assert.False(result.IsCanceled);
        Assert.Equal([1, 2, 3], progress.Select(item => item.ProcessedCount));
        Assert.All(progress, item => Assert.Equal(3, item.TotalCount));
    }

    [Fact]
    public async Task ExportAsync_CancellationStopsBeforeTheNextPage()
    {
        using CancellationTokenSource cancellation = new();
        List<int> exported = [];

        var result = await LyricsShareBatchExporter.ExportAsync(
            [0, 1, 2, 3],
            (pageIndex, _) =>
            {
                exported.Add(pageIndex);
                if (pageIndex == 1) cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellationToken: cancellation.Token);

        Assert.Equal([0, 1], exported);
        Assert.Equal([0, 1], result.CompletedPageIndices);
        Assert.True(result.IsCanceled);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ExportAsync_FailedPagesCanBeRetriedWithoutRepeatingCompletedPages()
    {
        List<int> firstAttempt = [];

        var firstResult = await LyricsShareBatchExporter.ExportAsync(
            [0, 1, 2],
            (pageIndex, _) =>
            {
                firstAttempt.Add(pageIndex);
                return pageIndex == 1
                    ? Task.FromException(new IOException("disk unavailable"))
                    : Task.CompletedTask;
            });

        Assert.Equal([0, 1, 2], firstAttempt);
        Assert.Equal([0, 2], firstResult.CompletedPageIndices);
        var failedPage = Assert.Single(firstResult.Failures);
        Assert.Equal(1, failedPage.PageIndex);

        List<int> retryAttempt = [];
        var retryResult = await LyricsShareBatchExporter.ExportAsync(
            firstResult.Failures.Select(failure => failure.PageIndex).ToList(),
            (pageIndex, _) =>
            {
                retryAttempt.Add(pageIndex);
                return Task.CompletedTask;
            });

        Assert.Equal([1], retryAttempt);
        Assert.Equal([1], retryResult.CompletedPageIndices);
        Assert.Empty(retryResult.Failures);
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
