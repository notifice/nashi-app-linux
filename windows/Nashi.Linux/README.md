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

## Differences from Windows

See the Linux column in [FEATURE-PARITY.md](../FEATURE-PARITY.md). Not available on Linux: the global drag wheel and window-management wheel, Share, face detection, `.lnk` shortcuts, and the frame, collage, redact and annotate image tools (shown as "isn't available on Linux yet").
