# Vanadreams Ashita

Everything a player needs to play on [Vanadreams](https://fairywitch.ca), a private Final Fantasy XI server, with Ashita v4: the launcher, the addons, and the boot files.

## The launcher

`VanadreamsLauncher.exe` is a small Windows program that does what the old Ashita v3 launcher did, plus the parts that used to be a wiki page:

- **Profiles and Play.** Pick who you are, press Play. Logins are remembered, protected with Windows's own per-user encryption, and never written into the boot files. Add other servers, or retail, the same way.
- **Client check.** Before every launch it reads your FFXI client's version stamp and compares it the way the server does, so you know before you press Play whether you'll get in.
- **Live status.** Whether Vanadreams is up, from the same status the website shows.
- **Setup.** First run downloads the Ashita v4 beta and xiloader into a folder you choose and writes the Vanadreams profile. It can adopt an Ashita v4 you already have, and it brings profiles and configs across from an Ashita v3 install.
- **The FFXI client guide.** A step-by-step window for installing and updating the retail client and getting it through the firewall, with checks that turn green as you go.
- **Addons.** A catalogue of plugins and addons: tick to enable, one click to install or update, and it writes your startup script. Anything from your v3 stack that has a v4 home is there.
- **Fishing.** The switch and the catch log for [vanafish](addons/vanafish/), the Vanadreams fishing bot.

It needs nothing installed: Ashita v4 already requires the .NET Framework that every Windows 10 and 11 machine has.

**Download:** the latest release is always at
`https://github.com/Finalferrin/vanadreams-ashita/releases/latest/download/VanadreamsLauncher.exe`.
Run it. It opens on Setup and runs from wherever you put it; Settings has an Install shortcuts button that copies it into your apps folder and makes Start menu and desktop shortcuts. After that it updates itself: each start checks for a newer release, fetches it, checks the signature, and offers a restart. Clicking a newly downloaded exe while a copy is installed refreshes the installed copy and opens that. Releases from 0.2.0 are code-signed by Lee Hattery through Azure Artifact Signing. A brand-new certificate has no download history with Windows yet, so SmartScreen may still show a warning for the first while; choose More info, then Run anyway, and check that the publisher shown is Lee Hattery.

### Building it

`launcher\VanadreamsLauncher.slnx`, Visual Studio 2026 or `dotnet build -c Release`. Tests: `dotnet test launcher\VanadreamsLauncher.Tests`. The design is in [docs/superpowers/specs](docs/superpowers/specs/).

Releases come from `launcher\release.ps1`: it builds, runs the tests, signs the exe with Azure Artifact Signing, scans it with Defender, and with `-Publish` creates the GitHub release. The header of the script says what signing needs on the build PC.

The launcher plays its own theme, "The Lanterns Are Lit", quietly while it is open and fades it out when the game starts. Turn it off on Settings. Credits are in [CREDITS.md](CREDITS.md).

## What else is here

| Folder | What |
| --- | --- |
| `addons/` | One folder per addon. `vanafish` is the fishing bot. Install through the launcher, or copy a folder into your Ashita `addons` directory. |
| `catalog.json` | The launcher's catalogue: every addon and plugin it can install, where it comes from, how it loads. Edit this to add one; every launcher picks it up on its next start. |
| `ui/` | The Vanadreams in-game UI, an Ashita v4 addon. Not started yet. |
| `scripts/` | Example boot ini and startup script for connecting by hand. |
| `server/` | For people running their own LandSandBoat server. [`landsandboat-level-99-gear-stats.sql`](server/landsandboat-level-99-gear-stats.sql) fills in the stats for 2,291 level 99 pieces; how to apply it is at the top of the file. |

## Connecting by hand, without the launcher

1. Get the latest xiloader from [LandSandBoat's releases](https://github.com/LandSandBoat/xiloader/releases) and put it in `Ashita-v4beta\bootloader\`.
2. In `Ashita-v4beta\config\boot\`, copy `example-privateserver.ini` to `vanadreams.ini` and set the boot section as in [`scripts/vanadreams.ini`](scripts/vanadreams.ini).
3. Run `Ashita-cli.exe vanadreams.ini`.

Server address: `vanadreams.fairywitch.ca`. Rates, rules and status are on [fairywitch.ca](https://fairywitch.ca).

Vanadreams is an unofficial fan-run private server with no affiliation to Square Enix.
