using BetterLyrics.Core.Models.Settings;
using BetterLyrics.WinUI3.Services.HaloPixelBar;
using System.Collections.Concurrent;
using System.Text;
using Xunit;

namespace BetterLyrics.Core.Tests;

public class HaloPixelBarTests
{
    [Theory]
    [InlineData(0xFF140000u, true)]
    [InlineData(0xFF140001u, true)]
    [InlineData(0xFF14FFFFu, true)]
    [InlineData(0xFF130001u, false)]
    [InlineData(0x00010001u, false)]
    [InlineData(0xFFFF0001u, false)]
    public void MatchesOnlyHaloUsagePage(uint usage, bool expected)
    {
        Assert.Equal(expected, Protocol.MatchesDeviceUsagePage(usage));
    }

    [Fact]
    public void TextPacketPreservesTheDeviceWireFormat()
    {
        var packet = Protocol.BuildTextPacket("A");

        Assert.Equal(64, packet.Length);
        Assert.Equal(new byte[] { 0x2E, 0xAA, 0xEC, 0xE8, 0, 3, 0, 1, 0x41, 0xC3 }, packet[..10]);
        Assert.All(packet[10..], value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData("中", 18)]
    [InlineData("😀", 13)]
    public void PacketTruncationKeepsWholeUnicodeCharacters(string character, int expectedCount)
    {
        var text = string.Concat(Enumerable.Repeat(character, 40));

        var clipped = Protocol.ClipToByteBudget(text);
        var packet = Protocol.BuildTextPacket(text);

        Assert.Equal(string.Concat(Enumerable.Repeat(character, expectedCount)), clipped);
        Assert.Equal(clipped, DecodeText(packet));
        Assert.InRange(packet[7], 1, Protocol.TextByteBudget);
    }

    [Fact]
    public async Task ClockCancelsPendingLyrics()
    {
        using var device = new RecordingDevice();
        using var engine = new DisplayEngine(device, new HaloPixelBarSettings());

        engine.SendLyric("过期歌词", 0, 1000);
        engine.ShowClock();
        await Task.Delay(180);

        Assert.Equal(Protocol.BuildUIModelPacket(Protocol.UIModel.Clock), device.Packets.Last());
    }

    [Fact]
    public async Task AlternationUsesSelectedAlignmentFromItsFirstFrame()
    {
        using var device = new RecordingDevice();
        using var engine = new DisplayEngine(device, new HaloPixelBarSettings
        {
            AlternateSplit = true, AutoScroll = false, ScrollThreshold = 4, TextAlignment = 2
        });

        engine.SendLyric("天地玄黄宇宙洪荒", 0, 10000);
        await device.WaitForTextAsync("天地玄黄");

        Assert.Equal(Protocol.BuildLayoutPacket(Protocol.Layout.Right), device.Packets.First());
    }

    [Fact]
    public async Task AlternationCanTypeTheFirstSegment()
    {
        using var device = new RecordingDevice();
        using var engine = new DisplayEngine(device, new HaloPixelBarSettings
        {
            AlternateSplit = true, AutoScroll = false, ScrollThreshold = 4,
            Typewriter = true, TypewriterSpeed = 30
        });

        engine.SendLyric("天地玄黄宇宙洪荒", 0, 10000);

        await device.WaitForTextAsync("天");
    }

    [Fact]
    public async Task SynchronizedTypingIsIndependentOfOrdinaryTyping()
    {
        using var device = new RecordingDevice();
        using var engine = new DisplayEngine(device, new HaloPixelBarSettings
        {
            Typewriter = false, TypewriterSync = true, LatencyCompMs = 0
        });

        engine.SendLyric("ABC", 1150, 6000,
            [new("A", 1000, 100), new("B", 1100, 100), new("C", 5000, 100)]);

        await device.WaitForTextAsync("AB");
    }

    [Fact]
    public async Task LongSinglePacketTextSwitchesToHardwareScrolling()
    {
        using var device = new RecordingDevice();
        using var engine = new DisplayEngine(device, new HaloPixelBarSettings
        {
            AutoScroll = true, AlternateSplit = false, ScrollThreshold = 4
        });

        engine.SendLyric("abcdef", 0, 300);
        await device.WaitForPacketAsync(packet =>
            packet.SequenceEqual(Protocol.BuildLayoutPacket(Protocol.Layout.ScrollRight)));
    }

    [Fact]
    public async Task OversizedUtf8TextUsesSoftwareScrolling()
    {
        using var device = new RecordingDevice();
        using var engine = new DisplayEngine(device, new HaloPixelBarSettings
        {
            AutoScroll = true, AlternateSplit = false, ScrollThreshold = 100
        });
        var text = new string('中', 40);

        engine.SendLyric(text, 0, 10000);

        await device.WaitForTextAsync(new string('中', 18));
    }

    private static string DecodeText(byte[] packet) =>
        Encoding.UTF8.GetString(packet, 8, packet[7]);

    private sealed class RecordingDevice : IHaloPixelBarDevice
    {
        private readonly SemaphoreSlim _packetSignal = new(0);
        public ConcurrentQueue<byte[]> Packets { get; } = new();
        public bool IsConnected => true;
        public bool Open() => true;

        public bool Write(byte[] packet)
        {
            Packets.Enqueue(packet);
            _packetSignal.Release();
            return true;
        }

        public Task WaitForTextAsync(string expected) =>
            WaitForPacketAsync(packet => packet[3] == 0xE8 && DecodeText(packet) == expected);

        public async Task WaitForPacketAsync(Func<byte[], bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!Packets.Any(predicate))
                await _packetSignal.WaitAsync(timeout.Token);
        }

        public void Dispose() => _packetSignal.Dispose();
    }
}
