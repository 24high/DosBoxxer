# DosBoxxer

A modern, cross-platform game launcher for DOSBox. Manage your DOS game library, enrich it with
metadata and cover art from online databases (MobyGames, IGDB or RAWG), and start any game with a
single click — on **Windows, Linux and macOS** from one code base.

Built with .NET 8 and [Avalonia UI](https://avaloniaui.net/), MVVM throughout, SQLite for the
library, and a light Fluent/Metro inspired theme.

---

## Table of contents

- [Screenshots](#screenshots)
- [Features](#features)
- [Requirements](#requirements)
- [Building](#building)
- [Running](#running)
  - [Windows](#windows)
  - [Linux](#linux)
  - [macOS](#macos)
- [Configuring DOSBox](#configuring-dosbox)
- [Configuring the metadata provider](#configuring-the-metadata-provider)
- [How a game is started](#how-a-game-is-started)
- [Data directories](#data-directories)
- [Database](#database)
- [Localization](#localization)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Tests](#tests)
- [Publishing](#publishing)
- [Troubleshooting](#troubleshooting)
- [Project structure](#project-structure)
- [Third-party licences](#third-party-licences)

---

## Screenshots

The repository ships without binary screenshots. To produce your own for documentation:

1. Start the application (see [Running](#running)).
2. Add two or three games so the library grid is populated.
3. Capture the window with your platform's screenshot tool
   (Windows: <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>, macOS:
   <kbd>Cmd</kbd>+<kbd>Shift</kbd>+<kbd>4</kbd> then <kbd>Space</kbd>, GNOME:
   <kbd>Alt</kbd>+<kbd>Print</kbd>).
4. Save them as `docs/screenshots/library.png`, `docs/screenshots/details.png` and
   `docs/screenshots/wizard.png`, then reference them here.

The main window is laid out in three columns:

```
┌──────────────┬──────────────────────────────────────┬───────────────────┐
│  Categories  │  Toolbar: search · sort · size · +   │                   │
│              ├──────────────────────────────────────┤   Game details    │
│  All Games   │                                      │                   │
│  Favorites   │   ┌──────┐ ┌──────┐ ┌──────┐         │   Cover           │
│  Recent      │   │cover │ │cover │ │cover │         │   Title / genres  │
│              │   ├──────┤ ├──────┤ ├──────┤         │   [ PLAY ]        │
│  Action      │   │ DOOM │ │ KEEN │ │ CIV  │         │   Facts           │
│  Adventure   │   └──────┘ └──────┘ └──────┘         │   Description     │
│  …           │                                      │   Screenshots     │
│              │                                      │   Statistics      │
├──────────────┴──────────────────────────────────────┴───────────────────┤
│  Status bar                                                             │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Features

**Library**

- Cover-based grid with a consistent 3:4 aspect ratio; images are never distorted
- Fallback chain per game: box art → first screenshot → generated initials placeholder
- Three cover sizes, selectable from the toolbar
- Category column: *All Games*, *Favorites*, *Recently Played* and 15 normalised genres with
  per-genre counts
- Search across title, publisher, developer and release year, with 300 ms debouncing
- Sorting by title (A–Z / Z–A), year (asc/desc), date added, last played and total play time
- Empty state with a call to action instead of a blank grid
- Favorites and play statistics (launch count, total play time, last played)

**Adding games**

- Four-step wizard: folder → launch file → metadata → review
- Recursive scan for `.exe`, `.bat` and `.com` files including all sub folders
- Heuristic ranking that pushes `SETUP.EXE`, `INSTALL.BAT`, `CONFIG.EXE` and similar to the
  bottom — the user always confirms the starter explicitly
- Automatic title suggestion derived from the folder name
- Metadata lookup with a result list, or *Skip metadata* for fully manual entries
- Duplicate detection based on the launch file

**Metadata**

- Three interchangeable metadata providers (MobyGames, IGDB, RAWG) behind an `IGameMetadataProvider` abstraction, switchable at runtime
- Descriptions honour the language fallback: selected language → English → any available
- Cover art and screenshots are downloaded once and cached locally
- Genres from the provider are mapped onto stable, normalised genre IDs
- Manual edits are remembered per field and protected from later refreshes
- Refresh dialog with per-field selection and an explicit "overwrite manual edits" opt-in
- Works completely offline once games have been added

**DOSBox**

- Your base `dosbox.conf` is only ever read, never modified
- Per-game overrides for cycles, core, machine, memsize, scaler, aspect, fullscreen, output,
  mixer rate, Sound Blaster type, PC speaker, plus free-form config lines
- Generated configuration can be previewed before launching
- Process start via an argument vector — no shell, no command injection
- Exit code, start and end time are captured and fed into the play statistics

**General**

- 9 languages, switchable at runtime without restarting
- Light Fluent/Metro theme with hover, selection and focus states
- Keyboard navigation and screen-reader labels
- Structured logging that never records credentials
- Runs without administrator privileges

---

## Requirements

| | |
|---|---|
| **Build** | [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or newer |
| **Run** | .NET 8 runtime, or a self-contained publish (no runtime needed) |
| **Emulator** | [DOSBox](https://www.dosbox.com/) or [DOSBox Staging](https://dosbox-staging.github.io/) |
| **Metadata** | A free API key for MobyGames, IGDB or RAWG (optional; IGDB/RAWG are instant self-service) |

Linux additionally needs the usual desktop libraries Avalonia relies on
(`libx11`, `libice`, `libsm`, `libfontconfig1`); they are present on any normal desktop install.

---

## Building

```bash
git clone <repository-url> DosBoxxer
cd DosBoxxer
dotnet restore
dotnet build -c Release
```

Run the test suite:

```bash
dotnet test
```

---

## Running

```bash
dotnet run --project src/DosBoxxer.App
```

### Windows

Install DOSBox, then point the launcher at it in **Settings → DOSBox**, typically:

```
C:\Program Files (x86)\DOSBox-0.74-3\DOSBox.exe
```

The default configuration usually lives at:

```
%LOCALAPPDATA%\DOSBox\dosbox-0.74-3.conf
```

### Linux

Install DOSBox from your distribution:

```bash
sudo apt install dosbox          # Debian / Ubuntu
sudo dnf install dosbox          # Fedora
sudo pacman -S dosbox            # Arch
```

Typical paths:

```
Executable:  /usr/bin/dosbox
Base config: ~/.dosbox/dosbox-0.74-3.conf
```

For DOSBox Staging use `/usr/bin/dosbox-staging` and `~/.config/dosbox/dosbox-staging.conf`.

### macOS

Install DOSBox (for example via Homebrew, `brew install dosbox`, or the official `.dmg`).

When DOSBox is installed as an app bundle, select the **binary inside the bundle**, not the
bundle folder itself:

```
/Applications/DOSBox.app/Contents/MacOS/DOSBox
```

The file picker enters app bundles, so you can navigate there directly. The base configuration
is usually at `~/Library/Preferences/DOSBox <version> Preferences`.

---

## Configuring DOSBox

Open **Settings → DOSBox**:

| Setting | Meaning |
|---|---|
| **DOSBox executable** | Path to the emulator binary. Validated when saving. |
| **Default dosbox.conf** | Base configuration used for every game. Never modified. |
| **Additional arguments** | Passed to DOSBox as separate arguments. No shell is involved. |
| **Close DOSBox when the game exits** | Appends an `exit` command to the generated autoexec. |

Per-game overrides live in **Edit game → DOSBox** and **→ Advanced**. Empty fields inherit the
value from the base configuration. The **Preview configuration** button on the Advanced tab shows
exactly what will be written before you launch anything.

---

## Configuring the metadata provider

DosBoxxer supports three interchangeable online databases and ships **no credentials** for any of
them. Pick the active one in **Settings → Metadata provider** and enter its credentials below the
dropdown. The `IGameMetadataProvider` abstraction means the rest of the app never depends on which
one is active; the `ActiveMetadataProvider` facade routes at runtime, and cached cover art from
any provider keeps working even after you switch.

| Provider | Auth | How to get it | DOS coverage |
|---|---|---|---|
| **MobyGames** | API key | Requested from MobyGames (manual, free) | Very good |
| **IGDB** | Client ID + Client Secret | **Instant, free** — register an app at [dev.twitch.tv](https://dev.twitch.tv/console/apps) | Very good |
| **RAWG** | API key | **Instant, free** — sign up at [rawg.io](https://rawg.io/apidocs) | Thinner for DOS; often modern cover art |

**IGDB is the recommended free option**: it is the only one with instant, self-service access
*and* good DOS coverage. IGDB uses the Twitch OAuth client-credentials flow — the launcher
exchanges your Client ID and Secret for a bearer token automatically and caches it.

Each provider restricts its search to the DOS platform by a configurable platform id
(MobyGames `2`, IGDB `13`, RAWG has no dedicated DOS platform so its filter is left empty). All
providers cache searches and game records on disk, enforce a per-provider minimum request
interval (MobyGames one request / 10 s, IGDB ~4 / s, RAWG conservative), retry only transient
failures with backoff, and honour HTTP 429.

Use **Test connection** to verify the active provider's credentials.

> **Security:** API keys and secrets are never written to `settings.json`. They are stored
> separately, encrypted with AES-GCM, in `secrets.dat` (see [Data directories](#data-directories)).
> Request URLs and errors are scrubbed before being logged.

> **Note on ScreenScraper:** an earlier `ScreenScraperMetadataProvider` also exists in the code
> (behind the same abstraction) but is not wired into the provider dropdown. ScreenScraper
> requires developer API credentials (`devid`/`devpassword`) that are granted manually on their
> forum; it was superseded here by the three providers above.

---

## How a game is started

Pressing **Play** performs these steps:

1. Load the base `dosbox.conf` (read-only).
2. Apply the per-game overrides.
3. Append a clearly delimited block to `[autoexec]` — creating the section when it does not
   exist, and preserving any commands that are already there.
4. Write the result to `<data>/temp/game-<id>.conf`.
5. Start DOSBox with `-conf <that file>` plus your additional arguments.
6. Wait for the process, then record exit code, duration, launch count and last-played date.

Given a game directory `/home/user/games/doom` and the starter
`/home/user/games/doom/BIN/DOOM.EXE`, the generated block is:

```ini
[autoexec]
# ... commands that were already in your base config stay here ...

REM --- DOSBox Launcher generated commands ---
mount c "/home/user/games/doom"
c:
cd BIN
DOOM.EXE
REM --- End DOSBox Launcher generated commands ---
```

On Windows the same game produces `mount c "D:\DOSGames\DOOM"` — only the `mount` command uses a
host path. Everything after it is a DOS path relative to the mounted drive.

> **8.3 file names:** DOSBox emulates a DOS file system, which only supports 8.3 names. Longer
> folder names are converted to their shortened form (`LongDirectoryName` → `LONGDI~1`) and the
> launcher logs a warning listing every name it had to shorten. If a game does not start, check
> that warning first — renaming the folder to an 8.3-compatible name is the reliable fix.

---

## Data directories

Nothing is written next to the executable. All paths come from a platform abstraction:

| Platform | Location |
|---|---|
| Windows | `%APPDATA%\DosBoxxer` |
| macOS | `~/Library/Application Support/DosBoxxer` |
| Linux | `$XDG_DATA_HOME/DosBoxxer` or `~/.local/share/DosBoxxer` |

```
DosBoxxer/
├── database/
│   └── library.db          SQLite library
├── cache/
│   ├── covers/             downloaded box art
│   ├── screenshots/        downloaded screenshots
│   └── metadata/           cached provider responses (JSON)
├── temp/
│   └── game-<id>.conf      generated DOSBox configuration per game
├── logs/
│   └── dosboxxer-<date>.log
├── settings.json           non-sensitive settings
├── secrets.dat             AES-GCM encrypted credentials
└── secrets.key             key file (mode 0600 on Unix)
```

**Settings → Storage** shows the cache size and offers *Open data folder* and *Clear cache*.
Clearing the cache only ever touches directories owned by the launcher.

---

## Database

SQLite via `Microsoft.Data.Sqlite`, with hand-written SQL and `PRAGMA user_version` migrations —
no ORM, no migration tooling, deterministic schema.

| Table | Purpose |
|---|---|
| `games` | One row per library entry |
| `genres` | The 15 normalised genres (seeded on startup) |
| `game_genres` | Many-to-many between games and genres |
| `screenshots` | Cached screenshot paths, ordered |
| `game_dosbox_settings` | Per-game DOSBox overrides |

Genres are stored as **stable numeric IDs**, never as localized display names, so switching the
application language never touches the database. Child rows cascade on delete.

**Removing a game never deletes the game's own files.** Only the database entry is removed, plus —
if you tick the checkbox — the cached cover and screenshots.

---

## Localization

Nine languages, all complete:

English · Deutsch · Français · Italiano · Español · Русский · 简体中文 · 日本語 · हिन्दी

The language can be changed in **Settings → Language** and applies **immediately**, without a
restart: every UI string binds to a cached `LocalizedString` object that raises
`PropertyChanged` when the language changes.

Translations are UTF-8 JSON files embedded in `DosBoxxer.Core`:

```
src/DosBoxxer.Core/Localization/Strings/Strings.<code>.json
```

To add a language:

1. Copy `Strings.en.json` to `Strings.<code>.json` and translate the values (never the keys).
2. Add the language to `SupportedLanguages` in `LocalizationService.cs`.
3. Run `dotnet test` — the suite fails if any language is missing keys, has unknown keys, has
   empty values, or uses a different number of `{0}`-style placeholders than English.

> The `EmbeddedResource` entry uses `WithCulture=false`. Without it MSBuild would mistake
> `Strings.de.json` for a satellite culture resource and move it out of the main assembly.

---

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| <kbd>Enter</kbd> | Start the selected game (or show details, depending on the setting) |
| <kbd>Delete</kbd> | Remove the selected game, with confirmation |
| <kbd>Ctrl</kbd>+<kbd>F</kbd> | Focus the search box |
| <kbd>Ctrl</kbd>+<kbd>N</kbd> | Add game |
| <kbd>F5</kbd> | Reload the library |
| <kbd>Esc</kbd> | Close the dialog / dismiss the error banner |
| <kbd>←</kbd> <kbd>→</kbd> | Previous / next screenshot in the lightbox |
| <kbd>Tab</kbd> / arrows | Standard focus and grid navigation |

---

## Tests

```bash
dotnet test
```

250 tests, none of which require any provider account, a network connection or an installed
DOSBox. Coverage includes:

- Recursive `.exe` / `.bat` / `.com` discovery, installer ranking
- Host path handling, relative launch paths, paths containing spaces
- DOS 8.3 name conversion and mount-path quoting
- DOSBox config generation: existing `[autoexec]`, missing `[autoexec]`, duplicate sections,
  per-game overrides, free-form config lines, base config left untouched
- **End-to-end launch** against a stub executable: `-conf` handover, argument-vector safety
  (shell metacharacters stay literal), exit code and play statistics
- Genre mapping from provider labels, including compound labels
- Metadata merge: field selection, protection of manual edits, no accidental clearing
- Response parsing for all three providers (ScreenScraper, MobyGames, IGDB, RAWG) from recorded
  fixtures, including inconsistent shapes (single object instead of array, numeric IDs, empty
  strings, HTML descriptions, Unix-timestamp dates) and rejection of media URLs from foreign hosts
- Provider selection: the `ActiveMetadataProvider` routes to the configured provider and the
  `CompositeMediaHttpClient` routes each media URL to the provider that owns its host
- Rate limiting: the shared `RateLimiter` enforces both minimum spacing and a rolling-window
  request cap (verified with a virtual clock), and serialises concurrent callers
- Bulk import: directory enumeration (immediate sub folders, hidden skipped, sorted)
- Settings serialization, secret encryption at rest, data-directory creation
- Repository round-trips, cascade deletes, play-session accumulation, restart persistence
- Translation completeness across all nine languages

---

## Publishing

```bash
# Windows
dotnet publish src/DosBoxxer.App -c Release -r win-x64   --self-contained true -o publish/win-x64
dotnet publish src/DosBoxxer.App -c Release -r win-arm64 --self-contained true -o publish/win-arm64

# Linux
dotnet publish src/DosBoxxer.App -c Release -r linux-x64   --self-contained true -o publish/linux-x64
dotnet publish src/DosBoxxer.App -c Release -r linux-arm64 --self-contained true -o publish/linux-arm64

# macOS
dotnet publish src/DosBoxxer.App -c Release -r osx-x64   --self-contained true -o publish/osx-x64
dotnet publish src/DosBoxxer.App -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
```

Add `-p:PublishSingleFile=true` for a single executable. Do **not** enable trimming without
testing: the ViewModels are bound reflectively, and `System.Text.Json` deserializes the settings
and cache files.

### Packaging (prepared, not included)

The layout keeps installer work independent of the application:

- **Windows** — point an Inno Setup or WiX project at `publish/win-x64`.
- **Linux `.deb` / `.rpm`** — install the publish output to `/opt/dosboxxer`, add a launcher
  script in `/usr/bin` and a `.desktop` entry; the app already writes exclusively to
  `$XDG_DATA_HOME`, so no post-install step is required.
- **macOS `.app` / `.dmg`** — wrap `publish/osx-arm64` in a bundle with an `Info.plist`
  (`CFBundleExecutable=DosBoxxer`, `LSMinimumSystemVersion=11.0`), then `hdiutil create`.
  Distribution outside your own machine additionally requires signing and notarisation.

---

## Troubleshooting

**"No DOSBox executable is configured"**
Set the path in Settings → DOSBox. On macOS pick the binary inside the `.app` bundle, not the
bundle itself.

**The game starts but immediately drops to the `C:\>` prompt**
Usually an 8.3 name problem. Check the log for `had to be shortened to DOS 8.3 form` and rename
the offending folder, or point the launcher at a starter closer to the game root.

**"The API key was rejected"**
Check the credentials for the active provider in Settings → Metadata provider. For IGDB, both the
Client ID and the Client Secret must be from the same Twitch application. Use *Test connection* to
confirm before searching.

**"The request limit has been reached"**
MobyGames (one request / 10 s) and RAWG have quotas; IGDB allows ~4 requests per second. The
launcher caches searches and game records and never issues parallel requests, so this normally
only appears during a large batch of additions — wait a moment and retry.

**No cover art appears**
Not every DOS game has box art in every database. The grid then falls back to a screenshot, and
finally to a generated placeholder. Switching the provider (Settings → Metadata provider) may find
art the other one lacks, or you can set a cover manually via right-click → *Change cover*.

**The library is slow with very many games**
The grid uses a wrapping panel rather than a virtualising one, so all tiles are realised. Image
memory is bounded — thumbnails are decoded at display size and held in an LRU cache of 400
bitmaps — but with several thousand games, expect the *initial* layout pass to take a moment.
Choosing the small cover size helps.

**Where are the logs?**
`<data directory>/logs/dosboxxer-<date>.log`, reachable via Settings → Storage → *Open data
folder*. Logs are kept for 14 days and never contain passwords.

---

## Project structure

```
DosBoxxer.sln
├── src/DosBoxxer.Core/            no UI dependency — fully unit-testable
│   ├── Models/                    Game, GenreKey, AppSettings, metadata models
│   ├── Abstractions/              ports: repository, provider, launcher, paths, …
│   ├── Infrastructure/
│   │   ├── Database/              connection factory, schema migrations
│   │   ├── Repositories/          SQLite game repository
│   │   ├── DosBox/                config document, config builder, launcher
│   │   ├── MobyGames/             HTTP client, DTOs, provider adapter, genre mapper
│   │   ├── Igdb/                  HTTP client (Twitch OAuth), DTOs, provider adapter
│   │   ├── Rawg/                  HTTP client, DTOs, provider adapter
│   │   ├── ScreenScraper/         HTTP client, DTOs, provider adapter (dormant)
│   │   ├── ActiveMetadataProvider + CompositeMediaHttpClient (provider selection)
│   │   ├── Media/                 media downloader, metadata cache
│   │   └── Settings/              settings service, encrypted secret store
│   ├── Localization/              service + embedded JSON translations
│   └── Helpers/                   path, DOS path, title, log sanitizer, arg splitter
├── src/DosBoxxer.App/             Avalonia UI
│   ├── ViewModels/                MVVM, CommunityToolkit.Mvvm
│   ├── Views/                     main window and dialogs
│   ├── Styles/                    design tokens, icons, control styles
│   ├── Services/                  dialog service, image loader, file logger
│   └── Converters/, Localization/
└── tests/DosBoxxer.Tests/         xUnit
```

Layering is strictly one-directional:

```
Views  →  ViewModels  →  Application services  →  Infrastructure
```

The UI never references ScreenScraper types; it only sees `IGameMetadataProvider` and the
launcher's own metadata models. API DTO, database entity and ViewModel are three separate model
layers. Code-behind is limited to what Avalonia genuinely requires — focusing a control,
translating gestures into commands, and persisting window geometry.

The `Game` entity carries an `EmulatorProfile` column and the launcher is addressed through an
interface, so a second emulator backend can be added later without a schema migration — but no
speculative abstraction beyond that has been built.

---

## Third-party licences

| Component | Licence |
|---|---|
| [Avalonia UI](https://github.com/AvaloniaUI/Avalonia) | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | MIT |
| [Microsoft.Extensions.*](https://github.com/dotnet/runtime) | MIT |
| [Inter font](https://github.com/rsms/inter) (via `Avalonia.Fonts.Inter`) | SIL Open Font License 1.1 |
| SQLite | Public domain |

**Icons:** every icon in `src/DosBoxxer.App/Styles/Icons.axaml` was drawn for this project on a
24×24 grid and is covered by this project's licence. No third-party icon font or SVG pack is
bundled, so there is nothing additional to attribute.

**Metadata providers:** DosBoxxer is an API *client* for
[MobyGames](https://www.mobygames.com/info/api/), [IGDB](https://api-docs.igdb.com/) and
[RAWG](https://rawg.io/apidocs) (and retains a dormant ScreenScraper client). Game metadata and
artwork remain the property of their respective rights holders and are subject to each provider's
terms of use. No credentials are distributed with this software, and every provider's use is
non-commercial unless you have arranged otherwise with them.

DOSBox itself is **not** bundled — it is licensed under the GPL and must be installed separately.
