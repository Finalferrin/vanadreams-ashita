# Vanadreams music overlay

An XIPivot overlay that puts Vanadreams' own music on the game's title screen and character select. Only our files are in here; nothing from Square Enix is copied.

| File | Replaces | Track |
| --- | --- | --- |
| `sound/win/music/data/music108.bgw` | Vana'diel March: the title screen and character select | The Lanterns Are Lit |

Install it from the launcher's Addons page (it needs XIPivot, also on that page). By hand: copy the `vanadreams-music` folder into `Ashita-v4beta\polplugins\DATs\` and add it to the `[overlays]` list in `config\pivot\pivot.ini`.

The BGW files are made with `tools\bgw` from the wav of each track:

```bash
dotnet run --project tools\bgw -c Release -- encode track.wav music108.bgw --id 108 --loop 0
```

`--id` is the game's track number, `--loop` the point in seconds the track returns to when it ends (0 loops the whole thing; leave it out to play once). The wav should be 16-bit, 44.1 kHz stereo; ffmpeg gets there from anything with `-ar 44100 -ac 2 -sample_fmt s16`.
