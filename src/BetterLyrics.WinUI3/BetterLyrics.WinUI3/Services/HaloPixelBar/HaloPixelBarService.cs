using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Models;
using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Settings;
using BetterLyrics.Core.ViewModels;
using BetterLyrics.WinUI3.Services.GSMTCService;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public class HaloPixelBarService : BaseViewModel,
    IRecipient<PropertyChangedMessage<TimeSpan>>,
    IRecipient<PropertyChangedMessage<LyricsData?>>,
    IRecipient<PropertyChangedMessage<SongInfo>>,
    IRecipient<PropertyChangedMessage<bool>>
{
    private readonly IGSMTCService _gsmtcService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<HaloPixelBarService> _logger;

    private HaloPixelBarDevice? _device;
    private DisplayEngine? _engine;
    private Timer? _reconnectTimer;
    private Timer? _syncTimer;

    private HaloPixelBarSettings Settings => _settingsService.AppSettings.HaloPixelBarSettings;

    private List<LyricsLine>? _lyricsLines;
    private int _currentLineIndex = -1;
    private bool _isPlaying;
    private bool _started;
    private SongInfo? _currentSong;

    private long _lastTickCount;
    private double _localPositionMs;
    private double _calibrationThresholdMs = 500;

    private const int ReconnectIntervalMs = 3000;
    private const int SyncPollingMs = 100;

    public HaloPixelBarService(
        IGSMTCService gsmtcService,
        ISettingsService settingsService,
        ILogger<HaloPixelBarService> logger)
    {
        _gsmtcService = gsmtcService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsDeviceConnected => _device?.IsConnected ?? false;

    public void Start()
    {
        if (_started) return;
        _started = true;

        if (!Settings.Enable) return;

        StartDevice();
        _logger.LogInformation("HaloPixelBarService started");
    }

    public void OnSettingsChanged()
    {
        if (!_started) return;

        if (Settings.Enable)
        {
            if (_device == null) StartDevice();
            _engine?.UpdateSettings(Settings);
            _engine?.Reset();
            ResendCurrentLine();
        }
        else
        {
            StopDevice();
        }
    }

    private void StartDevice()
    {
        _device = new HaloPixelBarDevice();
        _engine = new DisplayEngine(_device, Settings);
        _device.Open();

        _reconnectTimer = new Timer(_ =>
        {
            if (_device is { IsConnected: false })
                _device.Open();
        }, null, ReconnectIntervalMs, ReconnectIntervalMs);

        _localPositionMs = _gsmtcService.CurrentPosition.TotalMilliseconds;
        _lastTickCount = Environment.TickCount64;
        _isPlaying = _gsmtcService.CurrentIsPlaying;

        StartSyncTimer();
    }

    private void StartSyncTimer()
    {
        _syncTimer?.Dispose();
        _syncTimer = new Timer(_ => SyncTick(), null, SyncPollingMs, SyncPollingMs);
    }

    private void SyncTick()
    {
        if (!_isPlaying || _lyricsLines == null || _lyricsLines.Count == 0) return;

        long now = Environment.TickCount64;
        long elapsed = now - _lastTickCount;
        _lastTickCount = now;
        _localPositionMs += elapsed;

        int newIndex = FindCurrentLineIndex(_localPositionMs);
        if (newIndex == _currentLineIndex) return;

        _currentLineIndex = newIndex;
        SendLineAtIndex(newIndex, _localPositionMs);
    }

    private void StopDevice()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;

        _reconnectTimer?.Dispose();
        _reconnectTimer = null;

        _engine?.ShowClock();
        _engine?.Dispose();
        _device?.Dispose();
        _engine = null;
        _device = null;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;

        StopDevice();
        _logger.LogInformation("HaloPixelBarService stopped");
    }

    public void Receive(PropertyChangedMessage<SongInfo> message)
    {
        if (message.Sender is not IGSMTCService ||
            message.PropertyName != nameof(IGSMTCService.CurrentSongInfo)) return;
        if (!Settings.Enable) return;

        _currentLineIndex = -1;
        _currentSong = message.NewValue;

        string display = _currentSong.Title;
        if (!string.IsNullOrWhiteSpace(_currentSong.Artist))
            display = $"{_currentSong.Title} - {_currentSong.Artist}";

        SafeExecute(() => _engine?.ShowText(display));
    }

    public void Receive(PropertyChangedMessage<LyricsData?> message)
    {
        if (message.Sender is not IGSMTCService ||
            message.PropertyName != nameof(IGSMTCService.CurrentLyricsData)) return;

        var lyricsData = message.NewValue;

        if (lyricsData?.LanguageCode == "N/A") return;

        _currentLineIndex = -1;

        if (lyricsData?.LyricsLines is { Count: > 1 } lines)
        {
            _lyricsLines = lines;
        }
        else
        {
            _lyricsLines = null;
            if (Settings.Enable)
                SafeExecute(() => _engine?.ShowClock());
        }
    }

    public void Receive(PropertyChangedMessage<bool> message)
    {
        if (message.Sender is not IGSMTCService ||
            message.PropertyName != nameof(IGSMTCService.CurrentIsPlaying)) return;
        if (!Settings.Enable) return;

        _isPlaying = message.NewValue;
        if (_isPlaying)
        {
            _lastTickCount = Environment.TickCount64;
        }
        else
        {
            SafeExecute(() => _engine?.ShowClock());
        }
    }

    public void Receive(PropertyChangedMessage<TimeSpan> message)
    {
        if (message.Sender is not IGSMTCService ||
            message.PropertyName != nameof(IGSMTCService.CurrentPosition)) return;
        if (!Settings.Enable) return;

        double remoteMs = message.NewValue.TotalMilliseconds;
        double drift = Math.Abs(_localPositionMs - remoteMs);

        if (drift >= _calibrationThresholdMs)
        {
            _localPositionMs = remoteMs;
            _lastTickCount = Environment.TickCount64;

            if (_isPlaying)
            {
                int newIndex = FindCurrentLineIndex(_localPositionMs);
                if (newIndex != _currentLineIndex)
                {
                    _currentLineIndex = newIndex;
                    SendLineAtIndex(newIndex, _localPositionMs);
                }
            }
        }
    }

    private void SendLineAtIndex(int index, double posMs)
    {
        if (_lyricsLines == null) return;

        if (index < 0 || index >= _lyricsLines.Count)
        {
            SafeExecute(() => _engine?.ShowClock());
            return;
        }

        var line = _lyricsLines[index];
        string text = ResolveText(line);

        double lineDurationMs = 0;
        if (index + 1 < _lyricsLines.Count)
            lineDurationMs = _lyricsLines[index + 1].StartMs - line.StartMs;
        else if (_currentSong != null)
            lineDurationMs = Math.Max(_currentSong.DurationMs - line.StartMs, 5000);
        else
            lineDurationMs = 5000;

        List<CharTiming>? timings = null;
        if (Settings.LyricMode == 0 && line.IsPrimaryHasRealSyllableInfo && line.PrimaryChars.Count > 0)
        {
            timings = line.PrimaryChars
                .Select(c => new CharTiming(c.Text, c.StartMs, c.DurationMs))
                .ToList();
        }

        SafeExecute(() => _engine?.SendLyric(text, posMs, lineDurationMs, timings));
    }

    private string ResolveText(LyricsLine line)
    {
        return Settings.LyricMode switch
        {
            1 when !string.IsNullOrWhiteSpace(line.SecondaryText) => line.SecondaryText,
            2 when !string.IsNullOrWhiteSpace(line.TertiaryText) => line.TertiaryText,
            _ => line.PrimaryText
        };
    }

    private void ResendCurrentLine()
    {
        if (!_isPlaying || _lyricsLines == null) return;
        int idx = FindCurrentLineIndex(_localPositionMs);
        if (idx >= 0)
            SendLineAtIndex(idx, _localPositionMs);
    }

    private int FindCurrentLineIndex(double positionMs)
    {
        if (_lyricsLines == null || _lyricsLines.Count == 0) return -1;

        for (int i = _lyricsLines.Count - 1; i >= 0; i--)
        {
            if (positionMs >= _lyricsLines[i].StartMs)
                return i;
        }
        return -1;
    }

    private void SafeExecute(Action action)
    {
        _ = Task.Run(() =>
        {
            try { action(); }
            catch (Exception ex) { _logger.LogError(ex, "HaloPixelBar error"); }
        });
    }
}
