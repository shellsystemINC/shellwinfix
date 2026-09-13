# Builds a Release single-file TaskbarTYOL.exe, installs it to %LocalAppData%\Programs\TaskbarTYOL,
# registers it to start at sign-in, and launches it (replacing the Windows taskbar).
# Usage:  powershell -ExecutionPolicy Bypass -File .\install.ps1
$ErrorActionPreference = "Stop"
$src = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA "Programs\TaskbarTYOL"
$exe = Join-Path $dest "TaskbarTYOL.exe"

Write-Host "==> Building Release..." -ForegroundColor Cyan
Push-Location $src
try {
    # Build the native explorer.exe supervisor DLL (zig cc) if a compiler is available; otherwise reuse the prebuilt one.
    $hookDll = "$src\Native\hook\TaskbarTYOLHook.dll"
    $zigCmd = Get-Command zig -ErrorAction SilentlyContinue
    $zigExe = if ($zigCmd) { $zigCmd.Source } else { (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter zig.exe -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
    if ($zigExe) {
        Write-Host "==> Building explorer hook (zig)" -ForegroundColor Cyan
        Push-Location "$src\Native\hook"
        try { & $zigExe cc -target x86_64-windows-gnu -shared -O2 -o TaskbarTYOLHook.dll TaskbarTYOLHook.c -lkernel32 -luser32 }
        catch { Write-Warning "zig build failed ($_); using the prebuilt hook DLL if present." }
        Pop-Location
    }
    if (-not (Test-Path $hookDll)) { Write-Warning "TaskbarTYOLHook.dll missing and no compiler found; the explorer-integration feature will be unavailable." }

    # Folder publish (not single-file) + ReadyToRun: fastest cold start at login.
    if (Test-Path "$src\publish") { Remove-Item -Recurse -Force "$src\publish" }
    dotnet publish "TaskbarTYOL.csproj" -c Release -r win-x64 --self-contained false -o "$src\publish" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    if ((Test-Path $hookDll) -and -not (Test-Path "$src\publish\TaskbarTYOLHook.dll")) { Copy-Item $hookDll "$src\publish\" -Force }
} finally { Pop-Location }

Write-Host "==> Stopping any running instance..." -ForegroundColor Cyan
$running = Get-Process TaskbarTYOL -ErrorAction SilentlyContinue
if ($running) {
    # Ask nicely first so it restores the Windows taskbar state itself; force-kill only as a fallback.
    $anyExe = ($running | Select-Object -First 1).Path
    if ($anyExe) { Start-Process -FilePath $anyExe -ArgumentList "--exit" -Wait -ErrorAction SilentlyContinue }
    $running | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    Get-Process TaskbarTYOL -ErrorAction SilentlyContinue | Stop-Process -Force
}
Start-Sleep -Milliseconds 800

Write-Host "==> Installing to $dest" -ForegroundColor Cyan
# Clear the destination first so files dropped by earlier builds (e.g. the old Explorer Shell module) do not linger.
if (Test-Path $dest) { Remove-Item "$dest\*" -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$src\publish\*" $dest -Recurse -Force

Write-Host "==> Autostart" -ForegroundColor Cyan
# The app registers itself as a logon scheduled task (starts immediately at sign-in, unlike Run-key apps which
# Windows deliberately delays). Any old Run-key entry from earlier versions is removed here.
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "TaskbarTYOL" -ErrorAction SilentlyContinue

# Keep settings.json in agreement so the app (re)registers autostart on first run.
$settings = Join-Path $env:APPDATA "TaskbarTYOL\settings.json"
if (Test-Path $settings) {
    try {
        $json = Get-Content $settings -Raw | ConvertFrom-Json
        $json.StartWithWindows = $true
        $json.HideNativeTaskbar = $true
        $json | ConvertTo-Json -Depth 5 | Set-Content $settings -Encoding utf8
    } catch { Write-Warning "Could not update $settings ($_)" }
}

Write-Host "==> Starting TaskbarTYOL" -ForegroundColor Cyan
Start-Process -FilePath $exe

Write-Host ""
Write-Host "Installed. TaskbarTYOL replaces the Windows taskbar, starts with the shell, and comes back if it or" -ForegroundColor Green
Write-Host "Explorer restarts." -ForegroundColor Green
Write-Host "Settings: right-click the bar -> Taskbar settings. Uninstall: .\uninstall.ps1"
