<p align="center">
  <img src="docs/icon-256.png" width="96" alt="NowPlaying icon">
</p>

<h1 align="center">NowPlaying</h1>

<p align="center">
  What's playing, right in the Windows 11 taskbar, from any app (Spotify, YouTube, Deezer…), and as an overlay for
  OBS Studio.
</p>

<p align="center">
  <b>English</b> · <a href="README.fr.md">Français</a>
</p>

<p align="center">
  <img src=".github/screenshot.png" width="900" alt="NowPlaying: the app window on its Taskbar page (the widget in the color of the cover), the two OBS overlays (a compact bar and a card) on a stream scene, and the widget in a Windows 11 taskbar">
</p>

---

> **Early development.** Version 0.1 shows what plays in the taskbar and in OBS, with its customization. Liking a
> track (Spotify) and the taskbars of the other screens will come next.

## Features

### In the taskbar

- The **cover, title and artist** of what's playing, from any app that tells Windows what it plays: Spotify, a
  browser (YouTube, Deezer, SoundCloud…), Media Player, Apple Music…
- **On hover**: previous, play / pause, next, and the progress bar (click to seek).
- **Mouse wheel**: the volume of the app that plays, not the volume of the whole PC.
- A **visualizer**: the real spectrum of the sound, or a calm animation.
- The text stays readable: its color follows what is really behind the widget (a light or dark taskbar, a colored or
  see-through one).
- Several apps playing: the last one started is shown; **right click** to pick another one.
- On the left, after Windows' weather, or next to the clock. Hidden over full-screen apps. Turn off Windows'
  weather (the app opens the right settings page) and the widget takes its place.

### For streamers

- An **overlay for OBS Studio**: a Browser Source pointing to a page of the app, transparent, updated live.
- Two layouts: a **compact bar** (as in the taskbar) and a **card** (cover, title, artist, progress).

### Your style

- The widget and each overlay have **their own style**: the color of the cover, glass or your own color, width,
  rounded corners, font, and the parts you want to see.

### And also

- Windows 11 look (Fluent, Mica), light or dark theme.
- **English and French**.
- **Starts with Windows**, and uses nothing while nothing plays.
- **Updates in one click** from the app (GitHub Releases).

### Coming next

- [x] Taskbar widget, OBS overlay, customization.
- [ ] Like the track (Spotify).
- [ ] The taskbars of the other screens.

## Installation

1. Download `NowPlaying-Setup-x.y.z.exe` from the [Releases](../../releases) page.
2. Run it. As the installer is not signed, Windows SmartScreen may show “Windows protected your PC”:
   click **More info → Run anyway**.
3. Accept the administrator prompt (UAC).

The installer copies the app to `C:\Program Files\NowPlaying`, adds it to the Start menu, and offers to install the
Microsoft **.NET 10 Desktop Runtime** if it is missing. At its first start the app turns on *Start with Windows*.

In OBS: **Sources → + → Browser**, then paste the address given in **NowPlaying → OBS overlay** (for example
`http://localhost:6740/card`) with the size shown there.

To uninstall: **Settings → Apps → Installed apps → NowPlaying**.

**Requirements:** Windows 11 (Windows 10 2004 or later may work), x64.

## Development

### Prerequisites

| Tool | Purpose | Install |
|------|---------|---------|
| **Git** | Get the code | `winget install Git.Git` |
| **.NET 10 SDK** | Build the app (C# / WPF) and run the tests | `winget install Microsoft.DotNet.SDK.10` |
| **Inno Setup 6** | Build the installer | `winget install JRSoftware.InnoSetup` |

### Build

```powershell
.\scripts\build.ps1
```

The script runs the tests, then writes `dist\app\` (the app, framework-dependent, ReadyToRun) and
`dist\NowPlaying-Setup-<version>.exe`. Options: `-SkipTests`, `-NoInstaller`.

### While developing

```powershell
dotnet test tests\NowPlaying.Tests        # unit tests
dotnet run --project src\NowPlaying       # run the app (--background opens no window)
```

`NOWPLAYING_LANG=en|fr` and `NOWPLAYING_THEME=Light|Dark` force a language or a theme;
`NOWPLAYING_WIDGET_STATE=hover|volume` keeps the widget in that state (pictures). NowPlaying runs once per session:
quit it from its notification icon before starting another build.

### Code layout

```
src/
  NowPlaying.Core/   Logic without UI: which player to show, position between reports, cover color, spectrum
                     (FFT), styles of the widget and overlays, app names, GitHub releases (updates)
  NowPlaying/        WPF app: media sessions of Windows, app volume and sound of the PC (NAudio), the widget
                     window over the taskbar, the overlay server and pages (Overlay/), the app window, updates
installer/           Inno Setup script
scripts/             build.ps1, make-icons.ps1
tests/               Unit tests (xUnit)
```

### Translations

App texts: `src/NowPlaying/Properties/Strings.resx` (English) and `Strings.fr.resx` (French); the tests check that
both have the same keys and placeholders. Installer messages: `installer/NowPlaying.iss`.

### Icons

`scripts\make-icons.ps1` draws the icon (`.ico`, the 256 px PNG of the app, `docs/icon-256.png`, `docs/icons/NowPlaying.svg`).
The generated files are committed: run it again only to change the design.

### Releasing a version

1. Update `<Version>` in `Directory.Build.props`.
2. Commit, then create and push a tag: `git tag v0.1.0`, then `git push origin v0.1.0`.
3. The GitHub Actions workflow builds, tests and publishes the installer in a Release.

## Privacy policy

Nothing you listen to leaves your computer. The overlay pages are served on `localhost` only: other devices on your
network cannot reach them. For the spectrum, the sound of the PC is analyzed in memory while the bars are shown, never
recorded nor sent. The only network access is the **update check** on GitHub (`api.github.com`), which can be turned
off in **General**.

## License

[MIT](LICENSE.md). Uses [NAudio](https://github.com/naudio/NAudio) (MIT).

## Credits

Developed by [QuentinPerou](https://github.com/quentinperou), with [Claude Code](https://claude.com/claude-code) (Anthropic).
