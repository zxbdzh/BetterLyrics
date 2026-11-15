using BetterLyrics.Core.Models.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public sealed class DisplayEngine : IDisposable
{
    private readonly object _sync = new();
    private readonly IHaloPixelBarDevice _device;
    private HaloPixelBarSettings _settings;
    private Timer? _debounceTimer;
    private Timer? _effectTimer;
    private long _generation;
    private long _effectGeneration;
    private string? _lastText;
    private double _lastAnchorMs;
    private double _anchorPlaybackMs;
    private long _anchorWallTicks;
    private double _playbackRate = 1;
    private bool _showingClock;
    private bool _disposed;

    public DisplayEngine(IHaloPixelBarDevice device, HaloPixelBarSettings settings)
    {
        _device = device;
        _settings = settings;
    }

    public void UpdateSettings(HaloPixelBarSettings settings)
    {
        lock (_sync) _settings = settings;
    }

    public void SendLyric(string text, double anchorMs, double lineDurationMs,
        List<CharTiming>? charTimings = null, double playbackRate = 1)
    {
        lock (_sync)
        {
            if (_disposed) return;
            CancelPending();
            var generation = _generation;
            _debounceTimer = new Timer(_ =>
            {
                lock (_sync)
                {
                    // 暂停、切歌和退出后，旧回调不能重新覆盖设备上的时钟。
                    if (_disposed || generation != _generation) return;
                    Flush(text, anchorMs, lineDurationMs, charTimings, playbackRate);
                }
            }, null, 30, Timeout.Infinite);
        }
    }

    public void ShowText(string text)
    {
        lock (_sync)
        {
            if (_disposed) return;
            CancelPending();
            StopEffect();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowClock();
                return;
            }

            _showingClock = false;
            _lastText = text.Trim();
            if (Protocol.DisplayWidth(_lastText) > _settings.ScrollThreshold ||
                !Protocol.FitsSinglePacket(_lastText))
                StartMarquee(_lastText, 0);
            else
            {
                WriteLayout(GetStaticLayout());
                WriteText(_lastText);
            }
        }
    }

    public void ShowClock()
    {
        lock (_sync)
        {
            if (_disposed) return;
            CancelPending();
            StopEffect();
            if (_showingClock) return;
            if (EnsureOpen() && _device.Write(Protocol.BuildUIModelPacket(Protocol.UIModel.Clock)))
            {
                _showingClock = true;
                _lastText = null;
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            CancelPending();
            StopEffect();
            _lastText = null;
            _lastAnchorMs = 0;
            _showingClock = false;
        }
    }

    private void Flush(string text, double anchorMs, double durationMs,
        List<CharTiming>? timings, double playbackRate)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowClock();
            return;
        }
        text = text.Trim();
        if (!_showingClock && text == _lastText && Math.Abs(anchorMs - _lastAnchorMs) < 500)
            return;

        StopEffect();
        _showingClock = false;
        _lastText = text;
        _lastAnchorMs = anchorMs;
        _anchorPlaybackMs = anchorMs;
        _anchorWallTicks = Environment.TickCount64;
        _playbackRate = playbackRate;
        if (!EnsureOpen()) return;

        var isLong = Protocol.DisplayWidth(text) > Math.Max(4, _settings.ScrollThreshold) ||
                     !Protocol.FitsSinglePacket(text);
        if (isLong)
        {
            if (_settings.AlternateSplit) StartAlternation(text, durationMs);
            else if (_settings.AutoScroll)
            {
                if (Protocol.FitsSinglePacket(text)) TypeFullThenScroll(text, durationMs);
                else StartMarquee(text, durationMs);
            }
            else
            {
                WriteLayout(GetStaticLayout());
                WriteText(text);
            }
        }
        else if (_settings.TypewriterSync && timings is { Count: > 0 })
            StartTypewriterSync(text, timings);
        else if (_settings.Typewriter)
            StartTypewriter(text, CalculateTypewriterSpeed(text, durationMs));
        else
        {
            WriteLayout(GetStaticLayout());
            WriteText(text);
        }
    }

    private void StartTypewriter(string text, int speedMs, Action? onComplete = null)
    {
        WriteLayout(GetStaticLayout());
        var runes = text.EnumerateRunes().ToArray();
        var index = 0;
        var charEnd = 0;
        if (runes.Length == 0)
        {
            onComplete?.Invoke();
            return;
        }

        StartEffect(() =>
        {
            charEnd += runes[index++].Utf16SequenceLength;
            WriteText(text[..charEnd]);
            if (index < runes.Length) return speedMs;
            onComplete?.Invoke();
            return null;
        });
    }

    private void StartTypewriterSync(string text, List<CharTiming> timings)
    {
        WriteLayout(GetStaticLayout());
        var lastVisibleCount = 0;
        StartEffect(() =>
        {
            var positionMs = _anchorPlaybackMs +
                             (Environment.TickCount64 - _anchorWallTicks) * _playbackRate +
                             _settings.LatencyCompMs;
            var visibleCount = timings.TakeWhile(timing => timing.StartMs <= positionMs)
                .Sum(timing => timing.Text.Length);
            visibleCount = Math.Min(visibleCount, text.Length);
            if (visibleCount > lastVisibleCount)
            {
                WriteText(text[..visibleCount]);
                lastVisibleCount = visibleCount;
            }
            return visibleCount >= text.Length ? null : 50;
        });
    }

    private void TypeFullThenScroll(string text, double durationMs)
    {
        var count = Math.Max(1, text.EnumerateRunes().Count());
        var halfDuration = durationMs > 0 ? durationMs / 2 : count * 80;
        var speedMs = Math.Max(30, (int)(halfDuration / count));
        StartTypewriter(text, speedMs, () =>
        {
            WriteLayout(Protocol.Layout.ScrollRight);
            WriteText(text);
        });
    }

    private void StartMarquee(string text, double durationMs)
    {
        WriteLayout(GetStaticLayout());
        var runes = text.EnumerateRunes().ToArray();
        var threshold = Math.Max(4, _settings.ScrollThreshold);
        var lastStart = runes.Length;
        var tailWidth = 0;
        var tailBytes = 0;
        while (lastStart > 0)
        {
            var rune = runes[lastStart - 1];
            var width = rune.Value > 0x2E80 ? 2 : 1;
            if (tailWidth + width > threshold || tailBytes + rune.Utf8SequenceLength > Protocol.TextByteBudget)
                break;
            tailWidth += width;
            tailBytes += rune.Utf8SequenceLength;
            lastStart--;
        }
        if (lastStart == 0)
        {
            WriteText(text);
            return;
        }

        var stepMs = durationMs > 0
            ? Math.Max(120, (int)((durationMs - 1600) / lastStart))
            : 300;
        var start = 0;
        StartEffect(() =>
        {
            var width = 0;
            var bytes = 0;
            var segment = new StringBuilder();
            for (var index = start; index < runes.Length; index++)
            {
                var rune = runes[index];
                var runeWidth = rune.Value > 0x2E80 ? 2 : 1;
                if (width + runeWidth > threshold || bytes + rune.Utf8SequenceLength > Protocol.TextByteBudget)
                    break;
                width += runeWidth;
                bytes += rune.Utf8SequenceLength;
                segment.Append(rune.ToString());
            }
            if (!WriteText(segment.ToString())) return null;
            var atEnd = start >= lastStart;
            var delay = start == 0 || atEnd ? 800 + stepMs : stepMs;
            start = atEnd ? 0 : start + 1;
            return delay;
        });
    }

    private void StartAlternation(string text, double durationMs)
    {
        var runes = text.EnumerateRunes().ToArray();
        var halfWidth = Protocol.DisplayWidth(text) / 2;
        var width = 0;
        var charSplit = 0;
        foreach (var rune in runes)
        {
            var runeWidth = rune.Value > 0x2E80 ? 2 : 1;
            if (width + runeWidth > halfWidth) break;
            width += runeWidth;
            charSplit += rune.Utf16SequenceLength;
        }
        var first = text[..charSplit].TrimEnd();
        var second = text[charSplit..].TrimStart();
        var interval = Math.Max(1000, _settings.AlternateInterval);
        var showingFirst = true;
        var segmentBudget = durationMs > 0 ? Math.Max(0, (durationMs - 800) / 2) : interval;

        void ShowSegment()
        {
            var segment = showingFirst ? first : second;
            if (_settings.Typewriter)
                StartTypewriter(segment, CalculateTypewriterSpeed(segment, segmentBudget), () =>
                    StartEffect(() =>
                    {
                        showingFirst = !showingFirst;
                        ShowSegment();
                        return null;
                    }, interval));
            else
            {
                WriteLayout(GetStaticLayout());
                WriteText(segment);
            }
        }

        ShowSegment();
        if (!_settings.Typewriter)
            StartEffect(() =>
            {
                showingFirst = !showingFirst;
                ShowSegment();
                return interval;
            }, interval);
    }

    private void StartEffect(Func<int?> tick, int delayMs = 0)
    {
        StopEffect();
        var generation = _effectGeneration;
        _effectTimer = new Timer(_ =>
        {
            lock (_sync)
            {
                if (_disposed || generation != _effectGeneration) return;
                var nextDelay = tick();
                if (nextDelay.HasValue && generation == _effectGeneration)
                    _effectTimer?.Change(nextDelay.Value, Timeout.Infinite);
            }
        }, null, delayMs, Timeout.Infinite);
    }

    private int CalculateTypewriterSpeed(string text, double durationMs)
    {
        var count = text.EnumerateRunes().Count();
        var durationSpeed = durationMs > 0 && count > 0
            ? (int)(durationMs / count)
            : _settings.TypewriterSpeed;
        return Math.Clamp(Math.Min(_settings.TypewriterSpeed, durationSpeed), 30, 500);
    }

    private Protocol.Layout GetStaticLayout() => _settings.TextAlignment switch
    {
        1 => Protocol.Layout.Left,
        2 => Protocol.Layout.Right,
        _ => Protocol.Layout.Center
    };

    private bool WriteText(string text) => EnsureOpen() && _device.Write(Protocol.BuildTextPacket(text));
    private bool WriteLayout(Protocol.Layout layout) => EnsureOpen() && _device.Write(Protocol.BuildLayoutPacket(layout));
    private bool EnsureOpen() => _device.IsConnected || _device.Open();

    private void CancelPending()
    {
        _generation++;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    private void StopEffect()
    {
        _effectGeneration++;
        _effectTimer?.Dispose();
        _effectTimer = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            ShowClock();
            _disposed = true;
            CancelPending();
            StopEffect();
        }
    }
}

public sealed record CharTiming(string Text, int StartMs, int DurationMs);
