# charcapture

Takes a snapshot of your character for porting to Vanadreams: jobs and levels, skills, merits and job points, spells, abilities, weapon skills, traits, key items, every bag with augments, gear, gil, look, nation, rank and title. Quests and missions are not carried; everyone starts the story fresh on Vanadreams.

It reads only. It sends nothing and changes nothing in the game.

## Install

Copy this folder to `Ashita-v4beta\addons\charcapture\`, then in game:

```
/addon load charcapture
```

## Use

Log in on the character you want to bring. Open the **Merit Points** menu and the **Job Points** menu once from the main menu, since the game only sends your spent merits and job point upgrades when those open. Then:

```
/capture
```

It writes `config\charcapture\<YourName>.json` in the Ashita folder and prints a one-line summary. `/capture show` prints the summary again.

Then open the Vanadreams Launcher, go to Capture, pick the snapshot and press **Send to Vanadreams**. The server side imports it onto your Vanadreams account after a look.

## What's in the file

Documented in [the design](../../docs/superpowers/specs/2026-09-13-character-capture-design.md). Item `extra` blocks are kept whole, so augments come with the gear.
