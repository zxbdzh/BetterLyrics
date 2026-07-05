using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BetterLyrics.Core.Models.Settings;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public sealed class DisplayEngine : IDisposable
{
    private readonly HaloPixelBarDevice _device;
    private HaloPixelBarSettings _settings;

    private string? _pendingText;
    private double _pendingAnchorMs;
    private double _pendingLineDurationMs;
    private List<CharTiming>? _pendingTimings;

    private string? _lastText;
    private double _lastAnchorMs;
    private bool _showingClock;
    private bool _disposed;

    private Timer? _debounceTimer;
    private Timer? _effectTimer;

    private double _anchorPlaybackMs;
    private long _anchorWallTicks;
    private double _playbackRate = 1.0;

    private const int FlushDelayMs = 30;
    private const int ClockTickMs = 50;
    private const int MarqueeHoldMs = 800;
    private const int MinMarqueeStepMs = 120;
    private const int DefaultMarqueeStepMs = 300;
    private const double DedupThresholdMs = 500;
    private const int MinAlternateIntervalMs = 1000;

    public DisplayEngine(HaloPixelBarDevice device, HaloPixelBarSettings settings)
    {
        _device = device;
        _settings = settings;
    }

    public void UpdateSettings(HaloPixelBarSettings settings)
    {
        _settings = settings;
    }

    public void SendLyric(string text, double anchorMs, double lineDurationMs,
        List<CharTiming>? charTimings = null, double playbackRate = 1.0)
    {
        _pendingText = text;
        _pendingAnchorMs = anchorMs;
        _pendingLineDurationMs = lineDurationMs;
        _pendingTimings = charTimings;
        _playbackRate = playbackRate;

        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ => Flush(), null, FlushDelayMs, Timeout.Infinite);
    }

    public void ShowText(string text)
    {
        StopEffect();
        if (string.IsNullOrWhiteSpace(text)) { ShowClock(); return; }
        text = text.Trim();
        _showingClock = false;
        _lastText = text;

        bool isLong = Protocol.DisplayWidth(text) > _settings.ScrollThreshold;
        if (isLong)
        {
            StartMarquee(text, 0);
        }
        else
        {
            WriteLayout(GetStaticLayout());
            WriteText(text);
        }
    }

    public void ShowClock()
    {
        StopEffect();
        if (_showingClock) return;
        if (!EnsureOpen()) return;

        if (_device.Write(Protocol.BuildUIModelPacket(Protocol.UIModel.Clock)))
        {
            _showingClock = true;
            _lastText = null;
        }
    }

    public void Reset()
    {
        StopEffect();
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _lastText = null;
        _lastAnchorMs = 0;
        _showingClock = false;
    }

    private void Flush()
    {
        var text = _pendingText;
        if (string.IsNullOrWhiteSpace(text)) { ShowClock(); return; }
        text = text.Trim();

        double anchorMs = _pendingAnchorMs;
        double lineDurationMs = _pendingLineDurationMs;
        var timings = _pendingTimings;

        if (!_showingClock && text == _lastText && Math.Abs(anchorMs - _lastAnchorMs) < DedupThresholdMs)
            return;

        StopEffect();
        _showingClock = false;
        _lastText = text;
        _lastAnchorMs = anchorMs;
        _anchorPlaybackMs = anchorMs;
        _anchorWallTicks = Environment.TickCount64;

        if (!EnsureOpen()) return;

        int width = Protocol.DisplayWidth(text);
        bool isLong = width > _settings.ScrollThreshold;

        if (isLong)
        {
            if (_settings.AlternateSplit)
            {
                StartAlternation(text, lineDurationMs);
            }
            else if (_settings.AutoScroll)
            {
                if (Protocol.FitsSinglePacket(text))
                    TypeFullThenScroll(text, lineDurationMs);
                else
                    StartMarquee(text, lineDurationMs);
            }
            else
            {
                WriteLayout(GetStaticLayout());
                WriteText(Protocol.ClipToByteBudget(text));
            }
        }
        else
        {
            if (_settings.TypewriterSync && timings != null && timings.Count > 0)
            {
                StartTypewriterSync(text, timings);
            }
            else if (_settings.Typewriter)
            {
                int speed = CalculateTypewriterSpeed(text, lineDurationMs);
                StartTypewriter(text, speed, null);
            }
            else
            {
                WriteLayout(GetStaticLayout());
                WriteText(text);
            }
        }
    }

    private void StartTypewriter(string text, int speedMs, Action? onComplete)
    {
        WriteLayout(GetStaticLayout());

        var runes = text.EnumerateRunes().ToArray();
        int total = runes.Length;
        int index = 0;
        string captured = text;

        void Tick(object? _)
        {
            if (_lastText != captured || index >= total) { onComplete?.Invoke(); return; }

            index++;
            int charEnd = 0;
            for (int i = 0; i < index; i++) charEnd += runes[i].Utf16SequenceLength;
            WriteText(captured[..charEnd]);

            if (index >= total)
            {
                onComplete?.Invoke();
                return;
            }

            try { _effectTimer?.Change(speedMs, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        _effectTimer = new Timer(Tick, null, 0, Timeout.Infinite);
    }

    private void StartTypewriterSync(string text, List<CharTiming> timings)
    {
        WriteLayout(GetStaticLayout());

        int lastVisibleCount = 0;
        string captured = text;

        void Tick(object? _)
        {
            if (_lastText != captured) return;

            double est = _anchorPlaybackMs
                + (Environment.TickCount64 - _anchorWallTicks) * _playbackRate
                + _settings.LatencyCompMs;

            int visibleCount = 0;
            foreach (var ct in timings)
            {
                if (ct.StartMs <= est) visibleCount += ct.Text.Length;
                else break;
            }

            if (visibleCount > lastVisibleCount && visibleCount <= captured.Length)
            {
                lastVisibleCount = visibleCount;
                WriteText(captured[..visibleCount]);
            }

            if (visibleCount >= captured.Length) return;

            try { _effectTimer?.Change(ClockTickMs, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        _effectTimer = new Timer(Tick, null, 0, Timeout.Infinite);
    }

    private void TypeFullThenScroll(string text, double lineDurationMs)
    {
        var runes = text.EnumerateRunes().ToArray();
        int total = runes.Length;
        double halfDuration = lineDurationMs > 0 ? lineDurationMs / 2.0 : total * 80;
        int speed = Math.Max(30, (int)(halfDuration / total));

        WriteLayout(GetStaticLayout());

        int index = 0;
        string captured = text;

        void Tick(object? _)
        {
            if (_lastText != captured) return;

            index++;
            int charEnd = 0;
            for (int i = 0; i < index; i++) charEnd += runes[i].Utf16SequenceLength;
            WriteText(captured[..charEnd]);

            if (index >= total)
            {
                WriteLayout(Protocol.Layout.ScrollRight);
                WriteText(captured);
                return;
            }

            try { _effectTimer?.Change(speed, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        _effectTimer = new Timer(Tick, null, 0, Timeout.Infinite);
    }

    private void StartMarquee(string text, double lineDurationMs)
    {
        WriteLayout(GetStaticLayout());

        var runes = text.EnumerateRunes().ToArray();
        int runeCount = runes.Length;
        int threshold = _settings.ScrollThreshold;

        int lastStart = runeCount;
        int tailWidth = 0;
        while (lastStart > 0)
        {
            int cw = runes[lastStart - 1].Value > 0x2E80 ? 2 : 1;
            if (tailWidth + cw > threshold) break;
            tailWidth += cw;
            lastStart--;
        }

        if (lastStart <= 0) { WriteText(text); return; }

        int stepMs;
        if (lineDurationMs > 0)
            stepMs = Math.Max(MinMarqueeStepMs, (int)((lineDurationMs - 2 * MarqueeHoldMs) / lastStart));
        else
            stepMs = DefaultMarqueeStepMs;

        int start = 0;
        string captured = text;

        void Tick(object? _)
        {
            if (_lastText != captured) return;

            int w = 0;
            int endIdx = start;
            while (endIdx < runeCount)
            {
                int cw = runes[endIdx].Value > 0x2E80 ? 2 : 1;
                if (w + cw > threshold) break;
                w += cw;
                endIdx++;
            }

            int charStart = 0;
            for (int i = 0; i < start; i++) charStart += runes[i].Utf16SequenceLength;
            int charEnd = charStart;
            for (int i = start; i < endIdx; i++) charEnd += runes[i].Utf16SequenceLength;

            if (!WriteText(captured[charStart..charEnd])) return;

            bool atEnd = start >= lastStart;
            int delay = (atEnd || start == 0) ? MarqueeHoldMs + stepMs : stepMs;
            start = atEnd ? 0 : start + 1;

            try { _effectTimer?.Change(delay, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        _effectTimer = new Timer(Tick, null, 0, Timeout.Infinite);
    }

    private void StartAlternation(string text, double lineDurationMs)
    {
        var (first, second) = SplitByWidth(text);
        int interval = Math.Max(MinAlternateIntervalMs, _settings.AlternateInterval);
        double segBudget = lineDurationMs > 0
            ? (lineDurationMs - MarqueeHoldMs) / 2.0
            : interval;

        bool showingFirst = true;
        string captured = text;

        if (_settings.Typewriter)
        {
            int speed1 = CalculateTypewriterSpeed(first, segBudget);
            StartTypewriter(first, speed1, () =>
            {
                if (_lastText != captured) return;
                _effectTimer?.Dispose();
                _effectTimer = new Timer(_ =>
                {
                    if (_lastText != captured) return;
                    int speed2 = CalculateTypewriterSpeed(second, segBudget);
                    StartTypewriter(second, speed2, null);
                }, null, interval, Timeout.Infinite);
            });
        }
        else
        {
            WriteLayout(Protocol.Layout.Center);
            WriteText(first);

            void Tick(object? _)
            {
                if (_lastText != captured) return;
                showingFirst = !showingFirst;

                WriteLayout(GetStaticLayout());
                WriteText(showingFirst ? first : second);

                try { _effectTimer?.Change(interval, Timeout.Infinite); }
                catch (ObjectDisposedException) { }
            }

            _effectTimer = new Timer(Tick, null, interval, Timeout.Infinite);
        }
    }

    private static (string first, string second) SplitByWidth(string text)
    {
        var runes = text.EnumerateRunes().ToArray();
        int totalWidth = 0;
        foreach (var r in runes) totalWidth += r.Value > 0x2E80 ? 2 : 1;
        int halfWidth = totalWidth / 2;

        int w = 0;
        int splitIdx = 0;
        for (int i = 0; i < runes.Length; i++)
        {
            int cw = runes[i].Value > 0x2E80 ? 2 : 1;
            if (w + cw > halfWidth) { splitIdx = i; break; }
            w += cw;
            splitIdx = i + 1;
        }

        int charSplit = 0;
        for (int i = 0; i < splitIdx; i++) charSplit += runes[i].Utf16SequenceLength;

        return (text[..charSplit].TrimEnd(), text[charSplit..].TrimStart());
    }

    private int CalculateTypewriterSpeed(string text, double durationMs)
    {
        int charCount = text.EnumerateRunes().Count();
        if (charCount == 0) return _settings.TypewriterSpeed;
        int speedFromDuration = durationMs > 0 ? (int)(durationMs / charCount) : _settings.TypewriterSpeed;
        return Math.Clamp(Math.Min(_settings.TypewriterSpeed, speedFromDuration), 30, 500);
    }

    private Protocol.Layout GetStaticLayout() => _settings.TextAlignment switch
    {
        1 => Protocol.Layout.Left,
        2 => Protocol.Layout.Right,
        _ => Protocol.Layout.Center
    };

    private bool WriteText(string text) => EnsureOpen() && _device.Write(Protocol.BuildTextPacket(text));

    private bool WriteLayout(Protocol.Layout layout) => EnsureOpen() && _device.Write(Protocol.BuildLayoutPacket(layout));

    private bool EnsureOpen()
    {
        if (_device.IsConnected) return true;
        _device.Open();
        return _device.IsConnected;
    }

    private void StopEffect()
    {
        _effectTimer?.Dispose();
        _effectTimer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopEffect();
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        GC.SuppressFinalize(this);
    }
}

public record CharTiming(string Text, int StartMs, int DurationMs);
