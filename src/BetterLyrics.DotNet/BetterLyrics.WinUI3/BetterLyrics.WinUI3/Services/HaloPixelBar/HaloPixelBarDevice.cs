using HidSharp;
using System;
using System.Linq;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public sealed class HaloPixelBarDevice : IHaloPixelBarDevice
{
    private readonly object _sync = new();
    private HidStream? _stream;
    private bool _disposed;

    public bool IsConnected
    {
        get { lock (_sync) return _stream != null; }
    }

    public static HidDevice? FindDevice()
    {
        try
        {
            return DeviceList.Local.GetHidDevices().FirstOrDefault(device =>
                (device.GetProductName() ?? "").Contains(Protocol.DeviceName) &&
                device.GetMaxOutputReportLength() > 0 &&
                device.GetReportDescriptor().DeviceItems.Any(item =>
                    item.Usages.GetAllValues().Any(Protocol.MatchesDeviceUsagePage)));
        }
        catch
        {
            return null;
        }
    }

    public bool Open()
    {
        lock (_sync)
        {
            if (_disposed) return false;
            if (_stream != null) return true;
            try
            {
                var device = FindDevice();
                if (device == null || !device.TryOpen(out var stream)) return false;
                stream.WriteTimeout = 500;
                _stream = stream;
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }
    }

    public bool Write(byte[] packet)
    {
        lock (_sync)
        {
            if (_stream == null) return false;
            try
            {
                _stream.Write(packet);
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }
    }

    public void Close()
    {
        lock (_sync)
        {
            try { _stream?.Dispose(); }
            catch { }
            _stream = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            Close();
        }
    }
}
