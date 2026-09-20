# vanachatfix

Loads Ashita's `chatfix` only on the servers that need it, as an Ashita v4 addon.

`chatfix` ships with Ashita for private servers whose code is older than a client update that moved the chat message one byte along in the chat packet. On those servers chat is unreadable without it. On a server that already sends the current layout, which Vanadreams does, it does harm: every chat line loses its first letter, tells go to the wrong name, and the pop-up menus NPCs open are cut up at the spaces.

This addon rewrites nothing itself. It looks at the first chat packet the server sends, tells the two layouts apart by one byte, and then loads the stock `chatfix` on an old-layout server or makes sure it is not loaded on a current one. Tick this instead of `chatfix` if you play on more than one server from the same Ashita.

On an old-layout server the first chat line of a session can arrive unfixed, because `chatfix` is loaded in answer to it.

## Install

Copy this folder to `Ashita-v4beta\addons\vanachatfix\`, then in game:

```
/addon load vanachatfix
```

The Vanadreams launcher does both from its Addons page.

## Commands

| Command | What it does |
| --- | --- |
| `/vanachatfix` | Says which layout this server sends and whether `chatfix` is loaded. |
