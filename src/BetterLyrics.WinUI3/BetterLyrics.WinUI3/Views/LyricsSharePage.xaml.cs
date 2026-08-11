using BetterLyrics.Core.Constants;
using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Helpers.Lyrics;
using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Settings;
using BetterLyrics.WinUI3.Helper;
using BetterLyrics.WinUI3.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BetterLyrics.WinUI3.Views
{
    public sealed partial class LyricsSharePage : Page
    {
        private readonly ILocalizationService _localizationService;
        private CancellationTokenSource? _exportCancellation;
        private Control? _focusBeforePreview;

        public LyricsSharePageViewModel ViewModel { get; set; }

        public LyricsSharePage()
        {
            InitializeComponent();
            ViewModel = Ioc.Default.GetRequiredService<LyricsSharePageViewModel>();
            _localizationService = Ioc.Default.GetRequiredService<ILocalizationService>();
        }

        private void LyricsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var totalCount = LyricsListView.Items.Count;
            var selectedCount = LyricsListView.SelectedItems.Count;

            if (selectedCount == 0) LyricsHostCheckBox.IsChecked = false;
            else if (selectedCount == totalCount) LyricsHostCheckBox.IsChecked = true;
            else LyricsHostCheckBox.IsChecked = null;

            var selectedLyrics = LyricsListView.SelectedItems.Cast<LyricsLine>().ToHashSet();
            ViewModel.UpdateSelectedLyrics(LyricsListView.Items
                .Cast<LyricsLine>()
                .Where(selectedLyrics.Contains)
                .ToList());
        }

        private async void SaveImage_Click(SplitButton sender, SplitButtonClickEventArgs args)
        {
            if (_exportCancellation != null) return;

            using CancellationTokenSource cancellation = new();
            _exportCancellation = cancellation;
            var isPreviewModeActive = false;
            LyricsShareExportSnapshot? snapshot = null;
            try
            {
                snapshot = ViewModel.BeginExport();
                var pageIndex = snapshot.OriginalPageIndex;
                var timestamp = DateTime.Now;
                var imageScaleFactor = ImageQualitySlider.Value / 100.0 * 4.0;
                ShowSnapshotPage(snapshot, pageIndex);

                var file = await PickerHelper.PickSaveFileAsync<LyricsShareWindow>(
                    new Dictionary<string, IList<string>> { { "PNG Image", new List<string> { ".png" } } },
                    BuildPageFileName(pageIndex, snapshot.PageCount, timestamp, snapshot.StyleKey));
                if (file == null) return;
                cancellation.Token.ThrowIfCancellationRequested();

                isPreviewModeActive = true;
                await SetPreviewModeAsync(true);
                cancellation.Token.ThrowIfCancellationRequested();
                ShowSnapshotPage(snapshot, pageIndex);

                using var memoryStream = await RenderToStreamAsync(
                    ExportCard,
                    imageScaleFactor);
                cancellation.Token.ThrowIfCancellationRequested();
                await WriteStreamToFileAtomicallyAsync(
                    memoryStream,
                    file,
                    cancellation.Token);

                GlobalToastManager.Show("ActionCompleted", file.Path, MessageSeverity.Success);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                GlobalToastManager.Show("Error", ex.Message, MessageSeverity.Error);
            }
            finally
            {
                if (isPreviewModeActive) await SetPreviewModeAsync(false);
                if (snapshot != null)
                {
                    ViewModel.EndExport(snapshot);
                    RestoreLivePreview();
                }

                _exportCancellation = null;
            }
        }

        private async void CopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (_exportCancellation != null) return;

            using CancellationTokenSource cancellation = new();
            _exportCancellation = cancellation;
            var isPreviewModeActive = false;
            LyricsShareExportSnapshot? snapshot = null;
            try
            {
                snapshot = ViewModel.BeginExport();
                var pageIndex = snapshot.OriginalPageIndex;
                var imageScaleFactor = ImageQualitySlider.Value / 100.0 * 4.0;
                ShowSnapshotPage(snapshot, pageIndex);

                isPreviewModeActive = true;
                await SetPreviewModeAsync(true);
                cancellation.Token.ThrowIfCancellationRequested();
                ShowSnapshotPage(snapshot, pageIndex);

                var memoryStream = await RenderToStreamAsync(
                    ExportCard,
                    imageScaleFactor);
                cancellation.Token.ThrowIfCancellationRequested();
                var dataPackage = new DataPackage
                {
                    RequestedOperation = DataPackageOperation.Copy,
                };

                dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(memoryStream));
                Clipboard.SetContent(dataPackage);

                GlobalToastManager.Show("ActionCompleted", null, MessageSeverity.Success);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                GlobalToastManager.Show("Error", ex.Message, MessageSeverity.Error);
            }
            finally
            {
                if (isPreviewModeActive) await SetPreviewModeAsync(false);
                if (snapshot != null)
                {
                    ViewModel.EndExport(snapshot);
                    RestoreLivePreview();
                }

                _exportCancellation = null;
            }
        }

        private async void SaveAllPages_Click(object sender, RoutedEventArgs e)
        {
            await SavePagesAsync((snapshot, _) => Task.FromResult<IReadOnlyList<int>?>(
                Enumerable.Range(0, snapshot.PageCount).ToList()));
        }

        private async void SavePageRange_Click(object sender, RoutedEventArgs e)
        {
            await SavePagesAsync((snapshot, cancellationToken) =>
                PickPageRangeAsync(snapshot.PageCount, cancellationToken));
        }

        private async Task<IReadOnlyList<int>?> PickPageRangeAsync(
            int pageCount,
            CancellationToken cancellationToken)
        {
            if (pageCount == 0 || cancellationToken.IsCancellationRequested) return null;

            var startPageBox = CreatePageRangeNumberBox(
                Localize("LyricsSharePageExportRangeStart"),
                1,
                pageCount);
            var endPageBox = CreatePageRangeNumberBox(
                Localize("LyricsSharePageExportRangeEnd"),
                pageCount,
                pageCount);
            var errorInfoBar = new InfoBar
            {
                IsClosable = false,
                IsOpen = false,
                Severity = InfoBarSeverity.Error,
            };
            var content = new StackPanel
            {
                MinWidth = 320,
                Spacing = 12,
            };
            content.Children.Add(startPageBox);
            content.Children.Add(endPageBox);
            content.Children.Add(errorInfoBar);

            (int StartPage, int EndPage)? selectedRange = null;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = Localize("LyricsSharePageExportRangeTitle"),
                PrimaryButtonText = Localize("LyricsSharePageExportRangeConfirm"),
                CloseButtonText = Localize("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = content,
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (!TryGetPageNumber(startPageBox.Value, out var startPage) ||
                    !TryGetPageNumber(endPageBox.Value, out var endPage) ||
                    startPage > endPage)
                {
                    args.Cancel = true;
                    errorInfoBar.Message = FormatLocalized(
                        "LyricsSharePageExportRangeInvalidFormat",
                        pageCount);
                    errorInfoBar.IsOpen = true;
                    startPageBox.Focus(FocusState.Programmatic);
                    return;
                }

                selectedRange = (startPage, endPage);
            };

            using var cancellationRegistration = cancellationToken.Register(() =>
                _ = DispatcherQueue.TryEnqueue(dialog.Hide));
            var result = await dialog.ShowAsync();
            if (cancellationToken.IsCancellationRequested ||
                result != ContentDialogResult.Primary ||
                selectedRange == null)
            {
                return null;
            }

            return Enumerable.Range(
                    selectedRange.Value.StartPage - 1,
                    selectedRange.Value.EndPage - selectedRange.Value.StartPage + 1)
                .ToList();
        }

        private static NumberBox CreatePageRangeNumberBox(
            string header,
            int value,
            int pageCount)
        {
            var numberBox = new NumberBox
            {
                Header = header,
                Minimum = 1,
                Maximum = pageCount,
                SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
                Value = value,
            };
            AutomationProperties.SetName(numberBox, header);
            return numberBox;
        }

        private async Task SavePagesAsync(
            Func<
                LyricsShareExportSnapshot,
                CancellationToken,
                Task<IReadOnlyList<int>?>> selectPageIndicesAsync)
        {
            if (_exportCancellation != null) return;

            using CancellationTokenSource cancellation = new();
            _exportCancellation = cancellation;
            LyricsShareExportSnapshot? snapshot = null;

            try
            {
                snapshot = ViewModel.BeginExport();
                var imageScaleFactor = ImageQualitySlider.Value / 100.0 * 4.0;
                var timestamp = DateTime.Now;
                ShowSnapshotPage(snapshot, snapshot.OriginalPageIndex);
                var pageIndices = await selectPageIndicesAsync(snapshot, cancellation.Token);
                if (pageIndices == null || pageIndices.Count == 0 ||
                    cancellation.IsCancellationRequested)
                {
                    return;
                }

                var requestedPageIndices = pageIndices.Distinct().ToList();
                if (requestedPageIndices.Any(pageIndex =>
                        pageIndex < 0 || pageIndex >= snapshot.PageCount))
                {
                    throw new InvalidOperationException(Localize("LyricsSharePageExportPageUnavailable"));
                }

                var folder = await PickerHelper.PickSingleFolderAsync<LyricsShareWindow>();
                if (folder == null || cancellation.IsCancellationRequested) return;

                var pendingPageIndices = requestedPageIndices;
                var completedPageIndices = new HashSet<int>();

                while (pendingPageIndices.Count > 0)
                {
                    LyricsShareBatchExportResult exportResult;
                    var isPreviewModeActive = false;

                    try
                    {
                        isPreviewModeActive = true;
                        await SetPreviewModeAsync(true, true, pendingPageIndices.Count);

                        var progress = new InlineProgress<LyricsShareBatchExportProgress>(value =>
                        {
                            ExportProgressBar.Value = value.ProcessedCount;
                            AutomationProperties.SetName(
                                ExportProgressBar,
                                FormatLocalized(
                                    "LyricsSharePageExportProgressAutomationFormat",
                                    value.ProcessedCount,
                                    value.TotalCount));
                        });
                        exportResult = await LyricsShareBatchExporter.ExportAsync(
                            pendingPageIndices,
                            (pageIndex, token) => ExportPageAsync(
                                folder,
                                snapshot,
                                pageIndex,
                                timestamp,
                                imageScaleFactor,
                                token),
                            progress,
                            cancellation.Token);
                    }
                    finally
                    {
                        _ = ViewModel.ShowExportPage(snapshot, snapshot.OriginalPageIndex);
                        if (isPreviewModeActive) await SetPreviewModeAsync(false);
                    }

                    foreach (var pageIndex in exportResult.CompletedPageIndices)
                    {
                        completedPageIndices.Add(pageIndex);
                    }

                    if (exportResult.IsCanceled)
                    {
                        GlobalToastManager.Show(
                            "ActionCompleted",
                            FormatLocalized(
                                "LyricsSharePageExportCanceledFormat",
                                completedPageIndices.Count,
                                requestedPageIndices.Count),
                            MessageSeverity.Warning);
                        return;
                    }

                    if (exportResult.Failures.Count == 0)
                    {
                        GlobalToastManager.Show(
                            "ActionCompleted",
                            FormatLocalized(
                                "LyricsSharePageExportSuccessFormat",
                                completedPageIndices.Count,
                                folder.Path),
                            MessageSeverity.Success);
                        return;
                    }

                    if (!await ConfirmRetryFailedPagesAsync(
                            exportResult.Failures,
                            folder.Path,
                            cancellation.Token))
                    {
                        if (cancellation.IsCancellationRequested) return;

                        GlobalToastManager.Show(
                            "ActionCompleted",
                            FormatLocalized(
                                "LyricsSharePageExportPartialFormat",
                                completedPageIndices.Count,
                                requestedPageIndices.Count,
                                folder.Path),
                            MessageSeverity.Warning);
                        return;
                    }

                    pendingPageIndices = exportResult.Failures
                        .Select(failure => failure.PageIndex)
                        .ToList();
                }
            }
            catch (Exception) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                GlobalToastManager.Show("Error", ex.Message, MessageSeverity.Error);
            }
            finally
            {
                if (snapshot != null)
                {
                    ViewModel.EndExport(snapshot);
                    RestoreLivePreview();
                }

                _exportCancellation = null;
            }
        }

        private void ShowSnapshotPage(
            LyricsShareExportSnapshot snapshot,
            int pageIndex)
        {
            if (snapshot.PageCount > 0 && !ViewModel.ShowExportPage(snapshot, pageIndex))
            {
                throw new InvalidOperationException(Localize("LyricsSharePageExportPageUnavailable"));
            }

            PreviewCard.StyleKey = snapshot.StyleKey;
            PreviewCard.Config = new LyricsCardConfig
            {
                ResourceKey = snapshot.StyleKey,
                FontFamily = snapshot.FontFamily,
            };
            ExportCard.UpdateLayout();
        }

        private void RestoreLivePreview()
        {
            PreviewCard.StyleKey = ViewModel.SelectedStyleItem.StyleKey;
            PreviewCard.Config = ViewModel.CardConfig;
        }

        private async Task ExportPageAsync(
            StorageFolder folder,
            LyricsShareExportSnapshot snapshot,
            int pageIndex,
            DateTime timestamp,
            double imageScaleFactor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShowSnapshotPage(snapshot, pageIndex);

            ProcessingStatusText.Text = FormatLocalized(
                "LyricsSharePageExportingPageFormat",
                pageIndex + 1,
                snapshot.PageCount);
            await Task.Yield();

            cancellationToken.ThrowIfCancellationRequested();
            using var memoryStream = await RenderToStreamAsync(ExportCard, imageScaleFactor);
            cancellationToken.ThrowIfCancellationRequested();

            var finalFileName = BuildPageFileName(
                pageIndex,
                snapshot.PageCount,
                timestamp,
                snapshot.StyleKey);
            StorageFile? temporaryFile = null;
            try
            {
                temporaryFile = await folder.CreateFileAsync(
                    $"{finalFileName}.tmp",
                    CreationCollisionOption.GenerateUniqueName);
                await WriteStreamToFileAsync(memoryStream, temporaryFile);
                cancellationToken.ThrowIfCancellationRequested();
                await temporaryFile.RenameAsync(
                    finalFileName,
                    NameCollisionOption.GenerateUniqueName);
            }
            catch
            {
                if (temporaryFile != null)
                {
                    try
                    {
                        await temporaryFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                    catch
                    {
                        // Keep the original export error so the retry dialog remains actionable.
                    }
                }

                throw;
            }
        }

        private async Task<bool> ConfirmRetryFailedPagesAsync(
            IReadOnlyList<LyricsShareBatchExportFailure> failures,
            string folderPath,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return false;

            var failedPages = string.Join(", ", failures.Select(failure => failure.PageIndex + 1));
            var summary = FormatLocalized(
                "LyricsSharePageExportFailedSummaryFormat",
                failures.Count,
                failedPages,
                folderPath,
                failures[0].Error.Message);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = Localize("LyricsSharePageExportFailedTitle"),
                PrimaryButtonText = Localize("LyricsSharePageExportRetry"),
                CloseButtonText = Localize("LyricsSharePageExportFinish"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new TextBlock
                {
                    MaxWidth = 440,
                    Text = summary,
                    TextWrapping = TextWrapping.Wrap,
                },
            };

            using var cancellationRegistration = cancellationToken.Register(() =>
                _ = DispatcherQueue.TryEnqueue(dialog.Hide));
            var result = await dialog.ShowAsync();
            return !cancellationToken.IsCancellationRequested &&
                   result == ContentDialogResult.Primary;
        }

        private async Task SetPreviewModeAsync(
            bool isPreviewing,
            bool isBatchExport = false,
            int batchSize = 0)
        {
            if (isPreviewing)
            {
                _focusBeforePreview = XamlRoot == null
                    ? null
                    : FocusManager.GetFocusedElement(XamlRoot) as Control;
                LyricsSelectionRegion.IsEnabled = false;
                PreviewToolbar.IsEnabled = false;
                PaginationControls.IsEnabled = false;
                StyleSelectionRegion.IsEnabled = false;
                SaveImageButton.IsEnabled = false;
                CopyImageButton.IsEnabled = false;
                ProcessingRing.Visibility = isBatchExport ? Visibility.Collapsed : Visibility.Visible;
                ProcessingRing.IsActive = !isBatchExport;
                ExportProgressBar.Visibility = isBatchExport ? Visibility.Visible : Visibility.Collapsed;
                ExportProgressBar.Maximum = Math.Max(batchSize, 1);
                ExportProgressBar.Value = 0;
                CancelExportButton.Visibility = Visibility.Visible;
                CancelExportButton.IsEnabled = true;

                ProcessingOverlay.Visibility = Visibility.Visible;
                ProcessingOverlay.Opacity = 1;
                ProcessingStatusText.Text = isBatchExport
                    ? FormatLocalized("LyricsSharePageExportProgressAutomationFormat", 0, batchSize)
                    : Localize("LyricsSharePageProcessingImage");
                await Task.Delay(Time.AnimationDuration);

                PreviewCardContainer.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill;
                LeftColDef.Width = RightColDef.Width = new GridLength(0, GridUnitType.Pixel);
                ExportCard.UpdateLayout();
                CancelExportButton.Focus(FocusState.Programmatic);
            }
            else
            {
                var focusToRestore = _focusBeforePreview;
                _focusBeforePreview = null;
                LeftColDef.Width = RightColDef.Width = new GridLength(1, GridUnitType.Star);
                PreviewCardContainer.Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform;

                ProcessingOverlay.Opacity = 0;
                await Task.Delay(Time.AnimationDuration);
                ProcessingOverlay.Visibility = Visibility.Collapsed;

                ProcessingRing.IsActive = false;
                CancelExportButton.Visibility = Visibility.Collapsed;
                ExportProgressBar.Visibility = Visibility.Collapsed;
                ProcessingStatusText.Text = string.Empty;
                LyricsSelectionRegion.IsEnabled = true;
                PreviewToolbar.IsEnabled = true;
                PaginationControls.IsEnabled = true;
                StyleSelectionRegion.IsEnabled = true;
                SaveImageButton.IsEnabled = true;
                CopyImageButton.IsEnabled = true;
                if (focusToRestore?.Focus(FocusState.Programmatic) != true && XamlRoot != null)
                {
                    SaveImageButton.Focus(FocusState.Programmatic);
                }
            }
        }

        private async Task<InMemoryRandomAccessStream> RenderToStreamAsync(
            UIElement element,
            double scaleFactor = 4.0)
        {
            var frameworkElement = (FrameworkElement)element;
            var renderWidth = (int)(frameworkElement.ActualWidth * scaleFactor);
            var renderHeight = (int)(frameworkElement.ActualHeight * scaleFactor);

            RenderTargetBitmap renderTargetBitmap = new();
            await renderTargetBitmap.RenderAsync(element, renderWidth, renderHeight);

            var pixelBuffer = await renderTargetBitmap.GetPixelsAsync();
            var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);

            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)renderTargetBitmap.PixelWidth,
                (uint)renderTargetBitmap.PixelHeight,
                96,
                96,
                pixelBuffer.ToArray());

            await encoder.FlushAsync();
            stream.Seek(0);
            return stream;
        }

        private static async Task WriteStreamToFileAsync(
            InMemoryRandomAccessStream memoryStream,
            StorageFile file)
        {
            using var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite);
            fileStream.Size = 0;
            await RandomAccessStream.CopyAndCloseAsync(
                memoryStream.GetInputStreamAt(0),
                fileStream.GetOutputStreamAt(0));
        }

        private static async Task WriteStreamToFileAtomicallyAsync(
            InMemoryRandomAccessStream memoryStream,
            StorageFile file,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var transaction = await file.OpenTransactedWriteAsync();
            transaction.Stream.Size = 0;
            using (var inputStream = memoryStream.GetInputStreamAt(0))
            using (var outputStream = transaction.Stream.GetOutputStreamAt(0))
            {
                await RandomAccessStream.CopyAsync(inputStream, outputStream);
                await outputStream.FlushAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync();
        }

        private string BuildPageFileName(
            int pageIndex,
            int pageCount,
            DateTime timestamp,
            string? styleKey = null)
        {
            var pageNumberWidth = Math.Max(
                3,
                pageCount.ToString(CultureInfo.InvariantCulture).Length);
            var pageNumberFormat = $"D{pageNumberWidth}";
            var pageSuffix = pageCount > 1
                ? $"_p{(pageIndex + 1).ToString(pageNumberFormat, CultureInfo.InvariantCulture)}" +
                  $"-of-{pageCount.ToString(pageNumberFormat, CultureInfo.InvariantCulture)}"
                : string.Empty;
            return $"BetterLyrics_{styleKey ?? ViewModel.SelectedStyleItem.StyleKey}" +
                   $"{pageSuffix}_{timestamp:yyyyMMdd_HHmmss_fff}.png";
        }

        private static bool TryGetPageNumber(double value, out int pageNumber)
        {
            if (!double.IsFinite(value) || value != Math.Truncate(value))
            {
                pageNumber = 0;
                return false;
            }

            pageNumber = (int)value;
            return pageNumber > 0;
        }

        private string Localize(string key) => _localizationService.GetLocalizedString(key);

        private string FormatLocalized(string key, params object[] values) =>
            string.Format(CultureInfo.CurrentCulture, Localize(key), values);

        private void LyricsHostCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (LyricsHostCheckBox.IsChecked == true)
            {
                LyricsListView.SelectAll();
            }
            else if (LyricsHostCheckBox.IsChecked == false)
            {
                LyricsListView.SelectedItems.Clear();
            }
        }

        private void PreviousPage_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.PreviousPage();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NextPage();
        }

        private void PageNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_exportCancellation != null || double.IsNaN(args.NewValue)) return;

            if (!TryGetPageNumber(args.NewValue, out var pageNumber) ||
                !ViewModel.GoToPage(pageNumber - 1))
            {
                sender.Value = ViewModel.CurrentPageNumber;
            }
        }

        private void CancelExport_Click(object sender, RoutedEventArgs e)
        {
            if (_exportCancellation == null) return;

            CancelExportButton.IsEnabled = false;
            ProcessingStatusText.Text = Localize("LyricsSharePageCancelingExport");
            _exportCancellation.Cancel();
        }

        private void LyricsSharePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _exportCancellation?.Cancel();
        }

        private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
        {
            public void Report(T value) => report(value);
        }
    }
}
