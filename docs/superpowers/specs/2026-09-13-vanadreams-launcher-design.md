# Vanadreams Launcher — design

Date: 2026-09-13. Status: approved for planning.

## Purpose

A launcher for Ashita v4 that gives Vanadreams players the thing the v3 launcher had and v4 does not: a window where you pick who you are, see whether the server is up and whether your client will be accepted, choose your addons and plugins from a list, and press Play. First run also sets Ashita up and walks a new player through installing the FFXI client correctly.

## Audience

Vanadreams players first. The launcher is Vanadreams branded, ships with the Vanadreams profile, and shows Vanadreams status. Underneath it is a general profile launcher: a player can add a profile for another server and the same window launches it.

## Scope of version 1

1. Profiles: list, select, edit, create, duplicate, delete. One profile is one Ashita boot ini.
2. Login and Play: remembered credentials, handed to xiloader at launch.
3. Client version check before Play, compared the way the server compares.
4. Live server status from the fairywitch.ca status route.
5. Setup on first run: download and unpack the Ashita v4 beta and xiloader, write the Vanadreams profile, import from a v3 install if one exists.
6. FFXI client guide: a pop-out, step-by-step window with pictures, links and checks for installing and updating the retail client and getting it through the firewall.
7. Addon picker: a catalogue of plugins and addons, install and update with one click, tick to enable, writes the startup script.

## Not in version 1

Installing the FFXI client itself. Creating the server account (that stays at the xiloader prompt, which the guide explains). Auto-updating the launcher. Any in-game UI (that is the `ui/` addon, its own design).

## Visual language

The brand is fairywitch.ca's Vanadreams door. The grammar is the Final Fantasy VII menu.

Tokens, taken from the site's stylesheet and used unchanged:

| Token | Value | Use |
| --- | --- | --- |
| night | `#0f0a17` | window ground |
| plum | `#231938` | box gradient top |
| velvet | `#33224a` | box gradient mid |
| gold | `#d6a74c` | border, buttons |
| gold-soft | `#f1cd78` | border highlight, eyebrows, cursor |
| cream | `#fff3d2` | text |
| mist | `rgba(255,242,211,0.68)` | secondary text |
| line | `rgba(241,205,120,0.22)` | pills, dividers |

Type: Georgia for display and menu items, Segoe UI for body, tabular figures for versions and sizes. Both are on every Windows machine that runs FFXI.

The FF7 box: a vertical gradient from plum through velvet to night, a 3 px bevelled border (gold-soft top and left, brass right and bottom), a 1 px dark inner line, a hard 1 px by 2 px text shadow under every character. The cursor is the pointing hand glyph in gold-soft with a soft glow, always on the selected item. Eyebrow labels are small caps in gold-soft with the four-point star. Semantic colour is separate from the accent: green for pass, amber for warning, red for fail.

The window is 1000 by 640, not resizable in v1, dark only. The night sky is a static starfield; the moon from the site sits small in the top right of the menu screen.

## Screens

The mockups are the artifact "Vanadreams Launcher" (three artboards). The chosen window is mockup A with mockup B's moon.

### Menu

Left box: the selected profile as a character card. Name, server, status pill, client check line, Ashita path and update date, loader version, addon count with pending updates, window mode and size, last played. Play and Edit profile buttons at the bottom of the card.

Right box: the command list. Play, Profiles, Addons, Setup, Settings, Discord, Exit. Up and down move the hand, Enter selects, Escape backs out. Play is the default; Enter on the menu screen plays.

Bottom strip: three cells. Server state with the note from the status route; client check result; the latest news line, which is the last Discord announcement text carried by the status route's note field in v1.

### Profiles

The profile list on the left as menu items, the selected profile's fields on the right: name, boot file, server address, username, password, extra loader arguments, startup script, window mode, window size, menu size, close launcher after launching. New, Duplicate, Delete to the Recycle Bin, Save. The shipped examples are shown and cannot be saved over; Duplicate is offered instead.

### Addons

Left box: the catalogue as a two-column list, each row a checkbox, the name and installed version, and a source tag: bundled, the maintainer's name for third-party, `no v4` for items with no v4 version. Right box: the selected item's card with description, installed version, load line, v3 notes, and Install, Update, Open config folder, Read the docs. Save writes the startup script.

### Setup

Runs on first launch when no Ashita is found, and from the menu as "Repair or update Ashita" afterwards. Pages: choose folder, download, unpack, write profile, import from v3, done. Each download has its own progress bar and size.

### FFXI client guide

A separate window with one page per step and a picture on each. The steps and their checks:

| Step | Page | Check |
| --- | --- | --- |
| 1 | Get the client from Square Enix's free download | none; link and expectations |
| 2 | Install to a folder you own, `C:\Games\PlayOnline` recommended | reads the registered FFXI path; amber if under Program Files, with the reason |
| 3 | Update it once with PlayOnline Viewer, no login | reads the client version stamp and compares with the server's expected version |
| 4 | Let xiloader and the game through Windows Firewall | looks for allow rules for both executables; offers to add them, explaining the one admin prompt first |
| 5 | Make your account at the xiloader prompt | none; picture of the menu and what the password is for |
| 6 | Play | all earlier checks green |

A step with an automatic check turns green on its own. A step without one has a "done" tick and the page says so. Every page has "Something's wrong" leading to the Discord invite. The guide opens from the menu at any time, at any page.

### Settings

Ashita folder, FFXI folder override, catalogue refresh, open the launcher's data folder, version and licence. Version 1 has one theme, so there is no theme setting.

## Behaviours

### Client version check

The FFXI folder comes from `HKLM\SOFTWARE\WOW6432Node\PlayOnlineUS\InstallFolder`, value `0001`, or the Settings override. The installed version is the newest stamp matching `^3\d{7}_\d+` at the start of a line in `patch.cfg` in that folder, read as Latin-1. The expected version comes from the status route (see below), with `30260805_0` and lock mode 2 as the shipped defaults.

Comparison matches the server: the first six characters of each stamp. Lock 2 blocks only when expected is greater than installed; lock 1 blocks on any difference; lock 0 never blocks. The card shows both full stamps and a sentence: "Your client is 30260904_1, the server expects 30260805_0. Ready to play." A missing `patch.cfg` reads as "version unknown, update the client" and does not block Play.

### Server status

`GET https://fairywitch.ca/api/public/vanadreams/status` returns `{state, checked_at, note}` with states setting-up, online, offline, maintenance. The launcher adds two optional fields it will read when the route carries them: `client_ver` and `ver_lock`. Fetched on start and every five minutes, five-second timeout, failure shows "status unknown" with the last good time. Pill colours: online green, maintenance amber, offline red, setting-up and unknown mist.

### Credentials

Username and password are stored per profile in `%LOCALAPPDATA%\Vanadreams\credentials.dat`, encrypted with the Windows Data Protection API in current-user scope. They are never written into the boot ini. At launch they are passed to xiloader as `--user` and `--pass` on the command line through the ini's command field written to a temporary copy of the profile that is deleted after Ashita has started. A profile with no stored password launches to the xiloader prompt.

### Launch

`Ashita-cli.exe <profile>.ini` with the Ashita folder as the working directory. The launcher records the launch time for "last played". If "close after launching" is set it exits five seconds after Ashita starts.

## The catalogue

`catalog.json` at the repository root. Fetched from the raw main branch on start; the last good copy is cached in the data folder and used when the fetch fails, with a line in the Addons screen saying so.

```json
{
  "schema": 1,
  "interface": "4.30",
  "items": [
    {
      "id": "lootwhore",
      "name": "Lootwhore",
      "kind": "plugin",
      "source": { "type": "github-release", "repo": "ThornyFFXI/Lootwhore",
                  "asset": "Lootwhore.*Interface.4.30.zip" },
      "version": "1.18c",
      "install": "unzip-to-root",
      "load": "/load lootwhore",
      "config": "config\\lootwhore\\",
      "docs": "https://github.com/ThornyFFXI/Lootwhore",
      "description": "Lots and passes the treasure pool by your rules.",
      "v3": { "name": "Lootwhore", "carry": "profiles", "note": "Check drop and store lists after import." }
    },
    {
      "id": "distance", "name": "distance", "kind": "addon",
      "source": { "type": "bundled" }, "load": "/addon load distance",
      "description": "Shows distance to the target."
    },
    {
      "id": "find", "name": "find", "kind": "addon",
      "source": { "type": "repo-folder", "repo": "Sippius/Ashita-v4-addons", "path": "find" },
      "install": "copy-to-addons", "load": "/addon load find"
    },
    {
      "id": "pivot", "name": "XIPivot", "kind": "polplugin",
      "source": { "type": "github-release", "repo": "HealsCodes/XIPivot", "asset": "XIPivot_Ashita_v4_*.zip" },
      "version": "4.3.001", "install": "unzip-to-root", "load": "polplugins:pivot",
      "config": "config\\pivot\\pivot.ini"
    },
    {
      "id": "crafty", "name": "Crafty", "kind": "plugin",
      "source": { "type": "none" }, "replacement": null,
      "v3": { "name": "Crafty", "note": "No v4 version exists." }
    }
  ]
}
```

Source types: `bundled` (present in the Ashita folder, nothing to fetch), `github-release` (download the newest release asset matching the pattern, verify the size the API reports, unzip into the Ashita folder), `repo-folder` (fetch the folder's files from the raw branch, copy into `addons\<id>\`), `none` (no v4 version; listed greyed with `replacement` if any).

Install actions: `unzip-to-root`, `copy-to-addons`. Load lines: `/load <name>`, `/addon load <name>`, or `polplugins:<name>` which is written to the boot ini's `[ashita.polplugins]` section rather than the script.

The first catalogue is built from the verified map of Lee's v3 stack: 18 bundled addons, affinity and nokb, the Thorny plugins Bellhop, Lootwhore, Packer, Multisend, FindAll, LegacyAC, the Oseem addon, Sippius find, XIPivot, and the eight with no v4 version: Crafty, DressMe (replacement Packer), WatchEXP, lotomatic, pbar, pupatt (replacement pupsets), status, synplicity.

## The startup script

Save on the Addons screen writes `scripts\vanadreams.txt` in the Ashita folder and points the profile's script field at it. Order: plugins in catalogue order, then `/load addons`, then `/addon load` for each ticked addon, then `/wait 3`, then each item's config lines. The file ends with a block:

```
# --- yours ---
# Anything below this line is kept exactly as you wrote it.
```

Everything under that marker is preserved on every save. XIPivot and any other `polplugins:` item is written to the profile ini, not the script.

## Import from v3

Setup looks for a v3 install at the common places (Desktop, Program Files, a user-chosen folder) by the presence of `Ashita.exe` and `config\boot`. For each boot XML it writes a v4 ini with name, boot file, command, script, window size and windowed mode carried over. For the catalogue items marked with a `carry` value it copies: MultiSend's `config\MultiSend.xml`; Lootwhore's profiles; Ashitacast XMLs into `config\LegacyAC\`; XIPivot overlay folders from `plugins\DATs` to `polplugins\DATs` and a `pivot.ini` written from the v3 overlay list. Items with a v4 home are pre-ticked in the picker; items without are listed with their replacement.

## Architecture

WPF on .NET Framework 4.8, C#. One window hosting pages; the guide is a second window. No MVVM framework; plain view models with change notification. All work that is not a window lives in `Services`, each class independent and testable:

- `ProfileStore`: read and write boot ini files, preserving comments and unknown keys.
- `LoaderCommand`: parse and build the xiloader command line.
- `ClientVersion`: find the FFXI folder, read the stamp, compare with lock semantics.
- `ServerStatus`: fetch and cache the status route.
- `Downloader`: HTTP download with progress and size check; unzip.
- `Catalog`: fetch, cache, resolve items to install actions.
- `ScriptWriter`: generate the startup script, preserve the "yours" block.
- `PolPlugins`: read and write the boot ini's POL plugin section.
- `Credentials`: DPAPI store.
- `V3Import`: find a v3 install, convert profiles, copy config.
- `Firewall`: detect allow rules for two executables; add them through an elevated helper call.

Styling lives in one resource dictionary: tokens as brushes, the FF7 box as a Border style, the menu item template with the hand cursor, the pill, the button. Assets (moon, star, guide screenshots) are embedded resources.

## Files the launcher owns

`%LOCALAPPDATA%\Vanadreams\`: `settings.json` (Ashita folder, FFXI override, last profile), `credentials.dat`, `catalog.cache.json`, `status.cache.json`, `downloads\` (kept release zips), `launcher.log` (rolling, 1 MB).

Inside the Ashita folder it writes only: `config\boot\*.ini`, `scripts\vanadreams.txt`, and whatever a catalogue install unpacks.

## Errors

Every failure is a sentence on the screen where it happened, saying what failed and what to do: "Couldn't reach fairywitch.ca; showing status from 9:12 p.m." "Download of Bellhop stopped at 40%; press Install to resume." "Ashita-cli.exe is missing from C:\Games\Vanadreams; run Repair." Nothing is a modal box except Delete confirmations and the firewall admin prompt explanation. The log carries the detail.

## Testing

A test project alongside the launcher, MSTest, run in Visual Studio or by command line. Real files, no mocks of the file system:

- ini round-trip on the shipped examples and on a v3-converted profile; comments and unknown keys survive.
- command line parse and build in both directions, including extra arguments.
- `patch.cfg` stamp extraction on a captured real file; comparison across all three lock modes; missing file.
- catalogue resolution for each source type and each install action; cache fallback.
- script writer: ordering, POL plugin routing, the "yours" block preserved across saves.
- v3 import from a copy of a real v3 boot XML and config tree.

The window is exercised by hand against a scratch copy of the Ashita v4 beta, as draft 1 was.

## Build and release

`launcher\VanadreamsLauncher.sln`. Builds in Visual Studio 2026 or with `msbuild /p:Configuration=Release`. A release is a zip holding `VanadreamsLauncher.exe` alone, attached to a tagged GitHub release. No installer, no admin, no telemetry.

## Repository layout

```
vanadreams-ashita/
  README.md
  catalog.json
  addons/
  ui/
  scripts/
  launcher/
    VanadreamsLauncher.sln
    VanadreamsLauncher/        the WPF project
    VanadreamsLauncher.Tests/  MSTest
    assets/                    moon, star, guide screenshots
  docs/superpowers/specs/      this document
```

## Rulings this design rests on

Recorded in the Fairy Witch project matrix, 13 Sept 2026: both a launcher and an in-game addon, launcher first; Vanadreams players with a general launcher underneath; the v1 scope above; WPF on .NET Framework 4.8; mockup A with B's moon; the FFXI client guide with pictures, links and checks.
