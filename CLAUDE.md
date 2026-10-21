# CLAUDE.md

Guidance for Claude Code and contributors working on this repository. `README.md` (English) and
`README.fr.md` (French) describe the project for users.

## The project

**NowPlaying**: a small open-source (MIT) Windows 11 app that shows **what is playing**, whatever the source
(Spotify, YouTube in a browser, Deezer, Media Player, any app that tells Windows what it plays):

1. a **widget in the taskbar**: cover, title, artist, a visualizer; the controls and the progress on hover; the
   mouse wheel sets the volume of the app that plays;
2. an **overlay for OBS Studio** (a page of the app added as a Browser Source), two layouts (bar, card);
3. the **app window**, where both are customised, **each with its own style**.

Inspired by the features and the widget design of *Spectral* (Steam). UI in English and French, light/dark/system
theme. Author: [QuentinPerou](https://github.com/quentinperou). Sister projects with the same stack and conventions:
ScreenClip (the template of the resident pieces: tray, single instance, updater), PageCompose, FolderTools, CropPdf.
Credits wording: "Developed by QuentinPerou, with Claude Code (Anthropic)".

**Talk to the author in French.** Code, comments, commit messages and these docs are in English.

**Performance comes first**: the app stays resident all day next to games and streams. Nothing playing: no timer, no
audio capture, 0 % CPU (measured). Playing: the bars of the widget are drawn by hand (`BarsView`, no layout pass) at
25 fps, the window is composed by DWM (not `AllowsTransparency`): about 1.7 % of one core (it was 5.6 % with layered
windows and ten resized rectangles). Published build at rest: about 7 MB of working set. Measure a change that runs
while playing (Task Manager, or `TotalProcessorTime` over 10 s) before and after it.

**Git**: commits only in the author's name (repository-local identity `QuentinPerou`, his GitHub noreply address),
**no `Co-Authored-By` line**. **Never push** without his explicit OK: he pushes from his own PC.

## Project status (October 21, 2026)

**Version 0.1.3** (installed on the shop PC over 0.1.2, settings kept; not committed): the widget did not move when the author turned Windows'
weather off (Settings tells no app: the Explorer\Advanced key is now watched; checked: with the weather off the widget
starts at the left edge and the card says "Hidden"; the live move needs him to flip the switch). README picture made
(see "README pictures"). Asked by him too: a muted Twitch tab missing from the list (Chrome tells Windows nothing; see
"Known pitfalls").

**Version 0.1.2** (installed on the shop PC over 0.1.1, settings kept; not committed), asked by the author
while trying 0.1.1:

- a **double click on a slider** gives back its default value (`Ui.Slider(…, defaultValue, …)`, tooltip "Double-clic :
  revenir à 280 px");
- **Updates** and **About** are pages of their own, laid out as in ScreenClip / PageCompose (one card: state and check
  / install button, check at startup, channel; About: icon, version, author, source, Quit);
- a **"Windows weather"** card on the Taskbar page: he wanted the app to hide Windows' weather (and bring it back) so
  that the widget replaces it. **Windows refuses it to apps**: `TaskbarDa` (HKCU …\Explorer\Advanced) is guarded by the
  UCPD driver (`UnauthorizedAccessException`, while another value of the same key writes fine). Not worked around:
  the card says whether the weather shows and opens `ms-settings:taskbar` where the user turns "Widgets" off; the
  widget moves to its place by itself (it looks for the Widgets button again on `WM_SETTINGCHANGE`).

Checked **with the real mouse** on the shop PC (the author handed it over once, then asked to keep tests in the
background): hover (progress, controls), wheel (the player's volume in the Windows mixer, 94 % then 100 %), right click
(the sources, choosing Google Chrome switched the widget to his YouTube video), seek (YouTube went from 0:18 to 0:30;
the fake player does not seek), double click on the Width slider (back to 280 px, the widget widened at once), the
Updates and About pages.

**Version 0.1.1** (installed on the shop PC over 0.1.0, settings kept; not committed): after trying 0.1.0 with YouTube in Chrome (the title,
the channel and the cover showed), the author found the widget **black with black text** and its **corners not
rounded**: the Fluent theme, loaded with the app window, painted every window, the widget too (a dark backdrop over
its whole rectangle). Fixed with `ThemeMode="None"` on the widget window and its DWM settings applied again after a
theme change (`TaskbarWidget.KeepTransparent`); checked with the window open and a 16 px radius. Asked by him too: the
text color now follows the color **read on the screen** just above the widget (`ScreenSampler`, the taskbar line
above it), Windows' light / dark setting only until it is read; and the overlay addresses are in **English**,
`/bar` and `/card` (the French ones removed).

**Version 0.1.0, first build, not committed** (the author commits; the repository has no commit yet). Done the same
day, after the framing, the icon (four rounds of proposals, `.local/maquettes/`) and an interactive HTML mock-up of
the whole app (`.local/maquettes/maquette.html`, followed for the UI):

- the widget in the taskbar, its states (rest, hover, wheel, right click menu, idle), placed after the Widgets
  (weather) button of Windows or near the clock; the two overlay layouts served on `http://localhost:6740/bar` and
  `/card`; the app window (Taskbar · OBS overlay · General) with live previews; tray icon and menu; start with
  Windows; updater; English and French; installer; GitHub workflow.
- 86 tests pass; `scripts\build.ps1` builds `dist\NowPlaying-Setup-0.1.0.exe` (6 MB).
- Checked on the shop PC (light taskbar, weather on) with a **silent fake player** (`.local/fake-player.cs`): the
  widget shows the track and the cover, sits after the weather, the Cover color mode, the Animation bars, the hover
  state (forced), play / pause from the app (the player paused and resumed), the overlays in a browser (card with its
  progress, bar), the three pages of the window.
- **Not checked yet** (need the real mouse or real music): the hover and the wheel by hand, the right click menu of
  the widget, the seek, the real spectrum with sound, Spotify / a browser as players, the tray menu, a language
  change, OBS itself.
- **Installed on the shop PC** (October 21, 2026, at the author's request): the installer ran, the app started from
  `C:\Program Files\NowPlaying`, start with Windows was turned on, the uninstall entry exists.

## Commands

```powershell
.\scripts\build.ps1                       # tests + dist\app (ReadyToRun) + dist\NowPlaying-Setup-<version>.exe
dotnet test tests\NowPlaying.Tests        # unit tests (Core + localization)
dotnet run --project src\NowPlaying       # run (opens the window; --background opens nothing)
powershell -File scripts\make-icons.ps1   # regenerate the icons (Windows PowerShell 5.1)
```

Environment variables: `NOWPLAYING_LANG=en|fr`, `NOWPLAYING_THEME=Light|Dark`, `NOWPLAYING_WIDGET_STATE=hover|volume`
(the widget stays in that state: pictures, tests). Settings live in `%LOCALAPPDATA%\NowPlaying\settings.json`.

## Working on the project

- **Memory of Claude** for this project is in `.local/claude/memory/` (git-ignored, loaded at start through
  `CLAUDE.local.md`; read `MEMORY.md` there first). Personal notes go there, not in this file.
- `.local/` also holds the references (the author's brief, Spectral's pictures: never commit them), the mock-ups and
  their scripts, and the test tools (`.local/README.md` lists them): `dev.ps1` (start, picture, drive by UI
  Automation and stop the Debug build), `fake-player.cs` (a silent media session), `cs/sessions.cs` (what Windows
  lists: media sessions and audio sessions).
- The author may be using the PC: drive the app through UI Automation or posted window messages, never keys or the
  mouse. Picture the app window with `PrintWindow`; for the widget, a screen copy of **its own rectangle only** (the
  rest of the taskbar shows the author's apps). Clean up after a test: quit the app, delete a test `settings.json`.
- Never play sound to test (the shop): `fake-player.cs` plays silence.
- Docs (both READMEs, this file) are kept up to date at the end of each work session, "Project status" included.

### README pictures

`.github/screenshot.png` (English) and `.github/screenshot.fr.png` (French), 1560 × 1000, at the top of the READMEs
(asked by the author, "comme dans le readme de ScreenClip"): on the gradient of the icon, the real app window (light
theme, Taskbar page, the widget in the Cover color), the two real overlays on a stream scene, and the widget on a
Windows 11 taskbar. Made up track "Neon Harbor · Lumen Drift" with a synthetic cover (`.local\readme\make-cover.ps1`).
`.local\readme\capture.ps1 -Lang en|fr` takes the pieces (test settings, the silent fake player, `NOWPLAYING_LANG`,
`NOWPLAYING_THEME=Light`, `NOWPLAYING_NO_MICA=1`, PrintWindow, UI Automation for the preview widget, headless Edge on
`/card?once` and `/bar?once`; the author's settings put back), then `.local\readme\compose.ps1` renders
`.local\readme\hero.html`. Retake them when the UI changes.

## Framing (October 21, 2026, the author's answers)

- Widget: cover, title, artist; controls and progress on hover; the wheel sets the volume of the app that plays;
  visualizer real spectrum or animation (a setting); position left or near the clock (a setting) with an offset;
  several players: **the last one started wins**, right click to choose; nothing playing: hidden or the last track
  greyed (a setting); like: later.
- Overlay: two layouts, **compact bar** and **card**, each with its own style.
- Customisation (widget and each overlay): color from the cover, glass or custom (with opacity); width, corners,
  font and size; which parts show.
- Claude's defaults, not asked: the name, start with Windows, tray icon, main screen only, no popout, port 6740.

## Architecture

| Folder | Role |
|--------|------|
| `src/NowPlaying.Core` | Pure, tested logic: `SourcePicker` (which player to show), `Timeline` (position between reports, "3:34"), `DominantColor` (color of a cover, readable under white text), `SpectrumAnalyzer` (Hann + FFT + log bands + smoothing) and `DecorativeBars`, `Styles` (`WidgetStyle`, `OverlayStyle`, enums, ranges), `SurfaceColors` (background / text / track from a style), `Rgb`, `AppIdentity` (player names, process matching), `SemanticVersion`, `UpdateFeed`. |
| `src/NowPlaying` | WPF .NET 10. `App` (single instance, tray, widget, overlay, commands), `TaskbarWidget` (the window over the taskbar), `MainWindow` (pages built in code), `PopupMenu` (tray and widget menus), `UpdateBar`, `Localization.cs`; `Controls/`: `WidgetView` (the widget, also the preview), `BarsView`, `OverlayPreview`, `StyleEditor`, `Ui` (cards, `Segmented`); `Services/`: `MediaSessions`, `AppVolume`, `AudioSpectrum`, `OverlayServer`, `TrayHost`, `AppSettings`, `WindowsSettings`, `SingleInstance`, `MicaBackdrop`, `MemoryTrim`, `Updater`, `NativeMethods`; `Overlay/`: the overlay page (HTML, CSS, JS, embedded). |
| `tests/NowPlaying.Tests` | xUnit: Core, update feed, localization key parity. |
| `installer/NowPlaying.iss` | Inno Setup (English/French), .NET 10 Desktop Runtime check, closes the running app. |
| `scripts/` | `build.ps1`, `make-icons.ps1`. |
| `.github/workflows/build.yml` | Build + tests on push; release on a `v*` tag (pre-release when the tag has a `-`). |

### Technical choices (and why)

- **Media of any app**: `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` (target
  `net10.0-windows10.0.19041.0`): the sessions of the volume flyout, their track, cover, timeline and commands. Events
  only (no polling), marshalled to the UI thread. Two sessions of one app get ids `appId#2`… Players report their
  position rarely: `Timeline.Extrapolate`. No "like" in this API (the like will need the Spotify Web API).
- **Which player**: `SourcePicker`, the author's rule; a player picked in the menu stays until another one starts.
- **The widget window**: Windows 11 lets no app add to its taskbar. A borderless window of our own, `WS_EX_TOOLWINDOW`
  + `WS_EX_NOACTIVATE` (`WM_MOUSEACTIVATE` → `MA_NOACTIVATE`), topmost, put back on top on every foreground change
  (`SetWinEventHook`), an app bar without room (`ABM_NEW`) for `ABN_FULLSCREENAPP`, a 300 ms check only while the
  taskbar auto-hides. **Never a child of `Shell_TrayWnd`**: a hang of NowPlaying could freeze Explorer.
  Transparency through DWM (`DwmExtendFrameIntoClientArea`, transparent composition background, no backdrop, no
  rounded corners nor border): hardware rendering. `ThemeMode="None"` on it: the Fluent theme of the app window must
  not paint it.
- **Readable text**: in the Transparent mode the text is dark or light after the mean color of the taskbar line just
  above the widget, read on the screen (`ScreenSampler`, a few hundred pixels) when the widget moves or Windows' look
  changes (`WM_SETTINGCHANGE`); before that, Windows' light / dark setting. The Cover and Custom colors choose their
  text by contrast (`Rgb.IsLight`).
- **Its place**: the Widgets (weather) and Start buttons are drawn by Explorer without windows: found by UI
  Automation (`WidgetsButton`, `StartButton`), on a pool thread, again on `WM_SETTINGCHANGE` and when Explorer
  restarts. Left: after the weather; no room before Start (or icons aligned left): near the clock, left of
  `TrayNotifyWnd` (still a real window with its rectangle on Windows 11).
- **Volume of the app**: Core Audio sessions of the default output (NAudio), matched to the player by
  `AppIdentity.Matches` (exe name, Store package family, Firefox's hash). 2 % a notch.
- **Spectrum**: WASAPI loopback of the default output (NAudio), only while bars are shown and something plays
  (`AudioSpectrum.Acquire/Release`); follows a change of output device; readers of one frame share one computation;
  silence lets the bars fall.
- **Overlay**: `HttpListener` on `http://localhost:port/` (no admin rights, this PC only), the page gets events
  (Server-Sent Events: `style`, `state`, `levels`); it reconnects by itself when the app restarts. Colors are worked
  out by the app (`SurfaceColors`), the page only applies CSS variables. The "Animation" bars are drawn by the page.
- **Same stack as the sister projects**: WPF .NET 10 Fluent theme, Mica, resx EN/FR, settings JSON applied at once
  (saved 600 ms after the last change), Inno Setup, updater from GitHub Releases.

### Localization

English (default) and French, following the Windows display language or chosen in General. `Strings.Get("ColorMode"
+ value)` style keys exist for every enum value (a test checks). Installer messages in `installer/NowPlaying.iss`.

## Known pitfalls

- A **muted browser tab** gives Windows no media session (Chrome publishes one for audible playback only): it is in
  neither Windows' flyout nor NowPlaying's list until its sound is on. Nothing an app can read (an extension would).
- Windows Settings changes the Widgets switch (`TaskbarDa`) without telling the apps (no `WM_SETTINGCHANGE`): the
  widget watches `…\Explorer\Advanced` (`RegistryWatcher`) and looks for the taskbar buttons again.
- Headless Edge with `--virtual-time-budget` waits for every request to end: the overlay's event stream never does,
  hence `?once` (the page reads the state once, closes the stream, no fade).
- Windows protects some taskbar settings from apps (UCPD driver): `TaskbarDa` (Widgets / weather) cannot be written;
  open the Settings page instead, never work around the protection.
- Windows PowerShell 5.1 mangles nested JSON arrays (`ConvertFrom-Json`): it once emptied both resx files. Change the
  UI strings with `dotnet run .local\cs\strings.cs -- changes.json`.
- Computer-use screenshots hide (minimize) the windows of apps not yet allowed, NowPlaying's widget included: restart
  the app after granting it. Prefer background tests anyway (the author asked).
- `Application.ThemeMode` (Fluent) applies to every window, also one made see-through: a window that draws its own
  colors needs `ThemeMode="None"`. Tests run with `--background` never load the theme: test the widget with the app
  window open too.
- `PrintWindow` of a transparent window renders its see-through parts black: picture the widget by a screen copy of
  its own rectangle, or through the preview of the app window.
- Posted `WM_MOUSEMOVE` / `WM_MOUSEWHEEL` do not make WPF raise MouseEnter / MouseWheel when the real pointer is
  elsewhere: use `NOWPLAYING_WIDGET_STATE`, or UI Automation on the named buttons of the preview.
- .NET file-based programs (`dotnet run x.cs`) disable built-in COM: NAudio then needs
  `#:property BuiltInComInteropSupport=true` and `#:property PublishAot=false`.

## To do

- [ ] The author tries 0.1.0 (installer), with Spotify and a browser, and OBS.
- [ ] README pictures (the widget in a taskbar, the window, an overlay in OBS).
- [ ] Later: like (Spotify), the taskbars of the other screens, popout window, spectrum of the player only (process
      loopback of Windows 11).
