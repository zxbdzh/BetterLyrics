using System;
using System.Collections.Generic;
using System.Text;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public static class Protocol
{
    public const int PacketLen = 64;
    public const string DeviceName = "花再 Halo PixelBar";
    public const int DeviceUsagePage = 0xFF14;
    public const int TextByteBudget = PacketLen - 5 - 2 - 1 - 1;

    private static readonly byte[] TextHeader = [0x2E, 0xAA, 0xEC, 0xE8, 0x00];

    public enum Layout
    {
        Center,
        Left,
        Right,
        Stretch,
        ScrollLeft,
        ScrollRight
    }

    public enum UIModel
    {
        Clock,
        Game,
        Work,
        Reading,
        Cat,
        Dog
    }

    private static readonly Dictionary<Layout, byte[]> Layouts = new()
    {
        [Layout.Center] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x00, 0x01, 0xFF, 0xFD, 0x00],
        [Layout.Left] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x00, 0x00, 0xFF, 0xFC, 0x00],
        [Layout.Right] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x00, 0x02, 0xFF, 0xFE, 0x00],
        [Layout.Stretch] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x00, 0x03, 0xFF, 0xFF, 0x00],
        [Layout.ScrollLeft] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x01, 0x00, 0xFF, 0xFD, 0x00],
        [Layout.ScrollRight] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x01, 0x01, 0xFF, 0xFE, 0x00]
    };

    private static readonly Dictionary<UIModel, byte[]> UIModels = new()
    {
        [UIModel.Clock] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFB, 0x00],
        [UIModel.Game] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x01, 0xFF, 0xFF, 0xFC, 0x00],
        [UIModel.Work] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x02, 0xFF, 0xFF, 0xFD, 0x00],
        [UIModel.Reading] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x03, 0xFF, 0xFF, 0xFE, 0x00],
        [UIModel.Cat] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x04, 0xFF, 0xFF, 0xFF, 0x00],
        [UIModel.Dog] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x05, 0xFF, 0xFF, 0x00, 0x00]
    };

    public static string ClipToByteBudget(string text)
    {
        var byteCount = 0;
        var charCount = 0;
        // 设备按 UTF-8 字节限制文本，截断时不能拆开中文或代理对。
        foreach (var rune in text.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > TextByteBudget) break;
            byteCount += rune.Utf8SequenceLength;
            charCount += rune.Utf16SequenceLength;
        }
        return text[..charCount];
    }

    public static bool FitsSinglePacket(string text) =>
        Encoding.UTF8.GetByteCount(text) <= TextByteBudget;

    // HID 的高 16 位是无符号 Usage Page，先转 int 会让厂商页面右移后变成负数。
    public static bool MatchesDeviceUsagePage(uint usage) =>
        (usage >> 16) == DeviceUsagePage;

    public static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
            width += rune.Value > 0x2E80 ? 2 : 1;
        return width;
    }

    public static byte[] BuildTextPacket(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(ClipToByteBudget(text));
        var packet = new byte[PacketLen];
        Array.Copy(TextHeader, packet, TextHeader.Length);
        packet[5] = (byte)(bytes.Length + 2);
        packet[7] = (byte)bytes.Length;
        Array.Copy(bytes, 0, packet, 8, bytes.Length);
        var checksum = 128;
        foreach (var value in bytes) checksum += value + 2;
        packet[8 + bytes.Length] = (byte)(checksum % 256);
        return packet;
    }

    public static byte[] BuildLayoutPacket(Layout layout) => Pad(Layouts[layout]);
    public static byte[] BuildUIModelPacket(UIModel model) => Pad(UIModels[model]);

    private static byte[] Pad(byte[] data)
    {
        var packet = new byte[PacketLen];
        Array.Copy(data, packet, Math.Min(data.Length, packet.Length));
        return packet;
    }
}
