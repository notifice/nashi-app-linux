# Nashi for Linux (Avalonia)

An Avalonia 12 / .NET 10 desktop port that shares `Nashi.Core` and `Nashi.Engine` with the Windows build. It is built from source only; there are no published Linux releases yet. Targets: `linux-x64`, self-contained.

## Build and run

```sh
dotnet restore windows/Nashi.Linux --locked-mode
dotnet build windows/Nashi.Linux -c Release --no-restore
dotnet run --project windows/Nashi.Linux -c Release --no-build
```

Pass file paths as arguments to open them in a running instance (single-instance socket under `$XDG_RUNTIME_DIR`).

## Checks

- `dotnet run --project windows/Nashi.Linux.Verify -c Release` runs the engine and platform-service checks without a display.
- `NASHI_SMOKE_DIRECTORY=build/linux-smoke xvfb-run -a dotnet run --project windows/Nashi.Linux -c Release --no-build -- --smoke-test` drives the real UI on a virtual X11 display and writes JSON, diagnostics and render artifacts to that directory. `NASHI_SMOKE_DIRECTORY` is required.
- CI runs both in the `linux-desktop` job of `.github/workflows/linux.yml`. That job could not be exercised locally; see the workflow run for its result.

## Packaging

`scripts/package-linux.sh` produces the tar.gz (with `install.sh`) and the AppImage. Run it from the repository root after building.

## Runtime dependencies

Nashi installs what it can into its own tools directory from its setup screen (SHA-256 pinned): FFmpeg/ffprobe, yt-dlp, Deno and ExifTool. Existing system copies of the media tools are usable instead. Not downloaded, install with your package manager:

| Need | Used for | Example |
| --- | --- | --- |
| `perl` | running ExifTool (metadata) | usually preinstalled |
| LibreOffice (`soffice` on PATH, or under `/usr/lib/libreoffice`, `/opt/libreoffice*`) | Office/Pages document conversion | `sudo apt install libreoffice` / `sudo pacman -S libreoffice-fresh` |
| `tesseract` plus language data | OCR | `sudo apt install tesseract-ocr` / `sudo pacman -S tesseract tesseract-data-eng` |
| `xdg-open`, `gdbus` | opening files/links, tray detection | desktop base packages |

X11 client libraries (libX11, libXcursor, libXrandr, libXi, libICE, libSM, fontconfig) are needed by Avalonia; any desktop install has them.

## Desktop notes

- Wayland: Avalonia runs through XWayland. Drag and drop and clipboard behavior on pure Wayland sessions is not verified here.
- Tray: the tray icon uses StatusNotifierItem. It needs a StatusNotifier host (KDE, or GNOME with the AppIndicator extension). Without one Nashi detects the missing host and never hides itself to the tray.
- Launch at login writes `$XDG_CONFIG_HOME/autostart/nashi.desktop` (default `~/.config/autostart`).
- Terminal action tries `xdg-terminal-exec`, konsole, gnome-terminal, kitty, alacritty, foot, xterm.

## Global shortcut

Copy files in your file manager (Ctrl+C), then press a shortcut to open the Nashi wheel for them. Wayland apps cannot register global hotkeys, so bind one yourself: System Settings → Keyboard → Shortcuts → Add New → Command `nashi --wheel` (use the full path of the executable if `nashi` is not on the session PATH; Settings → Shortcut shows it and can copy the command). The packaged `.desktop` file also offers an "Open wheel for copied files" action.

The copied files are read with `wl-paste` in the launching process (XWayland cannot see the Wayland clipboard while Nashi is unfocused), so the `wl-clipboard` package is needed (`sudo apt install wl-clipboard` / `sudo pacman -S wl-clipboard`). Without it, or on X11, no copied files arrive and Nashi shows a short hint instead of the wheel.

The wheel opens centred on the mouse pointer and Nashi's own window stays hidden (also when the shortcut starts Nashi). Under XWayland the pointer is invisible to X11 apps while it is over Wayland windows, so on KDE Plasma Nashi asks KWin through a one-shot KWin script over D-Bus; elsewhere the wheel opens in the middle of the primary screen. Editors then open centred on that screen and progress shows in a toast at the pointer. Without a tray icon, a Nashi started hidden by the shortcut quits once its windows close.

## Differences from Windows

See the Linux column in [FEATURE-PARITY.md](../FEATURE-PARITY.md). Not available on Linux: the global drag wheel (replaced by the global shortcut above) and window-management wheel, Share, face detection in redact, `.lnk` shortcuts. Annotate has fewer tools than Windows (no marker, ellipse, numbered, magnifier or stamp). The media editor previews FFmpeg still frames instead of playing video, and has no audio playback.
