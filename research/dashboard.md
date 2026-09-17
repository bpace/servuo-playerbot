# PlayerBots dashboard research

This note uses the checked-in source trees as evidence: `upstream/uo-offline` for the reference project and `upstream/ServUO-pub57` for the target server. It is intentionally not based on the attached screenshot alone.

## The two upstream administration surfaces

### In-game GM gump

The UO Offline gump is accessed by typing `[GmPanel` on a character with `GameMaster` access. `[BotPanel` is an alias. Both commands send `BotPanelGump` to the issuing mobile. [BotPanelCommand.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/AdminPanel/BotPanelCommand.cs#L16-L30)

The gump is not the browser screen in the screenshot. It supplies first-time world setup, an at-your-location behavior/count spawner, city/dungeon teleport buttons, and bot/spawner cleanup. Destructive cleanup asks for confirmation. [BotPanelGump.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/AdminPanel/BotPanelGump.cs#L38-L41) [BotPanelGump.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/AdminPanel/BotPanelGump.cs#L379-L401) [BotPanelGump.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/AdminPanel/BotPanelGump.cs#L512-L531)

### Browser map/editor shown in the screenshot

The screenshot is `tools/map/map.html`, served by the separate Python HTTP server, not a ServUO gump. The server states its own routes and uses game-produced files under `Data/Live`. [serve_map.py](../../../upstream/uo-offline/tools/map/serve_map.py#L1-L9) [serve_map.py](../../../upstream/uo-offline/tools/map/serve_map.py#L283-L352)

| Screenshot feature | Source-backed implementation |
| --- | --- |
| Map layers for waypoints, routes, destinations, spawns, PKs, and edit modes | Static/game-data view from `/mapdata.json`; the browser exposes toggles and editors. [map.html](../../../upstream/uo-offline/tools/map/map.html#L64-L156) [serve_map.py](../../../upstream/uo-offline/tools/map/serve_map.py#L227-L258) |
| Live dots for bots, monsters, NPCs, vendors, and pets | `[LiveMap on [seconds]` writes a compact `entities.json`; the viewer polls `/live.json`. Entity classification includes Bot, Vendor, Pet, Monster, and NPC. [LiveMapSnapshot.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/LiveMapSnapshot.cs#L11-L21) [LiveMapSnapshot.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/LiveMapSnapshot.cs#L162-L221) |
| Bot census, behavior color/filtering, stuck indication, and route inspection | Browser-side live map features, driven by snapshot behavior/status/route fields. [map.html](../../../upstream/uo-offline/tools/map/map.html#L1341-L1483) [LiveMapSnapshot.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/LiveMapSnapshot.cs#L132-L146) |
| Event feed | The map serves a tail of `event-journal.jsonl` at `/journal`; the browser polls it every five seconds. [serve_map.py](../../../upstream/uo-offline/tools/map/serve_map.py#L304-L332) [map.html](../../../upstream/uo-offline/tools/map/map.html#L1341-L1384) |
| Reload/regenerate controls | Browser POSTs a token file; an in-game polling timer performs the command and writes an acknowledgement. It does not expose the game process directly as an HTTP command endpoint. [serve_map.py](../../../upstream/uo-offline/tools/map/serve_map.py#L899-L938) [EditorReloadWatcher.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/EditorReloadWatcher.cs#L5-L14) |

## ServUO pub57 boundary

ServUO pub57 has a very limited status listener, but it is disabled by default. If enabled, it binds `http://*:80/status/` with no authorization and publishes online client names and coordinates. It is not a safe foundation for an administrative dashboard. [WebStatus.cs](../../../upstream/ServUO-pub57/Scripts/Misc/WebStatus.cs#L16-L35) [WebStatus.cs](../../../upstream/ServUO-pub57/Scripts/Misc/WebStatus.cs#L44-L81) [WebStatus.cs](../../../upstream/ServUO-pub57/Scripts/Misc/WebStatus.cs#L103-L190)

## Recommended LAN-only ServUO dashboard

Build a PlayerBots-owned dashboard, separate from `WebStatus`, with these boundaries:

1. Serve a static map UI and read-only JSON on a dedicated non-public port, bound to the shard LAN address or loopback behind a LAN reverse proxy. Firewall it to the administrator subnet. Require a configured high-entropy password/token for every route, including read-only routes; use HTTPS if it crosses any untrusted LAN segment.
2. Have the ServUO timer create an immutable snapshot every few seconds only while the dashboard has active viewers. Snapshot bot locations/state plus separately classified existing mobiles for the map. Write a temporary JSON file and atomically replace the previous file, as UO Offline does, so a reader never receives a partial world snapshot. [LiveMapSnapshot.cs](../../../upstream/uo-offline/playerbots/source/CustomBots/LiveMapSnapshot.cs#L214-L221)
3. Make the web process write only narrowly typed action requests: `enable`, `disable`, target population `0..250`, spawn `1..50`, and remove-all. The game timer validates and executes those requests on the ServUO thread, records the result, and the dashboard displays the acknowledgement. Never mutate `World.Mobiles` in an HTTP request thread.
4. Treat `remove all` as a destructive two-step operation: server-issued one-time confirmation token plus explicit count shown in the UI. Log the authenticated operator, action, input, result, and timestamp into the event ring buffer.
5. Keep map editing, arbitrary command execution, and direct writes to shard data out of the first release. The requested product only needs map visibility, state/count, enable/disable, target population, spawn/remove, and events. This keeps its privilege surface much smaller than the upstream general-purpose editor.

This design preserves the useful upstream pattern, snapshot for observation and queued/acknowledged actions for control, while avoiding pub57's unauthenticated wildcard listener.
