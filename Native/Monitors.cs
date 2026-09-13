using System.Runtime.InteropServices;

namespace TaskbarTYOL.Native;

internal sealed record MonitorInfo(IntPtr Handle, Win32.RECT Bounds, Win32.RECT Work, bool IsPrimary, string Device);

/// <summary>Enumerates physical monitors (device pixels).</summary>
internal static class Monitors
{
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Win32.RECT rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public Win32.RECT rcMonitor;
        public Win32.RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref mi))
                list.Add(new MonitorInfo(h, mi.rcMonitor, mi.rcWork, (mi.dwFlags & 1) != 0, mi.szDevice));
            return true;
        }, IntPtr.Zero);

        // primary first, then left-to-right
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ToList();
    }

    public static IntPtr FromWindow(IntPtr hwnd) => MonitorFromWindow(hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/);
}
