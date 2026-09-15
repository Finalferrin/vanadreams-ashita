# crafty

Repeats a synthesis on Vanadreams, the way Crafty did on Ashita v3. Runs only on Vanadreams; on any other server it loads, says so, and does nothing.

## Use

1. Craft the recipe once by hand through the game's own crafting menu. Crafty sees it and says so in chat.
2. Type `/crafty` to open the window. The synth you just did is shown at the top.
3. Set how many times, press **Repeat**.

It waits out the server's cooldown from your hand synth, then keeps going until the count is reached. **Stop** in the window or `/crafty stop` ends it early. It also stops itself when a crystal or an ingredient runs out, the inventory is nearly full, the server says the recipe is bad or beyond your skill, or too many synths fail in a row.

The window shows the queue, the counters (synths, success, HQ, failed, free slots) and the settings: seconds between synths (the server refuses anything under 15), the free-slot floor, and the failure streak that stops it.

### Without the window

- `/crafty repeat 20` repeats the last hand synth twenty times.
- `/crafty add 12 "Fire Crystal" "Lizard Skin" "Distilled Water"` then `/crafty start` queues a recipe by name without crafting it first. List an ingredient twice to use two of it; up to eight ingredients; item ids work too.
- `/crafty list`, `/crafty clear`, `/crafty delay 20`.

## How it works

It watches the game's own synthesis request (packet 0x096) when you craft by hand and keeps the crystal and ingredient ids. On Repeat it looks those items up in your main inventory each time, sends the same request with the slots it found, waits for the result (0x06F), counts success, HQ and failure, then waits out the cooldown and goes again. It never sends anything unless you press Repeat or type start.
