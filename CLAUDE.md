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
(about 3 % more while playing). Measured on October 24 with 0.3.1 (Release, installed), each page 10 s
(`.local\measure-pages.ps1`): nothing playing, window closed 0.0 %; playing (the silent fake player and a YouTube
stream), floating widget on, window closed 0.9 % (+ 0.6 % for the widget process), open 1.4–6.4 % depending on the
page (Floating widget page 2.0 %). The Debug build, same case: 1.4–6.9 %. A measure right after UI Automation walked
the whole window once read 32 %: let the window rest before measuring.

**Git**: commits only in the author's name (repository-local identity `QuentinPerou`, his GitHub noreply address),
**no `Co-Authored-By` line**. **Never push** without his explicit OK: he pushes from his own PC.

## Project status (October 21, 2026)

**Version 0.3.2** (October 24, 2026; installed on the shop PC over 0.3.1 at his request, silently, settings kept;
committed with tag `v0.3.2`, not pushed): the **real spectrum listens to the player
shown only** (he agreed: "ça me paraît cohérent"), not Discord, a game or a notification: Windows' process loopback
(`ProcessLoopback`, COM declarations of our own, NAudio has none) on the player's sound process (`AppVolume.SoundProcess`:
its audio session; for a browser, the process it plays through, so all its tabs), followed when the player changes
(`AudioSpectrum.SetPlayer`, from the app and the widget process); the whole output as before when the player has made
no sound yet (looked for again every 3 s) or on a Windows older than build 20348. Checked: the silent fake player
shown while Chrome played, the bars sent to OBS stayed at 0.00; Chrome shown, up to 0.92. Also checked: the "32 %" of
October 22 does not reproduce (see Performance). `.local\cs\process-loopback.cs` listens to one process alone.

**Version 0.3.1** (October 24, 2026; installed on the shop PC over 0.3.0 at his request, silently, settings kept;
committed with tag `v0.3.1`, not pushed): two history entries showed the Chrome
logo: Chrome gives Windows its logo first, the page's picture a moment later, and the history kept the first one; the
last cover given for a track now stays (`PlayHistory.Heard`, `HistoryStore` reads a cover again at each new cover
revision). "History…" added to the menus of both widgets and of the notification icon (the taskbar widget asks the
app with `open history`). Checked: the tray menu shows it, choosing it by UI Automation opens the History page.

**Version 0.3.0** (October 24, 2026; installed on the shop PC over 0.2.4 at his request, silently, settings kept;
committed with tag `v0.3.0`, not pushed): four features he chose among ideas (mock-up `.local/maquettes/idees-0-3.png`, approved), all optional
(the section of the text file renamed by him "Informations dans un fichier texte"; he declined a click on a history
row searching the track: Windows gives no link):

- the **OBS overlay "on each new track"** (each layout its own): it appears at a new track (and on opening), stays
  `AppearSeconds` (8 s), fades out; optionally again when playing resumes. Done by the page (`overlay.js`, a timer:
  a page in the background draws no frames). Checked in a browser: shown at load, hidden at 8.2 s.
- a **text file for OBS** (`Documents\NowPlaying\now-playing.txt`, "Title – Artist" or other, empty when nothing
  plays, written only on change through a temporary file: `TrackFile`). Checked: on, the line; off, empty.
- a **History** page (second in the menu): tracks heard by day, a copy button each; `PlayHistory` (Core: one entry per
  track, not per pause; 30 days, 1000 entries) saved by `HistoryStore` in `history.json`, the covers as 64 px JPEGs
  in `covers\` (read only when the page shows them). And **"Copy title – artist"** in both widgets' menus.
- **Click on the cover** brings the player to the front (`PlayerWindow`: its window last in front, restored if
  minimized; a Store app opened again from `shell:AppsFolder`), a setting of each widget, off by default. Not tried
  by a click (it would take the author's focus).
- The taskbar widget on **every screen** (`WidgetStyle.AllScreens`): one widget per `Shell_SecondaryTrayWnd`, owned by
  it, made and closed as screens come and go (`TaskbarClient.SyncScreens`); near the clock of a secondary taskbar:
  left of its `SystemTrayIcon` (it has no `TrayNotifyWnd`). Checked on the shop PC's second screen: after its weather,
  or 8 px from its clock; gone again on "main screen".

**Version 0.2.4** (October 24, 2026; installed on the shop PC over 0.2.3 at his request, silently, settings kept;
committed with tag `v0.2.4`, not pushed): he hid Chrome on the Sources page and
still saw its track. The 4 s kept after a session ends applied to a player he hides himself too: a player hidden now
goes at once, and its track is not kept greyed either (`App.ApplySources`). Found with it: the window was not told when
those 4 s ended (it follows `App.ShownChanged` now, not `Media.Changed`), and the rows of the Sources page did not
follow what plays (`ShowPlayers` on each change, drawn again only when a line changes). Checked by UI Automation:
Chrome hidden, the widget gone within 0.5 s, the top line and the row updated; shown again, back at once.

**Version 0.2.3** (October 24, 2026; installed on the shop PC over 0.2.2 at his request, silently, settings kept;
committed with tag `v0.2.3`, not pushed): on a YouTube playlist in Chrome the widget
vanished and came back at each new video (Chrome ends its media session with the video and opens another one): what
played now stays shown 4 s (`App.ShownMedia`, a one-shot timer only then), for the widgets, the overlay and the window.
Checked: the fake player ended, the widget stayed 4 s, then hid. He also found previous / next not working on that
playlist: Windows' own flyout greyed them too, so Chrome declared them unavailable at that moment; a minute later
Chrome declared both available (`.local\cs\sessions.cs` now prints the buttons). The widget follows what Chrome
declares; nothing more to do from the app.

**Version 0.2.2** (October 22, 2026; installed on the shop PC over 0.2.1 at his request, silently, settings kept,
the Widgets package kept; he found it "ok"; committed with tag `v0.2.2`, not pushed), all asked by the author while
trying 0.2.1. He found the visualizer setting
did nothing, on the floating widget and for OBS. Two causes: the floating widget and the previews of the window keep
the very style object the window changes, so they never saw the new mode until the next pause (`WidgetView` and
`OverlayPreview` now take or give back the spectrum at each update, after what the style asks); and his music, played
quietly (peak −33 dBFS), gave nearly flat bars on the fixed scale: the spectrum now has an automatic gain (up to
24 dB, `SpectrumAnalyzer`). Checked by UI Automation: the floating widget's preview switches at once, its bars move in
both modes; the OBS bar receives moving levels (the overlay turned on for the test, then off again as he had it).
Asked the same morning: each look of the floating widget starts from its own opacity and shadow, given back when he
picks it (`WidgetStyle.FloatingLook`: Glass 40 % with a shadow, Transparent 55 % without, Cover and Custom 90 % with
one); a double click on these sliders gives back the look's value. Checked by UI Automation through the four looks.
Then, also asked by him: a **Blur** slider for the floating widget (0 % sharp to 100 %, Windows' blur, the strongest
there is: it mixes the sharp background in, `FloatingGlass.Strength`; Glass 100 %, the others 0 % by default; a 0.2.1
settings file gets 100 % if it was on Glass), and its **text dark or light after what is behind it**, read on a thin
band around it just outside its shadow (`FloatingWidget.ReadBackdrop`: when it shows or moves, and every 1.5 s only
while something plays), the color or veil of the style counted over it (`SurfaceColors.Resolve(…, backdrop)`).
Checked: pictures of the real widget over his white web page at 0, 33, 66 and 100 % (dark text, the blur mixed in);
CPU with the window closed 3.0–4.7 % of one core (Debug, both widgets, spectrum), as before. With the window open on
the Floating widget page the Debug build used about 32 % once; not reproduced on October 24 (see Performance).
Then he found the glass far from Apple's "Liquid Glass" (lens distortion, chromatic aberration on the edges). Told
him: Windows only hands apps a blurred copy of what is behind, so no lens of the sharp background without capturing
the screen all the time (costly, and the widget would vanish from screenshots and OBS). Four drawn variants shown
(`.local/maquettes/liquid-glass.html|png`); he chose **C**: a clear veil tinted after what is behind (white over a
light page, dark over a dark one; Glass now 25 % by default), light on the top of the veil, a bright rim (top left)
and an inner glow, and colored fringes on the edges. The fringes first had fixed colors (pink, cyan, orange, blue):
he found them odd over his white page and asked that they come from what is behind: each side now takes the color
read behind it, vivid, as opaque as it is colorful (none over white, grey or black: `WidgetView.ShowBehind`). The
edges are a cached layer (`BitmapCache`) over the widget, drawn for Glass and Transparent and any blurred look
(`App.GlassLook`). Checked by pictures of the real widget over his page and of the window's preview; CPU with the
window closed 3.6–6.6 % (Debug). README picture: the floating widget of `hero.html` drawn with the new glass.

**Version 0.2.1** (installed on the shop PC over 0.2.0 at his request, silently as the updater does, settings kept,
the Widgets package kept; commit `059fee5`, tag `v0.2.1`, not pushed), asked by the author while trying 0.2.0:

- a **Sources** page, first in the menu (mock-up `.local/maquettes/page-sources.png`, approved): the player shown is
  the last one started (Automatic) or the first of an order of priority (By priority, with "prefer what plays");
  how long a choice by right click lasts (until another player starts, or until another choice); the players seen
  (remembered in the settings with their last day), sorted by arrows, hidden by a switch (never shown, not offered
  by right click). Checked by UI Automation with Chrome and the fake player: priority, hiding.
- **Menus drawn as Windows'** (`Menus.xaml`): in Windows' mode, as the taskbar (his taskbar is light, his app window
  dark), rounded, no focus frame. Checked by a picture of the tray menu (opened by its message); the widget menus
  share the drawing, not opened by Claude.
- Firefox listed by its name (Windows' name of the app, `AppNames`), not its hash.
- Fixes: "EN DIRECT" on a normal Firefox video (Firefox tells Windows no timeline there, and the estimated position
  went on from 0: "live" now needs a position reported by the player, `Timeline.Kind`; checked on his video, the
  overlay state says `live: false`); the overlay showed the cover of the previous player (cover revisions counted per player: `/cover?r=1` for
  two players); the switches of the window did not follow a change from a menu (floating widget hidden); a switch
  toggled by UI Automation changed nothing; Quit moved from About to General.

**Version 0.2.0** (installed on the shop PC over 0.1.3, settings kept: the Widgets package registered, the widget
process owned by the taskbar, start with Windows kept; commit `c83a740`, tag `v0.2.0`, not pushed), all asked by the
author the same afternoon, tried by him as a dev build first:

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
| `src/NowPlaying.Core` | Pure, tested logic: `SourcePicker` (which player to show, the rules of the Sources page), `PlayHistory` (the tracks heard), `TrackText` (a track as one line), `Timeline` (position between reports, "3:34"), `DominantColor` (color of a cover, readable under white text), `SpectrumAnalyzer` (Hann + FFT + log bands + smoothing) and `DecorativeBars`, `Styles` (`WidgetStyle`, `OverlayStyle`, enums, ranges), `SurfaceColors` (background / text / track from a style), `Rgb`, `AppIdentity` (player names, process matching), `SemanticVersion`, `UpdateFeed`. |
| `src/NowPlaying` | WPF .NET 10. `App` (single instance, tray, widgets, overlay, commands; `--taskbar`: the widget process), `TaskbarWidget` (the window over the taskbar, in the widget process), `FloatingWidget` + `FloatingGlass` (its blur) + `FloatingShadow`, `MainWindow` (pages built in code), `PopupMenu` + `Menus.xaml` (tray and widget menus), `UpdateBar`, `Localization.cs`; `Controls/`: `WidgetView` (the widget, also the previews), `BarsView`, `OverlayPreview`, `StyleEditor`, `Ui` (cards, `Segmented`); `Services/`: `MediaSessions`, `AppNames`, `AppVolume`, `PlayerWindow`, `HistoryStore`, `TrackFile`, `AudioSpectrum`, `OverlayServer`, `TaskbarHost` (app side) + `TaskbarClient` (widget process) + `TaskbarLink` (`LineChannel`, the messages), `SeeThrough`, `TrayHost`, `AppSettings`, `WindowsSettings`, `SingleInstance`, `MicaBackdrop`, `MemoryTrim`, `Updater`, `NativeMethods`; `Overlay/`: the overlay page (HTML, CSS, JS, embedded). |
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
- **Which player**: `SourcePicker`, the author's rule (the last one started); a player picked in the menu stays until
  another one starts. The Sources page sets a `SourcePolicy`: an order of priority (`AppSettings.Players`, by app id:
  "chrome#2" ranks as "chrome"), prefer what plays, a choice that stays until another one, hidden players (out of the
  widgets' menus too, `MediaSessions.Sources`; all of them in `MediaSessions.Players`).
- **Menus**: the tray and widget menus have their own drawing (`Menus.xaml`, set by `PopupMenu`), in Windows' mode
  (`SystemUsesLightTheme`, the taskbar's) and not the app's theme, as Windows' own menus of the taskbar; the widget
  process loads no Fluent theme for them.
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
  strength is Windows' at most: the Blur slider sets the opacity of the glass layer (the sharp background mixes in);
  the opacity is the veil's. Its text is dark or light after a band read around it (see "Readable text").
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
  text by contrast (`Rgb.IsLight`). The floating widget reads a band around itself, outside its shadow (under it the
  screen shows the widget itself; excluding it from capture would hide it from screenshots and OBS too), and counts
  its color or veil over that mean color (`SurfaceColors.Seen`).
- **Its place**: the Widgets (weather) and Start buttons are drawn by Explorer without windows: found by UI
  Automation (`WidgetsButton`, `StartButton`), on a pool thread, again on `WM_SETTINGCHANGE` and when Explorer
  restarts. Left: after the weather; no room before Start (or icons aligned left): near the clock, left of
  `TrayNotifyWnd` (still a real window with its rectangle on Windows 11).
- **Volume of the app**: Core Audio sessions of the default output (NAudio), matched to the player by
  `AppIdentity.Matches` (exe name, Store package family, Firefox's hash). 2 % a notch.
- **Spectrum**: the sound of the player shown only, by Windows' process loopback (`ProcessLoopback`: the virtual
  device `VAD\Process_Loopback` activated for the player's sound process and its children, 16-bit stereo 44.1 kHz);
  else (no sound from the player yet, Windows before build 20348) a WASAPI loopback of the default output (NAudio).
  Only while bars are shown and something plays (`AudioSpectrum.Acquire/Release`); follows the player
  (`SetPlayer`) and a change of output device; readers of one frame share one computation; silence lets the bars
  fall; an automatic gain (`SpectrumAnalyzer`).
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
- A cover's address (`/cover?r=N`) must differ from one player to another: `CoverRevision` is counted over all the
  players, else a browser source keeps the previous player's picture.
- WPF raises no `Click` when UI Automation toggles a CheckBox (Narrator, the tests): `Ui.Toggle` listens to
  Checked / Unchecked, and a change shown from elsewhere goes through `Ui.ShowToggle` (no call back).
- Firefox names its media sessions with a hash (8 to 16 hex characters) and, without metadata from the site, sends
  "Firefox lit un contenu multimédia" as the title. Names come from Windows (`shell:AppsFolder\<app id>`). On some
  sites it tells no timeline at all (length and position 0): never judge "live" from `Timeline.Extrapolate`, which
  goes on from 0. `dotnet run .local\cs\sessions.cs` prints each session's timeline.
- A browser gives Windows one media session (its last audible tab): two tabs of one browser are never two players.
- YouTube in Chrome: the session ends with each video and a new one opens a moment later (hence `App.ShownMedia`);
  previous / next are what the page declares, sometimes unavailable for a while (Windows' flyout greys them too).
- The window changes the style objects of the settings in place, and the floating widget and the previews keep those
  same objects: a view cannot find a change by comparing the new style with the one it keeps. Follow what the style
  asks at each update instead (`WidgetView.HoldSpectrum`). The taskbar widget gets a copy through the pipe.
- The spectrum's loudness depends on the player's volume (loopback captures the mix): `.local\cs\loopback.cs`
  prints the level of the default output.
- A browser page in the background draws no frames (`requestAnimationFrame` stops): what must happen at a time on the
  overlay page (hide after a few seconds) uses a timer.
- To see a menu without the mouse: post the tray icon's callback (`WM_APP + 1`, lParam `WM_CONTEXTMENU`) to the
  hidden "NowPlaying" window, picture the popup's rectangle, post it again to close the menu.

## To do

- [ ] The author pushes from home: the repository has no remote yet (the GitHub repository `quentinperou/NowPlaying`
      is to be created), nine commits and nine lightweight tags (`v0.1.3`, `v0.2.0` to `v0.2.4`, `v0.3.0` to
      `v0.3.2`) wait. Each `v*` tag pushed makes a GitHub release (the workflow).
- [ ] The author tries 0.3.2 (installed on the shop PC): the Sources page with real players, the widget menus (and
      "Copy title – artist"), the click on the cover, the drag of the floating widget, OBS itself (the overlay on each
      new track, the text file), Spotify, a real live stream in the widget.
- [ ] Later: like (Spotify).
