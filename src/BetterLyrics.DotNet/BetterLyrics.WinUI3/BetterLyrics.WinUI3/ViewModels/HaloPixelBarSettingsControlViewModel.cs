using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Interfaces.ViewModels;
using BetterLyrics.Core.Models.Settings;
using BetterLyrics.WinUI3.Services.HaloPixelBar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;

namespace BetterLyrics.WinUI3.ViewModels;

public partial class HaloPixelBarSettingsControlViewModel : ObservableObject, ITransientViewModel, IDisposable
{
    private readonly HaloPixelBarService _haloPixelBarService;
    private DispatcherQueueTimer? _statusTimer;

    public AppSettings AppSettings { get; }
    [ObservableProperty] public partial string DeviceStatusText { get; set; } = "未连接";
    private HaloPixelBarSettings Settings => AppSettings.HaloPixelBarSettings;

    public HaloPixelBarSettingsControlViewModel(ISettingsService settingsService,
        HaloPixelBarService haloPixelBarService)
    {
        AppSettings = settingsService.AppSettings;
        _haloPixelBarService = haloPixelBarService;
    }

    public void StartStatusPolling()
    {
        if (_statusTimer != null) return;
        _statusTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(2);
        _statusTimer.Tick += OnStatusTick;
        _statusTimer.Start();
        RefreshStatus();
    }

    private void OnStatusTick(DispatcherQueueTimer sender, object args) => RefreshStatus();

    private void RefreshStatus() =>
        DeviceStatusText = _haloPixelBarService.IsDeviceConnected ? "已连接" : "未连接";

    public void StopStatusPolling()
    {
        if (_statusTimer == null) return;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTick;
        _statusTimer = null;
    }

    [RelayCommand]
    private void AdjustScrollThreshold(string delta)
    {
        if (int.TryParse(delta, out var amount))
            Settings.ScrollThreshold = Math.Clamp(Settings.ScrollThreshold + amount, 4, 100);
    }

    [RelayCommand]
    private void AdjustTypewriterSpeed(string delta)
    {
        if (int.TryParse(delta, out var amount))
            Settings.TypewriterSpeed = Math.Clamp(Settings.TypewriterSpeed + amount, 30, 500);
    }

    [RelayCommand]
    private void AdjustLatencyComp(string delta)
    {
        if (int.TryParse(delta, out var amount))
            Settings.LatencyCompMs = Math.Clamp(Settings.LatencyCompMs + amount, -200, 1000);
    }

    [RelayCommand]
    private void AdjustAlternateInterval(string delta)
    {
        if (int.TryParse(delta, out var amount))
            Settings.AlternateInterval = Math.Clamp(Settings.AlternateInterval + amount, 1000, 10000);
    }

    public void Dispose() => StopStatusPolling();
}
