using BetterLyrics.Core.Interfaces.Providers;
using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.WinUI3.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BetterLyrics.WinUI3.Providers;

/// <summary>
/// Singleton Provider
/// </summary>
public class LastFmDialogProvider : ILastFmDialogProvider
{
    private readonly IWindowManagerProvider _windowManagerProvider;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<LastFmDialogProvider> _logger;

    public LastFmDialogProvider(
        IWindowManagerProvider windowManagerProvider,
        ILocalizationService localizationService,
        ILogger<LastFmDialogProvider> logger)
    {
        _windowManagerProvider = windowManagerProvider;
        _localizationService = localizationService;
        _logger = logger;
    }

    public async Task ShowAuthDialogAsync(Func<Task> onConfirm)
    {
        var dialogXamlRoot = _windowManagerProvider.GetWindow<SettingsWindow>()?.Content.XamlRoot ??
                             _windowManagerProvider.GetWindows<NowPlayingWindow>().FirstOrDefault()?.Content.XamlRoot;
        if (dialogXamlRoot != null)
        {
            var dialog = new ContentDialog
            {
                Title = _localizationService.GetLocalizedString("LastFMRequestAuthTitle") ?? "",
                Content = _localizationService.GetLocalizedString("LastFMRequestAuthDesc") ?? "",
                PrimaryButtonText = _localizationService.GetLocalizedString("LastFMRequestAuthConfirm") ?? "",
                CloseButtonText = _localizationService.GetLocalizedString("Cancel") ?? "",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = dialogXamlRoot
            };
            dialog.PrimaryButtonClick += async (s, args) => await HandlePrimaryButtonClickAsync(args, onConfirm,
                "authentication");
            await dialog.ShowAsync();
        }
    }

    public async Task ShowUnAuthDialogAsync(Func<Task> onConfirm)
    {
        var dialogXamlRoot = _windowManagerProvider.GetWindow<SettingsWindow>()?.Content.XamlRoot ??
                             _windowManagerProvider.GetWindows<NowPlayingWindow>().FirstOrDefault()?.Content.XamlRoot;
        if (dialogXamlRoot == null) return;

        var dialog = new ContentDialog
        {
            Title = _localizationService.GetLocalizedString("LastFMRequestUnAuthTitle") ?? "",
            Content = _localizationService.GetLocalizedString("LastFMRequestUnAuthDesc") ?? "",
            PrimaryButtonText = _localizationService.GetLocalizedString("LastFMRequestUnAuthConfirm") ?? "",
            CloseButtonText = _localizationService.GetLocalizedString("Cancel") ?? "",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = dialogXamlRoot
        };

        dialog.PrimaryButtonClick += async (s, args) => await HandlePrimaryButtonClickAsync(args, onConfirm,
            "unauthentication");
        await dialog.ShowAsync();
    }

    private async Task HandlePrimaryButtonClickAsync(ContentDialogButtonClickEventArgs args, Func<Task> onConfirm,
        string operation)
    {
        // 点击事件默认同步收尾；使用 deferral 避免异步确认期间对话框提前关闭。
        var deferral = args.GetDeferral();
        try
        {
            await onConfirm();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            _logger.LogError(ex, "Last.fm {Operation} dialog callback failed.", operation);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
