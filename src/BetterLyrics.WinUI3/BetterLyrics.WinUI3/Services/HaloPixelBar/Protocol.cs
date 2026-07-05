using System;
using System.Collections.Generic;
using System.Text;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public static class Protocol
{
    public const int PacketLen = 64;
    public const string DeviceName = "花再 Halo PixelBar";
    public const int DeviceUsagePage = 0xFF14;

    private static readonly byte[] TextHeader = [0x2E, 0xAA, 0xEC, 0xE8, 0x00];

    public const int TextByteBudget = PacketLen - 5 - 2 - 1 - 1; // 55 bytes

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
        [Layout.Center]      = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x00, 0x01, 0xFF, 0xFD, 0x00],
        [Layout.Left]        = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x00, 0x00, 0xFF, 0xFC, 0x00],
        [Layout.Right]       = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x00, 0x02, 0xFF, 0xFE, 0x00],
        [Layout.Stretch]     = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x00, 0x03, 0xFF, 0xFF, 0x00],
        [Layout.ScrollLeft]  = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x01, 0x00, 0xFF, 0xFD, 0x00],
        [Layout.ScrollRight] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xF0, 0xB4, 0xC8, 0x00, 0x02, 0x01, 0x01, 0xFF, 0xFE, 0x00],
    };

    private static readonly Dictionary<UIModel, byte[]> UIModels = new()
    {
        [UIModel.Clock]   = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFB, 0x00],
        [UIModel.Game]    = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x01, 0xFF, 0xFF, 0xFC, 0x00],
        [UIModel.Work]    = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x02, 0xFF, 0xFF, 0xFD, 0x00],
        [UIModel.Reading] = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x03, 0xFF, 0xFF, 0xFE, 0x00],
        [UIModel.Cat]     = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x04, 0xFF, 0xFF, 0xFF, 0x00],
        [UIModel.Dog]     = [0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x02, 0xF0, 0xB4, 0xC8, 0x00, 0x01, 0x05, 0xFF, 0xFF, 0x00, 0x00],
    };

    public static string ClipToByteBudget(string text)
    {
        int bytes = 0;
        int charCount = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            int len = rune.Utf8SequenceLength;
            if (bytes + len > TextByteBudget) break;
            bytes += len;
            charCount += rune.Utf16SequenceLength;
        }
        return text[..charCount];
    }

    public static bool FitsSinglePacket(string text) =>
        Encoding.UTF8.GetByteCount(text) <= TextByteBudget;

    public static int DisplayWidth(string text)
    {
        int width = 0;
        foreach (var rune in text.EnumerateRunes())
            width += rune.Value > 0x2E80 ? 2 : 1;
        return width;
    }

    private static byte Checksum(byte[] textBytes)
    {
        int acc = 128;
        foreach (var b in textBytes)
            acc += b + 2;
        return (byte)(acc % 256);
    }

    private static byte[] Pad(byte[] data)
    {
        var result = new byte[PacketLen];
        Array.Copy(data, result, Math.Min(data.Length, PacketLen));
        return result;
    }

    public static byte[] BuildTextPacket(string text)
    {
        var clipped = ClipToByteBudget(text);
        var textBytes = Encoding.UTF8.GetBytes(clipped);
        int textLen = textBytes.Length;
        int totalLen = 1 + textLen + 1;

        var pkt = new byte[TextHeader.Length + 2 + 1 + textLen + 1];
        Array.Copy(TextHeader, pkt, TextHeader.Length);
        int offset = TextHeader.Length;
        pkt[offset++] = (byte)(totalLen & 0xFF);
        pkt[offset++] = (byte)((totalLen >> 8) & 0xFF);
        pkt[offset++] = (byte)(textLen & 0xFF);
        Array.Copy(textBytes, 0, pkt, offset, textLen);
        offset += textLen;
        pkt[offset] = Checksum(textBytes);

        return Pad(pkt);
    }

    public static byte[] BuildLayoutPacket(Layout layout)
    {
        return Pad((byte[])Layouts[layout].Clone());
    }

    public static byte[] BuildUIModelPacket(UIModel model)
    {
        return Pad((byte[])UIModels[model].Clone());
    }
}
