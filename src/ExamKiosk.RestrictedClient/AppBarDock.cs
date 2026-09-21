using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ExamKiosk.RestrictedClient;

internal sealed class AppBarDock : IDisposable
{
    private const int AbmNew = 0x00000000;
    private const int AbmRemove = 0x00000001;
    private const int AbmQueryPos = 0x00000002;
    private const int AbmSetPos = 0x00000003;
    private const int AbeRight = 2;
    private const int AbnPosChanged = 0x00000001;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private HwndSource? source;
    private nint handle;
    private uint callbackMessage;
    private int widthDeviceIndependentPixels;
    private bool registered;

    internal void Register(
        Window window,
        int widthDeviceIndependentPixels)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            widthDeviceIndependentPixels);
        if (registered)
        {
            return;
        }

        this.widthDeviceIndependentPixels = widthDeviceIndependentPixels;
        handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException(
                "The Restricted Client window handle is unavailable.");
        callbackMessage = RegisterWindowMessage(
            $"ExamKiosk.AppBar.{Environment.ProcessId}");
        source.AddHook(WindowProcedure);

        var data = CreateData();
        if (SHAppBarMessage(AbmNew, ref data) == 0)
        {
            source.RemoveHook(WindowProcedure);
            throw new InvalidOperationException(
                "Windows did not register the Restricted Client AppBar.");
        }

        registered = true;
        Position();
    }

    public void Dispose()
    {
        if (!registered)
        {
            return;
        }

        var data = CreateData();
        SHAppBarMessage(AbmRemove, ref data);
        source?.RemoveHook(WindowProcedure);
        source = null;
        registered = false;
    }

    private nint WindowProcedure(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == callbackMessage
            && wParam.ToInt32() == AbnPosChanged)
        {
            Position();
            handled = true;
        }

        return 0;
    }

    private void Position()
    {
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            throw new InvalidOperationException(
                "Windows did not return monitor information for the Restricted Client.");
        }

        var dpi = GetDpiForWindow(handle);
        var width = checked(
            (int)Math.Round(widthDeviceIndependentPixels * dpi / 96d));
        var data = CreateData();
        data.Edge = AbeRight;
        data.Rectangle = new Rectangle
        {
            Left = monitorInfo.Monitor.Right - width,
            Top = monitorInfo.Monitor.Top,
            Right = monitorInfo.Monitor.Right,
            Bottom = monitorInfo.Monitor.Bottom,
        };

        SHAppBarMessage(AbmQueryPos, ref data);
        data.Rectangle.Left = data.Rectangle.Right - width;
        SHAppBarMessage(AbmSetPos, ref data);
        if (!SetWindowPos(
                handle,
                -1,
                data.Rectangle.Left,
                data.Rectangle.Top,
                data.Rectangle.Right - data.Rectangle.Left,
                data.Rectangle.Bottom - data.Rectangle.Top,
                SwpNoActivate))
        {
            throw new InvalidOperationException(
                "Windows could not position the Restricted Client AppBar.");
        }
    }

    private AppBarData CreateData() =>
        new()
        {
            Size = Marshal.SizeOf<AppBarData>(),
            WindowHandle = handle,
            CallbackMessage = callbackMessage,
        };

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(
        uint message,
        ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(
        nint windowHandle,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        internal int Size;
        internal nint WindowHandle;
        internal uint CallbackMessage;
        internal uint Edge;
        internal Rectangle Rectangle;
        internal nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        internal int Size;
        internal Rectangle Monitor;
        internal Rectangle WorkArea;
        internal uint Flags;
    }
}
