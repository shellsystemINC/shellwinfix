using System.Runtime.InteropServices;

namespace TaskbarTYOL.Services;

/// <summary>Master volume / mute via the CoreAudio COM API (IAudioEndpointVolume). No extra packages needed.</summary>
internal static class AudioService
{
    private static IAudioEndpointVolume? _volume;
    private static DateTime _resolvedAt;

    private static IAudioEndpointVolume? Endpoint()
    {
        // Re-resolve every few seconds so plugging in headphones / switching the default device is picked up.
        if (_volume != null && (DateTime.UtcNow - _resolvedAt).TotalSeconds < 5) return _volume;
        _resolvedAt = DateTime.UtcNow;
        Release(ref _volume);   // free the previous endpoint, otherwise each re-resolve leaks a COM object

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var obj);
            _volume = (IAudioEndpointVolume)obj;
        }
        catch { _volume = null; }
        finally
        {
            var d = device; Release(ref d);
            var e = enumerator; Release(ref e);
        }
        return _volume;
    }

    private static void Release<T>(ref T? com) where T : class
    {
        if (com == null) return;
        try { if (Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com); } catch { /* ignore */ }
        com = null;
    }

    /// <summary>Drop the cached endpoint (e.g. after the default device changed).</summary>
    public static void Reset() => Release(ref _volume);

    public static bool IsAvailable => Endpoint() != null;

    /// <summary>0..100</summary>
    public static int GetVolume()
    {
        try { if (Endpoint() is { } v) { v.GetMasterVolumeLevelScalar(out float f); return (int)Math.Round(f * 100); } }
        catch { Reset(); }
        return 0;
    }

    public static void SetVolume(int percent)
    {
        try
        {
            percent = Math.Clamp(percent, 0, 100);
            Endpoint()?.SetMasterVolumeLevelScalar(percent / 100f, Guid.Empty);
            if (percent > 0 && GetMute()) SetMute(false);
        }
        catch { Reset(); }
    }

    public static bool GetMute()
    {
        try { if (Endpoint() is { } v) { v.GetMute(out bool m); return m; } }
        catch { Reset(); }
        return false;
    }

    public static void SetMute(bool mute)
    {
        try { Endpoint()?.SetMute(mute, Guid.Empty); }
        catch { Reset(); }
    }

    // ---------------- COM plumbing ----------------
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        int VolumeStepUp(Guid eventContext);
        int VolumeStepDown(Guid eventContext);
        int QueryHardwareSupport(out uint mask);
        int GetVolumeRange(out float min, out float max, out float increment);
    }
}
