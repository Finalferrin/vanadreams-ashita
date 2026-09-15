# crafty

Repeats a synthesis on Vanadreams, the way Crafty did on Ashita v3. Runs only on Vanadreams; on any other server it loads, says so, and does nothing.

## Use

Have the crystal and every ingredient in your main inventory, then:

```
/crafty add 12 "Fire Crystal" "Lizard Skin" "Distilled Water"
/crafty start
```

That queues twelve synths of that recipe. List an ingredient twice to use two of it. Up to eight ingredients. Names are the game's item names, in quotes when they have spaces; item ids work too.

- `/crafty` opens and closes the window, which shows the queue, the counters and the settings.
- `/crafty list`, `/crafty clear` look at and empty the queue.
- `/crafty stop` stops. It also stops itself when the queue is done, a crystal or an ingredient runs out, the inventory is nearly full, the server says the recipe is bad or beyond your skill, or too many synths fail in a row.
- `/crafty delay 20` sets the seconds between synths. The server refuses anything under 15.

## How it works

It sends the game's own synthesis request (packet 0x096) with the crystal and the inventory slots it found the ingredients in, waits for the result (0x06F), counts success, HQ and failure, then waits out the cooldown and goes again. It never sends anything unless you start it.
