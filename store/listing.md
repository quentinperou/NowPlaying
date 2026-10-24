# Microsoft Store listing

What goes into Partner Center for **NowPlaying Widget** (Store ID `9N7FSJ3M6KGC`), page by page, ready to
paste. Kept here so that the next submissions start from it. The package: `dist\store\NowPlaying_<version>.0_x64.msix`
(`scripts\build-store.ps1`).

## Pricing and availability

- Markets: all. Visibility: public. Price: free (no trial, no add-ons).
- Publish: as soon as it passes certification.

## Properties

- Category: **Music**. Secondary category: **Personalization**.
- Does the app access, collect or transmit personal information? **No**.
- Privacy policy URL: `https://github.com/quentinperou/NowPlaying#privacy-policy`
- Website: `https://github.com/quentinperou/NowPlaying`
- Support contact info: `https://github.com/quentinperou/NowPlaying/issues`
- Product declarations: the defaults (install to alternate drives, OneDrive backups); nothing else.
- System requirements: none.

## Age ratings (IARC questionnaire)

Category: utility / productivity / other. Every question **No**: no violence, no user interaction or shared content,
no location shared, no purchases, no unrestricted web access. Expected rating: 3+ (PEGI 3).

## Packages

Upload the `.msix`. Device family: Windows 10/11 Desktop (the package needs Windows 11).

## Submission options

**Why do you need the runFullTrust capability?** (500 characters at most)

```
Desktop app (WPF, .NET) that shows what is playing in the taskbar. Full trust is needed to: read the media sessions of Windows from any app; show a widget window over the taskbar (Win32, UI Automation); set the volume of the app that plays and analyze its sound in memory for the visualizer (Core Audio, process loopback); serve an OBS overlay on localhost only; run the Widgets board provider (COM server) and a startup task. No data leaves the PC.
```

**Notes for certification**

```
No account, no sign-in. Play any media (YouTube in Edge, Spotify, Media Player): the widget shows the track in the taskbar; hover it for the controls, turn the mouse wheel for the volume of that app. The settings open from the Start menu (NowPlaying) or the notification area icon. The floating widget and the OBS overlay are off by default (Floating widget and OBS overlay pages).
```

## Store listing: English (en-us)

**Description**

```
What's playing, right in the Windows 11 taskbar, from any app: Spotify, YouTube or Deezer in a browser, Media Player, Apple Music and any app that tells Windows what it plays.

NowPlaying shows the cover, the title and the artist in a small widget next to Windows' weather or the clock. Hover it for the controls and the progress; turn the mouse wheel to set the volume of the app that plays, not of the whole PC. Its bars move to the real sound of the player.

Also on the desktop as a floating glass widget, in the Windows Widgets board, and for streamers as an overlay for OBS Studio, with a text file of the track. Each widget and each overlay has its own style.

Light on your PC: when nothing plays, it uses nothing. Nothing you listen to leaves your computer.

Free and open source (MIT).
```

**Product features** (one per field)

```
The cover, title and artist of what's playing, in the taskbar
Previous, play / pause, next and the progress on hover
Mouse wheel: the volume of the app that plays
A visualizer: the real spectrum of the player's sound, or an animation
Several players: the last one started, your order of priority, or your choice
A floating widget anywhere on the desktop, in frosted glass
A widget for the Windows Widgets board, in three sizes
An overlay for OBS Studio: a bar or a card, always shown or at each new track
A text file of the track, for OBS
Your own style for each widget and overlay: colors, glass, corners, font
A history of what played, on this PC only
Light or dark theme, English and French
Starts with Windows, uses nothing while nothing plays
```

**Short description**

```
What's playing, from any app, in the Windows 11 taskbar: the cover, the title, the controls and a visualizer. Also a floating widget, a Widgets board widget and an OBS overlay.
```

**Screenshots**: `.github/screenshot.png` (1560 × 1000).

**Search terms** (one per field)

```
now playing
taskbar widget
music widget
spotify
media controls
obs overlay
visualizer
```

**Copyright and trademark info**

```
Copyright (c) 2026 QuentinPerou. Spotify, YouTube, Deezer and OBS Studio are trademarks of their owners.
```

**Developed by**: `QuentinPerou`. **What's new in this version**: empty for the first submission.

## Store listing: French (fr-fr)

**Description**

```
Ce qui est en lecture, directement dans la barre des tâches de Windows 11, quelle que soit l'application : Spotify, YouTube ou Deezer dans un navigateur, Lecteur multimédia, Apple Music et toute application qui indique à Windows ce qu'elle joue.

NowPlaying affiche la pochette, le titre et l'artiste dans un petit widget, à côté de la météo de Windows ou de l'horloge. Survolez-le pour les commandes et la progression ; la molette règle le volume de l'application qui joue, pas celui de tout le PC. Ses barres suivent le vrai son du lecteur.

Aussi sur le bureau en widget flottant en verre, dans le tableau Widgets de Windows, et pour les streamers en overlay pour OBS Studio, avec un fichier texte du morceau. Chaque widget et chaque overlay a son propre style.

Léger pour votre PC : quand rien ne joue, il ne consomme rien. Rien de ce que vous écoutez ne quitte votre ordinateur.

Gratuit et open source (MIT).
```

**Fonctionnalités** (une par champ)

```
La pochette, le titre et l'artiste de ce qui joue, dans la barre des tâches
Précédent, lecture / pause, suivant et la progression au survol
Molette : le volume de l'application qui joue
Un visualiseur : le vrai spectre du son du lecteur, ou une animation
Plusieurs lecteurs : le dernier lancé, votre ordre de priorité, ou votre choix
Un widget flottant où vous voulez sur le bureau, en verre dépoli
Un widget pour le tableau Widgets de Windows, en trois tailles
Un overlay pour OBS Studio : une barre ou une carte, toujours visible ou à chaque nouveau morceau
Un fichier texte du morceau, pour OBS
Votre style pour chaque widget et overlay : couleurs, verre, coins, police
Un historique de ce qui a joué, sur ce PC seulement
Thème clair ou sombre, français et anglais
Démarre avec Windows, ne consomme rien quand rien ne joue
```

**Description courte**

```
Ce qui joue, depuis n'importe quelle application, dans la barre des tâches de Windows 11 : la pochette, le titre, les commandes et un visualiseur. Aussi en widget flottant, dans le tableau Widgets et en overlay OBS.
```

**Captures d'écran** : `.github/screenshot.fr.png` (1560 × 1000).

**Termes de recherche** (un par champ)

```
lecture en cours
widget barre des tâches
widget musique
spotify
contrôles multimédia
overlay obs
visualiseur
```

**Copyright et marques**

```
Copyright (c) 2026 QuentinPerou. Spotify, YouTube, Deezer et OBS Studio sont des marques de leurs propriétaires.
```

**Développé par** : `QuentinPerou`. **Nouveautés de cette version** : vide pour la première soumission.
