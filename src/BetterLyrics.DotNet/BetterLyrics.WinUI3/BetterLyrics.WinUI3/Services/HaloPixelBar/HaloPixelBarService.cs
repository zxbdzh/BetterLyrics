using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Messages.Playback;
using BetterLyrics.Core.Models;
using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public sealed class HaloPixelBarService : ObservableRecipient, IDisposable,
    IRecipient<CurrentPositionChangedMessage>,
    IRecipient<CurrentLyricsDataChangedMessage>,
    IRecipient<CurrentSongInfoChangedMessage>,
    IRecipient<CurrentIsPlayingChangedMessage>
{
    private readonly object _sync = new();
    private readonly IGsmtcService _gsmtcService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<HaloPixelBarService> _logger;
    private HaloPixelBarDevice? _device;
    private DisplayEngine? _engine;
    private Timer? _syncTimer;
    private Timer? _reconnectTimer;
    private List<LyricsLine>? _lyricsLines;
    private SongInfo? _currentSong;
    private int _currentLineIndex = -1;
    private double _localPositionMs;
    private long _lastTickCount;
    private bool _isPlaying;
    private bool _started;
    private bool _updatingSettings;

    private HaloPixelBarSettings Settings => _settingsService.AppSettings.HaloPixelBarSettings;

    public HaloPixelBarService(IGsmtcService gsmtcService, ISettingsService settingsService,
        ILogger<HaloPixelBarService> logger)
    {
        _gsmtcService = gsmtcService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsDeviceConnected
    {
        get { lock (_sync) return _device?.IsConnected ?? false; }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_started) return;
            _started = true;
            // 默认通道跟随当前播放源，不能同时接收内置播放器的独立通道。
            IsActive = true;
            Settings.PropertyChanged += OnSettingsChanged;
            ApplySettings(null);
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        lock (_sync)
        {
            if (!_started || _updatingSettings) return;
            ApplySettings(args.PropertyName);
        }
    }

    private void ApplySettings(string? propertyName)
    {
        _updatingSettings = true;
        try
        {
            if (Settings.AutoScroll && Settings.AlternateSplit)
            {
                if (propertyName == nameof(HaloPixelBarSettings.AutoScroll))
                    Settings.AlternateSplit = false;
                else
                    Settings.AutoScroll = false;
            }

            if (!Settings.Enable)
            {
                StopDevice();
                return;
            }
            if (_device == null)
            {
                StartDevice();
                return;
            }
            _engine?.UpdateSettings(Settings);
            _engine?.Reset();
            SendCurrentLine(true);
        }
        finally
        {
            _updatingSettings = false;
        }
    }

    private void StartDevice()
    {
        _device = new HaloPixelBarDevice();
        _engine = new DisplayEngine(_device, Settings);
        _device.Open();
        _localPositionMs = _gsmtcService.CurrentPosition.TotalMilliseconds;
        _lastTickCount = Environment.TickCount64;
        _isPlaying = _gsmtcService.CurrentIsPlaying;
        _currentSong = _gsmtcService.CurrentSongInfo;
        SetLyrics(_gsmtcService.CurrentLyricsData);
        SendCurrentLine(true);

        _syncTimer = new Timer(_ => Execute(() =>
        {
            if (_device == null) return;
            var now = Environment.TickCount64;
            if (_isPlaying) _localPositionMs += now - _lastTickCount;
            _lastTickCount = now;
            SendCurrentLine(false);
        }), null, 100, 100);

        _reconnectTimer = new Timer(_ => Execute(() =>
        {
            if (_device is not { IsConnected: false } || !_device.Open()) return;
            _engine?.Reset();
            _localPositionMs = _gsmtcService.CurrentPosition.TotalMilliseconds;
            _lastTickCount = Environment.TickCount64;
            SendCurrentLine(true);
        }), null, 3000, 3000);
    }

    private void SetLyrics(LyricsData? data)
    {
        // 新版没有旧 LanguageCode 占位标记，显式过滤占位内容也允许真实的单行歌词。
        _lyricsLines = data is { IsPlaceholder: false, LyricsLines.Count: > 0 }
            ? data.LyricsLines
            : null;
        _currentLineIndex = -1;
    }

    public void Receive(CurrentLyricsDataChangedMessage message) => Execute(() =>
    {
        SetLyrics(message.Value);
        SendCurrentLine(true);
    });

    public void Receive(CurrentSongInfoChangedMessage message) => Execute(() =>
    {
        _currentSong = message.Value;
        _lyricsLines = null;
        _currentLineIndex = -1;
        _localPositionMs = _gsmtcService.CurrentPosition.TotalMilliseconds;
        _lastTickCount = Environment.TickCount64;
        if (!Settings.Enable) return;
        var text = string.IsNullOrWhiteSpace(_currentSong.Artist)
            ? _currentSong.Title
            : $"{_currentSong.Title} - {_currentSong.Artist}";
        _engine?.ShowText(text);
    });

    public void Receive(CurrentIsPlayingChangedMessage message) => Execute(() =>
    {
        _isPlaying = message.Value;
        _localPositionMs = _gsmtcService.CurrentPosition.TotalMilliseconds;
        _lastTickCount = Environment.TickCount64;
        // 同一行暂停后恢复也要重发，不能只依赖行号变化。
        SendCurrentLine(true);
    });

    public void Receive(CurrentPositionChangedMessage message) => Execute(() =>
    {
        var positionMs = message.Value.TotalMilliseconds;
        if (_isPlaying && Math.Abs(_localPositionMs - positionMs) < 500) return;
        _localPositionMs = positionMs;
        _lastTickCount = Environment.TickCount64;
        SendCurrentLine(true);
    });

    private void SendCurrentLine(bool force)
    {
        if (!Settings.Enable || _engine == null) return;
        if (!_isPlaying || _lyricsLines == null)
        {
            _engine.ShowClock();
            return;
        }

        var positionMs = _localPositionMs + (_gsmtcService.CurrentMediaSourceProviderInfo?.PositionOffset ?? 0);
        var index = _lyricsLines.FindLastIndex(line => line.StartMs <= positionMs);
        if (!force && index == _currentLineIndex) return;
        _currentLineIndex = index;
        if (index < 0)
        {
            _engine.ShowClock();
            return;
        }

        var line = _lyricsLines[index];
        var text = Settings.LyricMode switch
        {
            1 when !string.IsNullOrWhiteSpace(line.SecondaryText) => line.SecondaryText,
            2 when !string.IsNullOrWhiteSpace(line.TertiaryText) => line.TertiaryText,
            _ => line.PrimaryText
        };
        var durationMs = index + 1 < _lyricsLines.Count
            ? _lyricsLines[index + 1].StartMs - line.StartMs
            : Math.Max((_currentSong?.DurationMs ?? 0) - line.StartMs, 5000);
        List<CharTiming>? timings = null;
        if (Settings.LyricMode == 0 && line.IsPrimaryHasRealSyllableInfo && line.PrimaryChars.Count > 0)
        {
            timings = line.PrimaryChars.Select(character =>
                new CharTiming(character.Text, character.StartMs, character.DurationMs)).ToList();
        }
        _engine.SendLyric(text, positionMs, durationMs, timings);
    }

    private void Execute(Action action)
    {
        lock (_sync)
        {
            if (!_started) return;
            try { action(); }
            catch (Exception exception) { _logger.LogError(exception, "Halo PixelBar operation failed."); }
        }
    }

    private void StopDevice()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
        _engine?.Dispose();
        _engine = null;
        _device?.Dispose();
        _device = null;
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_started) return;
            _started = false;
            Settings.PropertyChanged -= OnSettingsChanged;
            IsActive = false;
            StopDevice();
        }
    }

    public void Dispose() => Stop();
}
