# Retail character capture — design

Date: 2026-09-13. Status: approved for build (client half); the site and server halves are specified here for the sessions that own them.

## Purpose

A player on retail runs one command and gets a snapshot of their character: jobs, skills, spells, abilities, key items, every bag, gear, gil, look, nation, rank, titles. The launcher sends the snapshot to fairywitch.ca. The server side imports it onto a Vanadreams account so the player arrives as the character they were, with the story starting fresh.

## Rulings

Everything the client exposes is captured. Delivery is the launcher uploading through the site. Capture runs on command only. Quests and missions are not ported.

## The three halves

| Half | Where | Owner |
| --- | --- | --- |
| Capture addon `charcapture` | `addons/charcapture/` in this repo | built |
| Launcher Capture page and upload | `launcher/` in this repo | built |
| Site endpoint and queue, Claudette importer | fairywitch.ca worker; `vanadreams/` host scripts | the site and server sessions |

## The snapshot file

`config\charcapture\<CharacterName>.json` in the Ashita folder, UTF-8, one object:

```json
{
  "schema": 1, "addon": "charcapture", "addon_version": "0.1.0",
  "captured_at": "2026-09-13T22:41:09Z", "captured_on": "retail",
  "character": {
    "name": "Ferrin", "server_id": 123456, "race": 1, "face": 3, "size": 1,
    "nation": 1, "rank": 6, "rank_points": 1200, "title": 42, "homepoint": 235, "residence": 0
  },
  "jobs": {
    "main": 1, "main_level": 75, "sub": 2, "sub_level": 37,
    "levels": { "1": 75, "2": 37, "...": 0 },
    "master_levels": { "1": 0 },
    "job_points": { "1": { "points": 0, "spent": 0, "capacity": 0 } },
    "merits": { "points": 5, "max": 30 }, "limit_points": 1200,
    "exp": { "current": 4000, "needed": 6000 }
  },
  "skills": {
    "combat": { "0": { "skill": 276, "rank": 5, "capped": true } },
    "craft":  { "0": { "skill": 42, "rank": 0, "capped": false } }
  },
  "spells": [1, 2, 3], "abilities": [16, 512], "weaponskills": [1, 2], "traits": [1], "key_items": [1, 2, 3],
  "gil": 123456,
  "inventory": {
    "0": [ { "slot": 1, "id": 4096, "count": 12, "flags": 0, "price": 0, "extra": "00…" } ],
    "1": [], "2": [], "8": []
  },
  "equipment": { "0": { "container": 0, "slot": 5, "id": 17384 } },
  "look": { "hair": 3, "head": 0, "body": 0, "hands": 0, "legs": 0, "feet": 0, "main": 0, "sub": 0, "ranged": 0 },
  "not_captured": ["merit categories", "quests", "missions", "fame", "linkshells", "mog house layout"]
}
```

Job ids are the game's (1 WAR … 22 RUN). Skill ids are Ashita's indexes for `GetCombatSkill` and `GetCraftSkill`. Container ids are the game's (0 inventory, 1 mog safe, 2 storage, 3 temporary, 4 locker, 5 satchel, 6 sack, 7 case, 8–15 wardrobes 1–8, 16 mog safe 2). `extra` is the item's 24-byte extra block, hex, which carries augments, trial numbers and linkshell colours. Gil is the count of the pseudo-item in inventory slot 0.

Every array is complete, not a diff: a snapshot replaces the previous one.

## The addon

`/capture` writes the file and prints a one-line summary: name, main job and level, item count across all bags, gil. `/capture show` prints the last summary again. It reads only through Ashita's memory managers, sends no packets and changes nothing. It runs on retail and on any server; the file says which.

Data comes from: `IPlayer` for jobs, levels, master levels, job points, merits, limit points, exp, skills, spells, abilities, weapon skills, traits, key items, nation, rank, rank points, title, homepoint, residence; `IInventory` for every container and the equipped slots; `IEntity` for name, race, look and size; `IParty` member 0 for the server id.

## The launcher

A Capture page in the menu lists the snapshots in `config\charcapture\`, shows each summary, and has "Send to Vanadreams". Sending posts the JSON to the capture route with the player's Vanadreams username, taken from the selected profile's remembered login, and shows the reply: queued with a reference, or the reason it was refused. Nothing is sent without the button.

## The site endpoint, for the site session

`POST /api/public/vanadreams/capture`, body the snapshot JSON, header `X-Vanadreams-User: <username>`. Limits: 2 MB, schema 1, character name present. Stores a row in a D1 table:

```
vanadreams_captures(id INTEGER PK, username TEXT, charname TEXT, received_at TEXT, bytes INTEGER, snapshot TEXT, status TEXT DEFAULT 'queued', note TEXT)
```

Replies `{ "ok": true, "reference": "<id>", "queued": <count for that user> }`. A second snapshot for the same username and charname replaces the queued one. Rate: five per hour per username. The bridge gets `GET /api/bridge/vanadreams/captures/next` returning the oldest queued row and marking it `taken`, and `POST /api/bridge/vanadreams/captures/<id>` with `{status, note}` to close it, both under the existing bridge token.

## The importer, for the server session

A script on Claudette pulls the queue through the bridge and maps a snapshot onto a Vanadreams character, run by hand per port after a look at the row. Mapping:

| Snapshot | Table |
| --- | --- |
| character.name, nation | `chars` (charname, nation); `chars.keyitems` from key_items; `chars.abilities`, `chars.weaponskills`, `chars.titles` bitmasks from the arrays |
| jobs.levels, master flags | `char_jobs` (one column per job); `char_stats` (mjob, sjob, mlvl, slvl); `char_exp` (merits, limits) |
| jobs.job_points | `char_job_points` |
| skills | `char_skills` (skillid, value, rank) |
| spells | `char_spells` |
| inventory, gil | `char_inventory` (location, slot, itemId, quantity, extra); gil as item 65535 in location 0 slot 0 |
| equipment | `char_equip` |
| look, character.race/face/size | `char_look` |
| character.rank, rank_points | `char_profile` |

Not imported, by ruling: quests, missions, fame, event flags. The importer refuses a snapshot whose username has no Vanadreams account, or whose character name already exists on a different account, and writes the reason back through the bridge so the launcher can show it.

## Tests

The addon has no automated tests; it is exercised by running `/capture` on a character and reading the file. The launcher's upload client is tested against a local stub for the reply shapes. The importer is the server session's to test against a copy of the database.
