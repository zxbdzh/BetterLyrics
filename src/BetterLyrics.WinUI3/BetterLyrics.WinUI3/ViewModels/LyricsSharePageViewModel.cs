using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Extensions;
using BetterLyrics.Core.Helpers.Lyrics;
using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Models;
using BetterLyrics.Core.Models.Domain;
using BetterLyrics.Core.Models.Entities;
using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Settings;
using BetterLyrics.Core.ViewModels;
using BetterLyrics.WinUI3.Models;
using BetterLyrics.WinUI3.Services.GSMTCService;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterLyrics.WinUI3.ViewModels
{
    public sealed record LyricsShareExportSnapshot(
        int OriginalPageIndex,
        IReadOnlyList<IReadOnlyList<LyricsLine>> Pages,
        string Title,
        string Artist,
        ImageSource? CoverImage,
        AppColor? AccentCoverColor,
        string StyleKey,
        string FontFamily)
    {
        public int PageCount => Pages.Count;
    }

    public partial class LyricsSharePageViewModel : BaseViewModel,
        IRecipient<PropertyChangedMessage<BitmapImage?>>,
        IRecipient<PropertyChangedMessage<SongInfo>>,
        IRecipient<PropertyChangedMessage<LyricsData?>>,
        IRecipient<PropertyChangedMessage<MappedSongSearchQuery?>>
    {
        private readonly ILocalizationService _localizationService;
        private readonly string _pageCountFormat;
        private readonly string _pageIndicatorFormat;
        private readonly string _pageJumpAutomationFormat;
        private readonly ISongSearchMapService _songSearchMapService;
        private readonly ISettingsService _settingsService;
        private bool _isExportActive;
        private bool _isPaginationDirty;
        private IReadOnlyList<IReadOnlyList<LyricsLine>> _lyricsPages = [];
        private List<LyricsLine> _selectedLyrics = [];
        private CardMetadata? _pendingCardMetadata;
        private List<LyricsLine>? _pendingSelectedLyrics;
        private long _metadataRefreshGeneration;

        public IGSMTCService GSMTCService { get; private set; }

        [ObservableProperty] public partial AppSettings AppSettings { get; set; }
        [ObservableProperty] public partial LyricsCardData CardData { get; set; } = new();
        [ObservableProperty] public partial LyricsCardConfig CardConfig { get; set; } = new();
        [ObservableProperty] public partial ObservableCollection<LyricsCardStyleGroup> StyleGroups { get; set; }
        [ObservableProperty] public partial LyricsCardStyleItem SelectedStyleItem { get; set; }
        [ObservableProperty] public partial int SelectedStyleDisplayTypeIndex { get; set; } = 1;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
        [NotifyPropertyChangedFor(nameof(HasNextPage))]
        [NotifyPropertyChangedFor(nameof(PageIndicator))]
        [NotifyPropertyChangedFor(nameof(CurrentPageNumber))]
        [NotifyPropertyChangedFor(nameof(PageJumpAutomationText))]
        public partial int CurrentPageIndex { get; set; }
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
        [NotifyPropertyChangedFor(nameof(HasNextPage))]
        [NotifyPropertyChangedFor(nameof(IsPaginationVisible))]
        [NotifyPropertyChangedFor(nameof(PageIndicator))]
        [NotifyPropertyChangedFor(nameof(CurrentPageNumber))]
        [NotifyPropertyChangedFor(nameof(MaximumPageNumber))]
        [NotifyPropertyChangedFor(nameof(PageCountIndicator))]
        [NotifyPropertyChangedFor(nameof(PageJumpAutomationText))]
        public partial int PageCount { get; set; }

        public bool HasPreviousPage => CurrentPageIndex > 0;
        public bool HasNextPage => CurrentPageIndex + 1 < PageCount;
        public bool IsPaginationVisible => PageCount > 1;
        public double CurrentPageNumber => PageCount == 0 ? 1 : CurrentPageIndex + 1;
        public double MaximumPageNumber => Math.Max(PageCount, 1);
        public string PageIndicator => string.Format(
            CultureInfo.CurrentCulture,
            _pageIndicatorFormat,
            PageCount == 0 ? 0 : CurrentPageIndex + 1,
            PageCount);
        public string PageCountIndicator => string.Format(CultureInfo.CurrentCulture, _pageCountFormat, PageCount);
        public string PageJumpAutomationText => string.Format(
            CultureInfo.CurrentCulture,
            _pageJumpAutomationFormat,
            PageCount == 0 ? 0 : CurrentPageIndex + 1,
            PageCount);
        public string PreviousPageText { get; }
        public string NextPageText { get; }
        public IReadOnlyList<LyricsLine> ShareableLyricsLines =>
            GSMTCService.CurrentLyricsData?.LyricsLines
                .Where(line => line.IsShareable)
                .ToList() ?? [];

        public LyricsSharePageViewModel(IGSMTCService gsmtcService, ISongSearchMapService songSearchMapService, ISettingsService settingsService, ILocalizationService localizationService)
        {
            _songSearchMapService = songSearchMapService;
            _settingsService = settingsService;
            _localizationService = localizationService;

            _pageCountFormat = localizationService.GetLocalizedString("LyricsSharePagePageCountFormat");
            _pageIndicatorFormat = localizationService.GetLocalizedString("LyricsSharePagePageIndicatorFormat");
            _pageJumpAutomationFormat = localizationService.GetLocalizedString("LyricsSharePagePageJumpAutomationFormat");
            PreviousPageText = localizationService.GetLocalizedString("LyricsSharePagePreviousPage");
            NextPageText = localizationService.GetLocalizedString("LyricsSharePageNextPage");

            AppSettings = settingsService.AppSettings;
            GSMTCService = gsmtcService;

            _ = RefreshCardDataAsync();
            ActivateCardDataForBinding();
            LoadStyleData();
        }

        private void LoadStyleData()
        {
            // 经典设计
            var classicGroup = new LyricsCardStyleGroup(_localizationService.GetLocalizedString("LyricsSharePageGroupClassic"), new[]
            {
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleMinimal"), StyleKey = "LyricsCardMinimalStyle", IsChecked = true },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleSwiss"), StyleKey = "LyricsCardSwissStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleMagazine"), StyleKey = "LyricsCardMagazineStyle" }
            });

            // 实体质感
            var physicalGroup = new LyricsCardStyleGroup(_localizationService.GetLocalizedString("LyricsSharePageGroupPhysical"), new[]
            {
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleVinyl"), StyleKey = "LyricsCardVinylStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleCD"), StyleKey = "LyricsCardCDStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStylePolaroid"), StyleKey = "LyricsCardPolaroidStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleTicket"), StyleKey = "LyricsCardTicketStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleReceipt"), StyleKey = "LyricsCardReceiptStyle" }
            });

            // 时光印记
            var tracesOfTimeGroup = new LyricsCardStyleGroup(_localizationService.GetLocalizedString("LyricsSharePageGroupTracesOfTime"), new[]
            {
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleJournal"), StyleKey = "LyricsCardJournalStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleStickyNote"), StyleKey = "LyricsCardStickyNoteStyle" }
            });

            // 数码怀旧
            var retroGroup = new LyricsCardStyleGroup(_localizationService.GetLocalizedString("LyricsSharePageGroupRetro"), new[]
            {
                new LyricsCardStyleItem { DisplayText = "iPod", StyleKey = "LyricsCardPodStyle" },
                new LyricsCardStyleItem { DisplayText = "Windows Phone", StyleKey = "LyricsCardWindowsPhoneStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleRetroQQ"), StyleKey = "LyricsCardRetroQQStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleRetroMSN"), StyleKey = "LyricsCardRetroMSNStyle" }
            });

            // 现代视窗
            var modernGroup = new LyricsCardStyleGroup(_localizationService.GetLocalizedString("LyricsSharePageGroupModernVision"), new[]
            {
                new LyricsCardStyleItem { DisplayText = "QQ", StyleKey = "LyricsCardQQStyle" },
                new LyricsCardStyleItem { DisplayText = "微信", StyleKey = "LyricsCardWeChatStyle" },
                new LyricsCardStyleItem { DisplayText = "WhatsApp", StyleKey = "LyricsCardWhatsAppStyle" },
                new LyricsCardStyleItem { DisplayText = "Telegram", StyleKey = "LyricsCardTelegramStyle" },
                new LyricsCardStyleItem { DisplayText = "LINE", StyleKey = "LyricsCardLINEStyle" },
            });

            // 氛围创意
            var atmosphereGroup = new LyricsCardStyleGroup(_localizationService.GetLocalizedString("LyricsSharePageGroupAtmosphere"), new[]
            {
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleCinematic"), StyleKey = "LyricsCardCinematicStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleCyberpunk"), StyleKey = "LyricsCardCyberpunkStyle" }
            });

            // 国风雅韵
            var chineseEleganceGroup = new LyricsCardStyleGroup(_localizationService.GetLocalizedString("LyricsSharePageGroupChineseElegance"), new[]
            {
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleInkWash"), StyleKey = "LyricsCardInkWashStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleAncientBook"), StyleKey = "LyricsCardAncientBookStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleBambooSlips"), StyleKey = "LyricsCardBambooSlipsStyle" },
                new LyricsCardStyleItem { DisplayText = _localizationService.GetLocalizedString("LyricsSharePageStyleDunhuang"), StyleKey = "LyricsCardDunhuangStyle" }
            });

            StyleGroups = new ObservableCollection<LyricsCardStyleGroup>
            {
                classicGroup,
                physicalGroup,
                tracesOfTimeGroup,
                retroGroup,
                modernGroup,
                atmosphereGroup,
                chineseEleganceGroup,
            };

            SwitchStyle(classicGroup[0]);
        }

        public void SwitchStyle(LyricsCardStyleItem styleItem)
        {
            foreach (var styleGroup in StyleGroups)
            {
                foreach (var item in styleGroup)
                {
                    item.IsChecked = false;
                }
            }
            SelectedStyleItem = styleItem;
        }

        public void UpdateSelectedLyrics(List<LyricsLine> lyrics)
        {
            var selectedLyrics = CloneLyrics(lyrics);
            _selectedLyrics = selectedLyrics;

            if (_isExportActive)
            {
                _pendingSelectedLyrics = selectedLyrics;
                return;
            }

            ApplySelectedLyrics(selectedLyrics, resetCurrentPage: true);
        }

        public void PreviousPage()
        {
            _ = GoToPage(CurrentPageIndex - 1);
        }

        public void NextPage()
        {
            _ = GoToPage(CurrentPageIndex + 1);
        }

        public bool GoToPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= PageCount) return false;

            CurrentPageIndex = pageIndex;
            UpdateCurrentPage();
            return true;
        }

        public LyricsShareExportSnapshot BeginExport()
        {
            if (_isExportActive)
            {
                throw new InvalidOperationException("An export is already active.");
            }

            var snapshot = new LyricsShareExportSnapshot(
                CurrentPageIndex,
                _lyricsPages
                    .Select(page => (IReadOnlyList<LyricsLine>)CloneLyrics(page))
                    .ToList(),
                CardData.Title,
                CardData.Artist,
                CardData.CoverImage,
                CardData.AccentCoverColor,
                SelectedStyleItem.StyleKey,
                CardConfig.FontFamily);

            _pendingCardMetadata = null;
            _pendingSelectedLyrics = null;
            _isPaginationDirty = false;
            _isExportActive = true;
            _ = ShowExportPage(snapshot, snapshot.OriginalPageIndex);
            return snapshot;
        }

        public bool ShowExportPage(LyricsShareExportSnapshot snapshot, int pageIndex)
        {
            if (!_isExportActive || pageIndex < 0 || pageIndex >= snapshot.PageCount)
            {
                return false;
            }

            PageCount = snapshot.PageCount;
            CurrentPageIndex = pageIndex;
            CardData.Title = snapshot.Title;
            CardData.Artist = snapshot.Artist;
            CardData.CoverImage = snapshot.CoverImage;
            CardData.AccentCoverColor = snapshot.AccentCoverColor;
            CardData.Lyrics = snapshot.Pages[pageIndex].ToList();
            return true;
        }

        public void EndExport(LyricsShareExportSnapshot snapshot)
        {
            if (!_isExportActive) return;

            _isExportActive = false;
            var pendingLyrics = _pendingSelectedLyrics;
            var pendingMetadata = _pendingCardMetadata;
            _pendingSelectedLyrics = null;
            _pendingCardMetadata = null;

            if (pendingLyrics != null)
            {
                ApplySelectedLyrics(pendingLyrics, resetCurrentPage: true);
            }
            else if (_isPaginationDirty)
            {
                CurrentPageIndex = snapshot.OriginalPageIndex;
                ApplySelectedLyrics(_selectedLyrics, resetCurrentPage: false);
            }
            else
            {
                PageCount = _lyricsPages.Count;
                CurrentPageIndex = PageCount == 0
                    ? 0
                    : Math.Clamp(snapshot.OriginalPageIndex, 0, PageCount - 1);
                UpdateCurrentPage();
            }

            _isPaginationDirty = false;

            ApplyCardMetadata(pendingMetadata ?? new CardMetadata(
                snapshot.Title,
                snapshot.Artist,
                snapshot.CoverImage,
                snapshot.AccentCoverColor));
        }

        private void ApplySelectedLyrics(
            IReadOnlyList<LyricsLine> lyrics,
            bool resetCurrentPage)
        {
            var displayedCharacterOffset = resetCurrentPage
                ? 0
                : GetCurrentPageDisplayedCharacterOffset();
            var limits = LyricsSharePaginator.GetLimitsForStyle(SelectedStyleItem?.StyleKey);
            _lyricsPages = LyricsSharePaginator.Paginate(
                lyrics,
                maxLinesPerPage: limits.MaxLinesPerPage,
                maxCharactersPerPage: limits.MaxCharactersPerPage,
                maxCharactersPerLine: limits.MaxCharactersPerLine);
            PageCount = _lyricsPages.Count;
            CurrentPageIndex = PageCount == 0
                ? 0
                : resetCurrentPage
                    ? 0
                    : FindPageAtDisplayedCharacterOffset(displayedCharacterOffset);
            UpdateCurrentPage();
        }

        private long GetCurrentPageDisplayedCharacterOffset()
        {
            long offset = 0;
            var pageIndex = Math.Clamp(CurrentPageIndex, 0, _lyricsPages.Count);
            for (var index = 0; index < pageIndex; index++)
            {
                offset += _lyricsPages[index].Sum(LyricsSharePaginator.GetDisplayedCharacterCount);
            }
            return offset;
        }

        private int FindPageAtDisplayedCharacterOffset(long targetOffset)
        {
            long offset = 0;
            for (var pageIndex = 0; pageIndex < _lyricsPages.Count; pageIndex++)
            {
                offset += _lyricsPages[pageIndex]
                    .Sum(LyricsSharePaginator.GetDisplayedCharacterCount);
                if (targetOffset < offset) return pageIndex;
            }
            return Math.Max(0, _lyricsPages.Count - 1);
        }

        private void UpdateCurrentPage()
        {
            CardData.Lyrics = PageCount == 0
                ? []
                : _lyricsPages[CurrentPageIndex].ToList();
        }

        private async Task RefreshCardDataAsync()
        {
            var generation = Interlocked.Increment(ref _metadataRefreshGeneration);
            var songInfo = (SongInfo)GSMTCService.CurrentSongInfo.Clone();
            var coverImage = GSMTCService.AlbumArtBitmapImage;
            var mappingTask = _songSearchMapService.GetMappingAsync(songInfo);
            var accentColorsTask = GSMTCService.GetAlbumArtAccentColorsAsync(
                PaletteGeneratorType.Auto,
                true);
            var (mappedTitle, mappedArtist, _) = await mappingTask;
            var accentCoverColor = (await accentColorsTask).First();

            if (generation != Volatile.Read(ref _metadataRefreshGeneration)) return;

            var metadata = new CardMetadata(mappedTitle, mappedArtist, coverImage, accentCoverColor);

            if (_isExportActive)
            {
                _pendingCardMetadata = metadata;
                return;
            }

            ApplyCardMetadata(metadata);
        }

        private void ApplyCardMetadata(CardMetadata metadata)
        {
            CardData.Title = metadata.Title;
            CardData.Artist = metadata.Artist;
            CardData.CoverImage = metadata.CoverImage;
            CardData.AccentCoverColor = metadata.AccentCoverColor;
            UpdateCurrentPage();
        }

        private static List<LyricsLine> CloneLyrics(IEnumerable<LyricsLine> lyrics) =>
            lyrics.Select(line => new LyricsLine
            {
                StartMs = line.StartMs,
                EndMs = line.EndMs,
                AgentId = line.AgentId,
                IsPrimaryHasRealSyllableInfo = line.IsPrimaryHasRealSyllableInfo,
                IsShareable = line.IsShareable,
                PrimaryText = line.PrimaryText,
                SecondaryText = line.SecondaryText,
                TertiaryText = line.TertiaryText,
            }).ToList();

        private void ActivateCardDataForBinding()
        {
            _ = CardData.Title;
            _ = CardData.Artist;
            _ = CardData.CoverImage;
            _ = CardData.AccentCoverColor;
            _ = CardData.Lyrics;

            _ = CardConfig;
            _ = CardConfig.FontFamily;
        }

        partial void OnSelectedStyleItemChanged(LyricsCardStyleItem value)
        {
            SwitchStyle(value);

            var found = _settingsService.AppSettings.LyricsCardConfigs.FirstOrDefault(c => c.ResourceKey == value.StyleKey);
            if (found == null)
            {
                found = LyricsCardConfigExtensions.GetDefaultLyricsCardConfig(value.StyleKey);
                _settingsService.AppSettings.LyricsCardConfigs.Add(found);
            }
            CardConfig = found;

            if (_isExportActive)
            {
                _isPaginationDirty = true;
                return;
            }

            ApplySelectedLyrics(_selectedLyrics, resetCurrentPage: false);
        }

        partial void OnSelectedStyleDisplayTypeIndexChanged(int value)
        {
            foreach (var group in StyleGroups)
            {
                foreach (var style in group)
                {
                    style.IsExpanded = value != 0;
                }
            }
        }

        public void Receive(PropertyChangedMessage<BitmapImage?> message)
        {
            if (message.Sender is IGSMTCService)
            {
                if (message.PropertyName == nameof(IGSMTCService.AlbumArtBitmapImage))
                {
                    _ = RefreshCardDataAsync();
                }
            }
        }

        public void Receive(PropertyChangedMessage<SongInfo> message)
        {
            if (message.Sender is IGSMTCService &&
                message.PropertyName == nameof(IGSMTCService.CurrentSongInfo))
            {
                _ = RefreshCardDataAsync();
            }
        }

        public void Receive(PropertyChangedMessage<LyricsData?> message)
        {
            if (message.Sender is IGSMTCService &&
                message.PropertyName == nameof(IGSMTCService.CurrentLyricsData))
            {
                OnPropertyChanged(nameof(ShareableLyricsLines));
            }
        }

        public void Receive(PropertyChangedMessage<MappedSongSearchQuery?> message)
        {
            if (message.Sender is LyricsSearchControlViewModel)
            {
                if (message.PropertyName == nameof(LyricsSearchControlViewModel.MappedSongSearchQuery))
                {
                    _ = RefreshCardDataAsync();
                }
            }
        }

        private sealed record CardMetadata(
            string Title,
            string Artist,
            ImageSource? CoverImage,
            AppColor? AccentCoverColor);
    }
}
