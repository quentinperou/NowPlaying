# CLAUDE.md

Guidance for Claude Code and contributors working on this repository. `README.md` (English) and
`README.fr.md` (French) describe the project for users.

## The project

**NowPlaying**: a small open-source (MIT) Windows 11 app that shows **what is playing**, whatever the source
(Spotify, YouTube in a browser, Deezer, Media Player, any app that tells Windows what it plays):

1. a **widget in the taskbar**: cover, title, artist, a visualizer; the controls and the progress on hover; the
   mouse wheel sets the volume of the app that plays;
2. a **floating widget**, anywhere on the screen (frosted glass, shadow, always on top or not, lockable);
3. a **widget for the Windows Widgets board** (Win + W), three sizes;
4. an **overlay for OBS Studio** (a page of the app added as a Browser Source), two layouts (bar, card), off by
   default;
5. the **app window**, where all of them are customised, **each with its own style**.

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

Since 0.2 the taskbar widget runs in a second process, pinged once a second by the app. Measured on the shop PC, a
YouTube video playing in Chrome, floating widget off, 30 s windows alternated: 0.1.3 1.7 % and 6.2 % of one core,
0.2.0 3.7 % and 5.4 % (app + widget process; about 40 + 65 MB of working set). Such measures vary a lot with what
plays: alternate the builds, several times, before concluding. The floating widget adds its own bars and spectrum
(about 3 % more while playing). Not measured yet with nothing playing.

**Git**: commits only in the author's name (repository-local identity `QuentinPerou`, his GitHub noreply address),
**no `Co-Authored-By` line**. **Never push** without his explicit OK: he pushes from his own PC.

## Project status (October 21, 2026)

**Version 0.2.0** (installed on the shop PC over 0.1.3, settings kept: the Widgets package registered, the widget
process owned by the taskbar, start with Windows kept; not committed), all asked by the author the same afternoon,
tried by him as a dev build first:

- **Widgets board** widget (`src/NowPlaying.Widgets`, a COM server in a sparse package): design A of three rendered
  proposals (`.local/maquettes/widgets-board.png`): the cover blurred as background, the cover, the controls as
  icons; light pastel on a light board, dark on a dark one (Windows writes the widget's name over it). Checked on his
  board in the three sizes, with its icon and its preview in the picker.
- **Taskbar widget visible while Start is open** (his bug: it vanished): it is now owned by `Shell_TrayWnd`, in a
  process of its own (`NowPlaying.exe --taskbar`), watched by the app: he asked that a hang could never freeze the
  taskbar. Checked: visible with Start open; a suspended widget process was ended and started again by itself, the
  taskbar answering all along; closing the app ends both. Its menu (shown by the widget process) opens above the
  taskbar and closes on a click elsewhere.
- **Floating widget** (his idea): drag, place kept, always on top or not, locked, hidden from its menu; Glass
  (blurred), Transparent, Cover, Custom; its own shadow (on/off, density). Checked by pictures: blur, any corner
  radius, no shadow of Windows. Not checked by Claude: the drag (his mouse).
- **OBS overlay off by default** (and the floating widget), its settings still shown; the port moved to its page;
  the bar layout can show the time and the progress (checked on the real page).
- **Live streams** (no length): nothing breaks (tested); "● LIVE" / "● EN DIRECT" replaces the times and the bar when
  the position goes on, nothing when the player tells neither (`Timeline.Kind`). Seen on the overlay page with a fake
  live stream; not yet with a real one in the widget.
- Tray menu: checks as icons (they were shifted from the icons). README picture retaken (floating widget, Widgets
  board). Installer: registers the Widgets package (Windows 11), closes the app by process (a window title matched an
  Explorer window open on a NowPlaying folder).

**Version 0.1.3** (installed on the shop PC over 0.1.2, settings kept; the first commit, `a55ed07`, tag `v0.1.3`, not
pushed): the widget did not move when the author turned Windows'
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
.\scripts\build.ps1                       # tests + dist\app (ReadyToRun) + dist\NowPlaying.Widgets.msix + installer
dotnet test tests\NowPlaying.Tests        # unit tests (Core + localization)
dotnet run --project src\NowPlaying       # run (opens the window; --background opens nothing)
powershell -File scripts\make-icons.ps1   # regenerate the icons (Windows PowerShell 5.1)
```

Environment variables: `NOWPLAYING_LANG=en|fr`, `NOWPLAYING_THEME=Light|Dark`, `NOWPLAYING_WIDGET_STATE=hover|volume`
(the widget stays in that state: pictures, tests). Settings live in `%LOCALAPPDATA%\NowPlaying\settings.json`.

The Widgets board widget in development: register a copy of `src\NowPlaying.Widgets\package` whose manifest has
`Publisher="CN=NowPlaying"` (a loose registration refuses the unsigned OID) and a higher version, with
`Add-AppxPackage -Register <copy>\AppxManifest.xml -ExternalLocation src\NowPlaying.Widgets\bin\Debug\…\win-x64`
(Developer Mode). Same CLSID as the installed package: remove one before adding the other. The provider logs to
`%TEMP%\NowPlaying-Widgets.log`. Its picker previews: `.local\maquettes\widget-preview.ps1`.

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
theme, Taskbar page, the widget in the Cover color), the two real overlays on a stream scene, the widget of the
Widgets board (its picker preview), the floating widget in frosted glass (drawn in `hero.html`: a picture of the real
one would show the desktop behind it) and the widget on a Windows 11 taskbar. Made up track "Neon Harbor · Lumen Drift" with a synthetic cover (`.local\readme\make-cover.ps1`).
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
| `src/NowPlaying` | WPF .NET 10. `App` (single instance, tray, widgets, overlay, commands; `--taskbar`: the widget process), `TaskbarWidget` (the window over the taskbar, in the widget process), `FloatingWidget` + `FloatingGlass` (its blur) + `FloatingShadow`, `MainWindow` (pages built in code), `PopupMenu` (tray and widget menus), `UpdateBar`, `Localization.cs`; `Controls/`: `WidgetView` (the widget, also the previews), `BarsView`, `OverlayPreview`, `StyleEditor`, `Ui` (cards, `Segmented`); `Services/`: `MediaSessions`, `AppVolume`, `AudioSpectrum`, `OverlayServer`, `TaskbarHost` (app side) + `TaskbarClient` (widget process) + `TaskbarLink` (`LineChannel`, the messages), `SeeThrough`, `TrayHost`, `AppSettings`, `WindowsSettings`, `SingleInstance`, `MicaBackdrop`, `MemoryTrim`, `Updater`, `NativeMethods`; `Overlay/`: the overlay page (HTML, CSS, JS, embedded). |
| `src/NowPlaying.Widgets` | The widget of the Windows Widgets board: a COM server (`Program`, `ClassFactory`, `WidgetProvider`) that Windows starts while the widget is on the board; `AppLink` (what plays from the app's local server, its buttons back), `Card` (Adaptive Card template and data), `Artwork` (the pictures: blurred background, rounded cover, icons). `package/`: the sparse package (manifest, logos, picker previews). Windows App SDK widgets component, self-contained. |
| `tests/NowPlaying.Tests` | xUnit: Core, update feed, localization key parity. |
| `installer/NowPlaying.iss` | Inno Setup (English/French), .NET 10 Desktop Runtime check, closes the running app, registers the Widgets package (Windows 11, `Add-AppxPackage -AllowUnsigned`; kept across updates, removed at uninstall). |
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
  taskbar auto-hides. Transparency through DWM (`DwmExtendFrameIntoClientArea`, transparent composition background,
  no backdrop, no rounded corners nor border, `SeeThrough`): hardware rendering. `ThemeMode="None"` on it: the Fluent
  theme of the app window must not paint it.
- **Owned by the taskbar, in its own process**: while Start or search is open, Windows lifts the taskbar over every
  topmost window (measured: the taskbar leaves the z-order list of the desktop). An *owned* window (`GWLP_HWNDPARENT`
  = `Shell_TrayWnd`) stays above it; a *child* of the taskbar does not show at all. Owner and owned windows of two
  threads share their input: a hung widget could freeze the taskbar. So the widget runs in `NowPlaying.exe --taskbar`
  (`TaskbarClient`), which the app (`TaskbarHost`) starts, feeds (named pipe, one JSON line per change, the cover
  apart), pings every second from the widget's UI thread, ends after 2 s of silence and starts again (backoff). Each
  side reads and writes the pipe on threads of its own. The widget process: the window, the bars (its own
  `AudioSpectrum`), the volume (`AppVolume`); the commands go to the app; its menu is its own (it got the click, so
  its menu may take the focus and closes on a click elsewhere), above the taskbar. Explorer restarted: the owned
  window dies with the taskbar, the process quits, the app starts another one.
- **The floating widget**: a top-level window in the app (tied to nothing). Frosted glass by a second window just
  under it (`FloatingGlass`): the compositor's `HostBackdropBrush` (`DWMWA_USE_HOSTBACKDROPBRUSH`, a
  `DesktopWindowTarget`, a rounded `CompositionGeometricClip`), on also while inactive, any corner radius, no
  shadow of Windows. Its shadow: a third, click-through window (`FloatingShadow`, layered, drawn once). The blur
  strength is Windows'; the opacity is the veil's.
- **Widgets board**: Windows App SDK widget provider (`Microsoft.WindowsAppSDK.Widgets`, self-contained: only
  `Microsoft.Windows.Widgets.dll` ships), a COM `ExeServer` declared by a sparse unsigned package (publisher OID
  `2.25.311729368913984317654407730594956997722`, `Add-AppxPackage -ExternalLocation <install folder>
  -AllowUnsigned`, as FolderTools). It reads the app's local server (`/events?layout=widget`, `/cover`, POST
  `/api/playpause|next|previous` with an `X-NowPlaying` header: a web page cannot send one without CORS asking first)
  and launches NowPlaying through Explorer (not with the package's identity). The card: `$host.widgetSize` and
  `$host.hostTheme`; the pictures are data URIs made by `Artwork`.
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
  Off by default (`OverlayEnabled`): the pages answer 404, the server keeps running for the Widgets board. In OBS,
  "Glass" is only a veil: a Browser Source is drawn alone, it cannot blur the scene behind it.
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
- Blur of an inactive window: the system backdrops (`DWMWA_SYSTEMBACKDROP_TYPE`) turn flat when the window is not
  active, and the floating widget never is. The undocumented accent acrylic works, but Windows only rounds such a
  window with its own corners (`DWMWCP_ROUND` / `ROUNDSMALL`), which always bring a shadow of Windows that nothing
  removes (tried: `DWMWA_NCRENDERING_POLICY`, margins 0, a layered window, `SetWindowRgn` even after showing, which is
  ignored). Hence `FloatingGlass`.
- Windows' shadow of a rounded window falls far below it ("trop verticale", the author): our own shadow is even.
- The Widgets board reads the package's pictures (icon, previews) from its **external location** (the install
  folder), not from the package: they ship next to the exe too (`NowPlaying.Widgets.csproj`). It writes the widget's
  name over the card's background in the color of its theme: hence the light and dark looks.
- A loose registration (`Add-AppxPackage -Register`) refuses the unsigned publisher OID (0x80073D2D): dev copies use
  `CN=NowPlaying`. A package update in place (higher version) keeps the widgets on the board; removing it drops them.
- Two NowPlaying processes run (the app and `--taskbar`): look for the app's windows in all of them (`dev.ps1`), and
  close the app by its process (`taskkill /im`), not by a window title (an Explorer window on a "NowPlaying" folder
  has it too).
- `Add-Type` in PowerShell passes `$null` strings as `""`: use `[NullString]::Value` for `FindWindow(null, …)`.

## To do

- [ ] The author installs 0.2.0 (the Widgets package needs the installer's admin prompt), tries the drag of the
      floating widget, OBS itself, Spotify.
- [ ] Later: like (Spotify), the taskbars of the other screens, spectrum of the player only (process loopback of
      Windows 11).
