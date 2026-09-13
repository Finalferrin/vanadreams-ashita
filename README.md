# Vanadreams Ashita

Addons and UI for playing on [Vanadreams](https://fairywitch.ca), a private Final Fantasy XI server, with Ashita v4.

## What is here

| Folder | What |
| --- | --- |
| `addons/` | One folder per addon. Copy the folder into your Ashita `addons` directory. |
| `ui/` | The Vanadreams UI, an Ashita v4 addon. Same install as any other addon. |
| `scripts/` | Example boot script and launcher config for connecting to Vanadreams. |

## Installing an addon

1. Copy the addon's folder from `addons/` (or `ui/`) into `Ashita-v4beta\addons\`.
2. In game, load it with `/addon load <name>`, or add that line to `Ashita-v4beta\scripts\default.txt` so it loads every time.

## Connecting to Vanadreams with Ashita

Ashita's own bootloader will not reach the server; point it at xiloader instead.

1. Get the latest xiloader from [LandSandBoat's releases](https://github.com/LandSandBoat/xiloader/releases).
2. In `Ashita-v4beta\config\boot\`, copy `example-privateserver.ini` to `vanadreams.ini` and set the boot section as in [`scripts/vanadreams.ini`](scripts/vanadreams.ini).
3. Make a shortcut to `Ashita-cli.exe vanadreams.ini`.

Server address: `vanadreams.fairywitch.ca`. Rates, rules and status are on [fairywitch.ca](https://fairywitch.ca).

Vanadreams is an unofficial fan-run private server with no affiliation to Square Enix.
