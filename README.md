# TaskbarTYOL

Your own Windows 11 taskbar + start menu replacement, in the spirit of StartAllBack / Open-Shell / Start11.
Native C# / WPF, no third-party packages. 14 themes included.

## Install (replaces the Windows taskbar, starts at sign-in)

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Uninstall (restores the stock taskbar):

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

Dev loop: `dotnet build` then run `bin\Debug\net9.0-windows\TaskbarTYOL.exe`. Requires the .NET 9 SDK.
Quit a running instance cleanly (restores the Windows taskbar) with `TaskbarTYOL.exe --exit`.

### Explorer / shell integration

TaskbarTYOL genuinely runs code *inside* explorer.exe, but not the dangerous way. StartAllBack / ExplorerPatcher drop a
`dxgi.dll` next to explorer.exe (search-order hijack): it persists, trips anti-virus, and can leave you in a login loop after
a Windows update. The old supported hook (`HKLM\...\ShellServiceObjectDelayLoad`) is dead on current Windows 11 — the shell
no longer loads third-party SSOs, verified here (even the default WebCheck isn't loaded).

So injection is done with `SetWindowsHookEx(WH_GETMESSAGE, …, <explorer shell thread>)`, a documented API:

- **No admin, no system-folder file, non-persistent.** The hook is owned by TaskbarTYOL.exe; when the app exits (or you
  reboot) Windows unmaps `TaskbarTYOLHook.dll` from explorer automatically. Worst case is always one reboot away from clean.
- **What runs inside** (`Native\hook\TaskbarTYOLHook.c`, built with zig, ~180 KB, no CRT): a tiny routine on explorer's own
  message pump that keeps explorer's `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` hidden — from the very thread that owns them,
  so the real bar never flickers in when the shell recreates it. It only touches windows owned by the explorer process, never
  TaskbarTYOL's own look-alike tray host. No worker thread, so unloading it can never crash the shell.
- **Re-injects on Explorer restart**: our 1 s watchdog notices the new explorer.exe and hooks it again.
- Toggle in Settings → Behaviour ("Run inside explorer.exe"). Off = the bar just runs as its own process.

Startup and crash-resilience do NOT depend on injection (the hook dies with our app, so it can't relaunch us):

- **Starts with the shell** — a per-user *logon scheduled task* fires the moment the desktop appears.
- **Comes back if it crashes** — that task has restart-on-failure (3 tries, 1 min apart). A clean Exit is not resurrected.
- **Survives an Explorer restart** — the bar is its own process; a 1 s watchdog re-hides the Windows taskbar, re-claims the
  screen edge and re-injects. (Verified: same bar PID before and after `taskkill /f explorer.exe`.)
- **Adopts your pins** — on first run it imports the shortcuts you had pinned to the Windows taskbar.

### Startup speed

- Autostart is a **logon scheduled task** (`schtasks /Query /TN TaskbarTYOL`), not a Run-key entry: Windows launches logon tasks
  the moment the desktop appears, while Run-key / Startup-folder apps are deliberately delayed by several seconds.
- The installer publishes with **ReadyToRun** (pre-compiled native code) as a plain folder, so login pays no JIT and no
  single-file extraction. Warm launch-to-bar dropped from ~1 s to ~0.6 s on the dev box; the cold-login gain is larger.
- All icon extraction runs on worker threads after the bar is on screen; volume/network probing waits for first render; the
  start-menu index is pre-warmed 4 s after startup so the first Win-key press is instant.

### How the stock taskbar is really replaced

Hiding `Shell_TrayWnd` alone is not enough: Explorer keeps reserving its strip of the work area, so a replacement
AppBar gets pushed *above* a ghost gap and "floats". TaskbarTYOL therefore flips Explorer's taskbar into auto-hide
(`ABM_SETSTATE`, reserves nothing), hides its window, claims the real screen edge for its own AppBar and sets the work
area itself (`SPI_SETWORKAREA`). The original taskbar state is stored in `settings.json` so it is restored on exit even
after a force-kill. A 1 s watchdog re-applies all of this if Explorer restarts.

## What it does

- **Replaces** the built-in taskbar: registers as a shell AppBar (windows never maximise under it), hides `Shell_TrayWnd`
  on every monitor and re-hides it if Explorer brings it back. Restores it on exit or crash.
- **Windows key / Ctrl+Esc** open *this* start menu instead of the Windows one. Win+E, Win+D, Win+Tab etc. untouched.
- **Start menu**: every installed app, grouped A-Z with icons, instant search, right-click → pin / run as admin / file location,
  user folders, This PC, Run, Task Manager, Windows Settings, Lock / Sign out / Sleep / Restart / Shut down.
- **Task buttons**: pinned launchers + running windows (combined per app or one button per window), live titles + icons,
  active highlight, click to focus / minimise / cycle, middle-click for a new instance, right-click to pin, unpin or close.
- **System tray**: volume icon with slider popup (wheel to adjust, right-click to mute), network (Wi-Fi / Ethernet / offline,
  click = Quick Settings), battery % (laptops), clock + calendar popup, notification centre, show-desktop strip, search, task view.
- **Real notification-area icons**: TaskbarTYOL becomes the tray host (it owns a `Shell_TrayWnd` window and broadcasts
  `TaskbarCreated`), so Steam, Discord, OneDrive & co. register their icons *here*. Left/right/double-click and tooltips are
  forwarded to the owning app exactly like Explorer does (NOTIFYICON_VERSION_4 aware). AppBar traffic is proxied to Explorer.
- **Overflow chevron (`^`)**: like Windows, new icons live in a flyout behind the chevron. Drag an icon onto the bar to keep it
  visible, drag it back into the flyout to hide it, or tick it in Settings → Tray icons. Choices persist per app.
  "Always show every icon" is one checkbox away for the Windows-7 crowd.
- **Search = Everything**: the search button and Win+S open a live search box powered by voidtools Everything (over its
  WM_COPYDATA IPC — no SDK/es.exe needed). Everything's own syntax works (`ext:pdf`, `size:>10mb`, `dm:today`, regex…).
  Enter opens, Ctrl+Enter reveals in Explorer, right-click for more, "Open in Everything" hands the query over.
- **Multi-monitor**: a bar on every monitor (toggle in settings), start menu opens on the monitor you clicked.
- **Settings** (right-click bar → Taskbar settings): theme, top/bottom, left/centre alignment, height, icon size, opacity,
  labels, combine, every tray button, Win-key takeover, autostart, pinned apps manager. Everything applies live.

## Themes

| Theme | Vibe |
|---|---|
| Fluent Dark / Fluent Light | Windows 11 |
| Aero | Windows 7 glass + orb |
| Luna | Windows XP, green "start" |
| Classic | Windows 95/98/2000 grey |
| Metro Dark | Windows 10 flat |
| Dracula, Nord, Catppuccin, Solarized, Gruvbox, Rose Pine | popular editor palettes |
| Cyberpunk | neon magenta / cyan |
| Amber Terminal | monochrome CRT |

Every theme is a XAML `ResourceDictionary` in `Themes/` with the same ~35 keys (brushes, corner radii, font, start text,
logo colours). They are generated from the table in `Themes/gen-themes.mjs` — add a row, run `node Themes/gen-themes.mjs`,
add the name to `ThemeManager.Themes`, done. Or hand-edit a generated `.xaml`.

## Layout

```
Native/    Win32 P/Invoke, AppBar registration, low-level Win-key hook, monitor enumeration
Services/  window tracker (EnumWindows polling), icon extraction, start-menu app indexer, .lnk resolver,
           CoreAudio volume, network/battery status, theme manager, native-taskbar hide/restore, autostart
Views/     TaskbarWindow (one per monitor), StartMenuWindow, SettingsWindow
Controls/  WindowsLogo (theme-coloured 4-pane logo), value converters
Themes/    Styles.xaml (all control templates, DynamicResource-bound) + 14 generated theme dictionaries
```

Settings: `%AppData%\TaskbarTYOL\settings.json`. Errors: `%AppData%\TaskbarTYOL\error.log`.

## Known limits

- Third-party notification-area icons (Steam, Discord tray icons…) are not re-hosted; the stock hidden tray still owns them.
  Use the notification-centre / Quick Settings buttons, or temporarily "Show Windows taskbar" from the bar's right-click menu.
- Explorer's own flyouts (Quick Settings, notifications, Task View) are opened through their Win+key shortcuts.
