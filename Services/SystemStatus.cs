using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace TaskbarTYOL.Services;

public enum NetworkState { None, Wired, Wifi }
public readonly record struct BatteryState(bool Present, int Percent, bool Charging);

/// <summary>Cheap polled system status for the tray: network type and battery.</summary>
internal static class SystemStatus
{
    public static NetworkState GetNetwork()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable()) return NetworkState.None;
            var up = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                            && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                            && !n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (up.Count == 0) return NetworkState.None;
            return up.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) ? NetworkState.Wifi : NetworkState.Wired;
        }
        catch { return NetworkState.None; }
    }

    public static BatteryState GetBattery()
    {
        if (!GetSystemPowerStatus(out var s)) return new(false, 0, false);
        bool present = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255;
        int pct = s.BatteryLifePercent == 255 ? 0 : s.BatteryLifePercent;
        return new(present, pct, s.ACLineStatus == 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
