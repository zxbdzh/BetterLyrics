using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Models.Settings;
using BetterLyrics.Core.ViewModels;
using BetterLyrics.WinUI3.Services.HaloPixelBar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;
using System.ComponentModel;
using System.Threading;

namespace BetterLyrics.WinUI3.ViewModels
{
    public partial class HaloPixelBarSettingsControlViewModel : BaseViewModel
    {
        private readonly ISettingsService _settingsService;
        private readonly HaloPixelBarService _haloPixelBarService;
        private readonly DispatcherQueue _dispatcherQueue;
        private Timer? _statusTimer;

        [ObservableProperty] public partial AppSettings AppSettings { get; set; }
        [ObservableProperty] public partial bool IsDeviceConnected { get; set; }
        [ObservableProperty] public partial string DeviceStatusText { get; set; } = "未连接";

        private HaloPixelBarSettings Settings => AppSettings.HaloPixelBarSettings;

        public HaloPixelBarSettingsControlViewModel(
            ISettingsService settingsService,
            HaloPixelBarService haloPixelBarService)
        {
            _settingsService = settingsService;
            _haloPixelBarService = haloPixelBarService;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            AppSettings = _settingsService.AppSettings;

            Settings.PropertyChanged += OnHaloSettingsChanged;
        }

        public void StartStatusPolling()
        {
            _statusTimer = new Timer(_ =>
            {
                var connected = _haloPixelBarService.IsDeviceConnected;
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsDeviceConnected = connected;
                    DeviceStatusText = connected ? "已连接" : "未连接";
                });
            }, null, 0, 2000);
        }

        public void StopStatusPolling()
        {
            _statusTimer?.Dispose();
            _statusTimer = null;
        }

        private void OnHaloSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HaloPixelBarSettings.AutoScroll) && Settings.AutoScroll)
                Settings.AlternateSplit = false;
            else if (e.PropertyName == nameof(HaloPixelBarSettings.AlternateSplit) && Settings.AlternateSplit)
                Settings.AutoScroll = false;

            _haloPixelBarService.OnSettingsChanged();
        }

        [RelayCommand]
        private void AdjustScrollThreshold(string delta)
        {
            if (int.TryParse(delta, out int d))
                Settings.ScrollThreshold = Math.Clamp(Settings.ScrollThreshold + d, 4, 100);
        }

        [RelayCommand]
        private void AdjustTypewriterSpeed(string delta)
        {
            if (int.TryParse(delta, out int d))
                Settings.TypewriterSpeed = Math.Clamp(Settings.TypewriterSpeed + d, 30, 500);
        }

        [RelayCommand]
        private void AdjustLatencyComp(string delta)
        {
            if (int.TryParse(delta, out int d))
                Settings.LatencyCompMs = Math.Clamp(Settings.LatencyCompMs + d, -200, 1000);
        }

        [RelayCommand]
        private void AdjustAlternateInterval(string delta)
        {
            if (int.TryParse(delta, out int d))
                Settings.AlternateInterval = Math.Clamp(Settings.AlternateInterval + d, 1000, 10000);
        }
    }
}
