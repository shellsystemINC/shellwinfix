# Stops TaskbarTYOL, restores the Windows taskbar, removes autostart and deletes the installed files.
# Settings in %AppData%\TaskbarTYOL are kept unless you pass -PurgeSettings.
param([switch]$PurgeSettings)
$ErrorActionPreference = "SilentlyContinue"

$dest = Join-Path $env:LOCALAPPDATA "Programs\TaskbarTYOL"
$exe = Join-Path $dest "TaskbarTYOL.exe"

# Scrub any dead Shell Service Object registration written by earlier builds (per-user needs no admin; the HKLM
# entry is only present if an old build's UAC step was approved).
Remove-Item -Recurse -Force "HKCU:\Software\Classes\CLSID\{7F1C2B3A-5D4E-4F60-9A8B-2C3D4E5F6071}"
try { Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\ShellServiceObjectDelayLoad" -Name "TaskbarTYOL" -ErrorAction Stop } catch {}

# Ask the running instance to quit gracefully (it restores the taskbar itself); force-kill as fallback.
$running = Get-Process TaskbarTYOL
if ($running) {
    if (Test-Path $exe) { Start-Process -FilePath $exe -ArgumentList "--exit" -Wait }
    $running | Wait-Process -Timeout 5
    Get-Process TaskbarTYOL | Stop-Process -Force
}
Start-Sleep -Milliseconds 800

# Make sure Explorer's taskbar is visible and NOT auto-hidden even if the app was killed hard.
$sig = @'
[DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string w);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
[DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
'@
$types = Add-Type -MemberDefinition $sig -Name Tray -Namespace W -PassThru
$U = $types | Where-Object { $_.Name -eq 'Tray' }
$h = $U::FindWindow("Shell_TrayWnd", $null)
if ($h -ne [IntPtr]::Zero) {
    $U::ShowWindow($h, 5) | Out-Null
    $abd = New-Object W.Tray+APPBARDATA
    $abd.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type][W.Tray+APPBARDATA])
    $abd.hWnd = $h
    $abd.lParam = [IntPtr]0           # state 0 = normal (not auto-hide)
    $U::SHAppBarMessage(10, [ref]$abd) | Out-Null   # ABM_SETSTATE
}
# Tell every app to re-register its tray icon with Explorer (in case ours was hosting them when killed).
$sig2 = '[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s); [DllImport("user32.dll")] public static extern bool SendNotifyMessage(IntPtr h, uint m, IntPtr w, IntPtr l);'
$B = Add-Type -MemberDefinition $sig2 -Name Bcast -Namespace W -PassThru
$B::SendNotifyMessage([IntPtr]0xFFFF, $B::RegisterWindowMessage("TaskbarCreated"), [IntPtr]0, [IntPtr]0) | Out-Null

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "TaskbarTYOL"
schtasks /Delete /TN "TaskbarTYOL" /F 2>$null | Out-Null
Remove-Item -Recurse -Force $dest
if ($PurgeSettings) { Remove-Item -Recurse -Force (Join-Path $env:APPDATA "TaskbarTYOL") }

Write-Host "TaskbarTYOL removed. The Windows taskbar is back." -ForegroundColor Green
