# SharedExploration

A **server-side** plugin: the server keeps track of where every player has been and adds it to the
explored area of **every map table**. Anyone who uses **Read** on a map table gets the whole group's
exploration, even of places they never visited and even if the others never stood at a table.

## What it does

- Every couple of seconds, the server marks the area around each connected player as explored, with
  the same radius the game uses for your own map.
- Every 30 seconds (by default), it adds what is new to every map table in the world.
- Map tables also share with each other: something added to one table with **Record** reaches all of
  them on the next update.
- Pins are not touched: only the explored (unfogged) area is shared.
- The explored area is kept between restarts, per world, in
  `BepInEx/config/SharedExploration/<world>.explored.gz`.

## Install

On the **dedicated server only**, with BepInEx: install it with a mod manager's server profile, or
copy `SharedExploration.dll` into the server's `BepInEx/plugins`. Players need nothing: they use the
map tables as usual.

## Settings

`BepInEx/config/Tie.SharedExploration.cfg`, section `[General]`:

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Add the areas players explore to every map table. |
| `SampleIntervalSeconds` | `2` | How often player positions are recorded (the game explores every 2 s). |
| `MapTableUpdateSeconds` | `30` | How often map tables are updated with the recorded areas. |
| `ExploreRadius` | `0` | Radius in metres revealed around each player; `0` uses the game's own radius. |

## Console

`mapexplore` shows how much of the map is explored and how many map tables there are;
`mapexplore update` updates the tables right away. It runs on the server, so from a player's console
it needs a mod that sends commands to the server, such as ServerDevcommands (`server mapexplore`).
