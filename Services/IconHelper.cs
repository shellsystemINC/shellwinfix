using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Services;

internal static class IconHelper
{
    private static readonly Dictionary<string, ImageSource?> _pathCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets a window icon: WM_GETICON → class icon → executable icon.</summary>
    public static ImageSource? GetWindowIcon(IntPtr hWnd, string? exePath)
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            Win32.SendMessageTimeout(hWnd, Win32.WM_GETICON, (IntPtr)Win32.ICON_BIG, IntPtr.Zero, 0x0002, 100, out hIcon);
            if (hIcon == IntPtr.Zero)
                Win32.SendMessageTimeout(hWnd, Win32.WM_GETICON, (IntPtr)Win32.ICON_SMALL2, IntPtr.Zero, 0x0002, 100, out hIcon);
            if (hIcon == IntPtr.Zero)
                Win32.SendMessageTimeout(hWnd, Win32.WM_GETICON, (IntPtr)Win32.ICON_SMALL, IntPtr.Zero, 0x0002, 100, out hIcon);
            if (hIcon == IntPtr.Zero) hIcon = Win32.GetClassLongPtr(hWnd, Win32.GCLP_HICON);
            if (hIcon == IntPtr.Zero) hIcon = Win32.GetClassLongPtr(hWnd, Win32.GCLP_HICONSM);
        }
        catch { /* ignore */ }

        if (hIcon != IntPtr.Zero)
        {
            var img = FromHIcon(hIcon, destroy: false);
            if (img != null) return img;
        }

        if (!string.IsNullOrEmpty(exePath)) return GetFileIcon(exePath);
        return null;
    }

    /// <summary>Icon for any file path (exe, lnk, url...) via the shell — resolves .lnk targets automatically.</summary>
    public static ImageSource? GetFileIcon(string path, bool large = true)
    {
        string key = (large ? "L|" : "S|") + path;
        lock (_pathCache)
        {
            if (_pathCache.TryGetValue(key, out var cached)) return cached;
        }

        ImageSource? result = null;
        try
        {
            var shfi = new Win32.SHFILEINFO();
            uint flags = Win32.SHGFI_ICON | (large ? Win32.SHGFI_LARGEICON : Win32.SHGFI_SMALLICON);
            IntPtr r = Win32.SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
            if (r != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                result = FromHIcon(shfi.hIcon, destroy: true);

            if (result == null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var big = new IntPtr[1];
                if (Win32.ExtractIconEx(path, 0, big, null, 1) > 0 && big[0] != IntPtr.Zero)
                    result = FromHIcon(big[0], destroy: true);
            }
        }
        catch { /* ignore */ }

        lock (_pathCache) _pathCache[key] = result;
        return result;
    }

    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;
    private static readonly Dictionary<string, ImageSource?> _typeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Icon by file *type* only (extension / folder) — never touches the disk, so it is safe for long live-search
    /// result lists that may include network paths. .exe/.lnk/.ico get their real icon since that is what users expect.
    /// </summary>
    public static ImageSource? GetTypeIcon(string path, bool isFolder)
    {
        string ext = isFolder ? "<dir>" : System.IO.Path.GetExtension(path);
        if (!isFolder && ext is ".exe" or ".lnk" or ".ico" or ".url")
        {
            try { if (System.IO.File.Exists(path)) return GetFileIcon(path, large: false); } catch { /* fall through */ }
        }

        lock (_typeCache)
        {
            if (_typeCache.TryGetValue(ext, out var cached)) return cached;
        }

        ImageSource? result = null;
        try
        {
            var shfi = new Win32.SHFILEINFO();
            uint attrs = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            string probe = isFolder ? "folder" : (ext.Length > 0 ? "file" + ext : "file");
            IntPtr r = Win32.SHGetFileInfo(probe, attrs, ref shfi, (uint)Marshal.SizeOf(shfi), Win32.SHGFI_ICON | Win32.SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
            if (r != IntPtr.Zero && shfi.hIcon != IntPtr.Zero) result = FromHIcon(shfi.hIcon, destroy: true);
        }
        catch { /* ignore */ }

        lock (_typeCache) _typeCache[ext] = result;
        return result;
    }

    private static ImageSource? FromHIcon(IntPtr hIcon, bool destroy)
    {
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { if (destroy) Win32.DestroyIcon(hIcon); }
    }
}
