# SharedExploration

Server-only BepInEx plugin for Valheim: every map table gets the areas all players have explored.

The server samples each connected player's position (`ZNetPeer.m_refPos`, every 2 s) and marks the
same circle `Minimap.Explore` would on a map of the game's size, then merges that into the explored
pixels of every map table's shared map data (`ZDOVars.s_data`, map data version 3), reading what the
tables already hold so a manual Record on one table reaches the others. Pins are left as they are.
The explored map is saved per world in `BepInEx/config/SharedExploration/<world>.explored.gz`.

Settings: `BepInEx/config/Tie.SharedExploration.cfg`. Console: `mapexplore [update]` on the server.
Clients do not need the plugin.

Install with a mod manager: [Tie-SharedExploration on Thunderstore](https://thunderstore.io/c/valheim/p/Tie/SharedExploration/).
`thunderstore/build.sh <folder>` builds that package (mod page, icon from `thunderstore/make_icon.py`,
changelog and manifest in `thunderstore/`; the version is `Version` in the plugin).
