using System;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public interface IHaloPixelBarDevice : IDisposable
{
    bool IsConnected { get; }
    bool Open();
    bool Write(byte[] packet);
}
