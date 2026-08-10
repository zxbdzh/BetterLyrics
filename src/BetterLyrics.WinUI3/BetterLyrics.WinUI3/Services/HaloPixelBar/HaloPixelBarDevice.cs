using System;
using System.Linq;
using HidSharp;

namespace BetterLyrics.WinUI3.Services.HaloPixelBar;

public class HaloPixelBarDevice : IDisposable
{
    private HidStream? _stream;
    private HidDevice? _hidDevice;
    private bool _opening;
    private bool _disposed;

    public bool IsConnected => _stream != null;

    public static HidDevice? FindDevice()
    {
        try
        {
            return DeviceList.Local.GetHidDevices()
                .FirstOrDefault(d =>
                    (d.GetProductName() ?? "").Contains(Protocol.DeviceName) &&
                    d.GetMaxOutputReportLength() > 0 &&
                    GetUsagePage(d) == Protocol.DeviceUsagePage);
        }
        catch
        {
            return null;
        }
    }

    private static int GetUsagePage(HidDevice device)
    {
        try
        {
            var reportDescriptor = device.GetReportDescriptor();
            foreach (var deviceItem in reportDescriptor.DeviceItems)
            {
                foreach (var usage in deviceItem.Usages.GetAllValues())
                {
                    int page = ((int)usage >> 16) & 0xFFFF;
                    if (page == Protocol.DeviceUsagePage) return page;
                }
            }
        }
        catch { }
        return 0;
    }

    public bool Open()
    {
        if (_stream != null) return true;
        if (_opening) return false;
        _opening = true;
        try
        {
            _hidDevice = FindDevice();
            if (_hidDevice == null) return false;

            if (!_hidDevice.TryOpen(out var stream)) return false;
            _stream = stream;
            return true;
        }
        catch
        {
            _stream = null;
            _hidDevice = null;
            return false;
        }
        finally
        {
            _opening = false;
        }
    }

    public bool Write(byte[] packet)
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

    public void Close()
    {
        try { _stream?.Close(); } catch { }
        _stream = null;
        _hidDevice = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        GC.SuppressFinalize(this);
    }
}
