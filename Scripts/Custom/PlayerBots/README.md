# PlayerBots for ServUO pub57 / AoS

ServUO-native PlayerBots v1. It is inspired by the MIT-licensed PlayerBots system in Klein187/uo-offline, but is a fresh implementation for ServUO's net48 runtime and APIs.

The system adds persistent headless `PlayerMobile` characters. They roam between cities, use a visible moongate-style long trip, chat, bank gold, seek monsters, fight using ServUO combat rules, report murders, and resurrect after a delay. It is disabled by default and caps the management command at 250.

Use `[PlayerBots status`, `[PlayerBots spawn 5`, `[PlayerBots population 50`, `[PlayerBots on`, `[PlayerBots off`, and `[PlayerBots remove` as a GameMaster. Start with `[PlayerBots population 10`, then `[PlayerBots on` on the AoS shard and inspect CPU, save time, pathing, banks, and murder reporting before increasing it.

## Local dashboard

The mod also provides a LAN dashboard with no browser token, URL secret, cookie, or browser storage. On first startup it creates `Config/PlayerBotsDashboard.cfg`; its `BindAddress`, `Port`, and `AllowedAddress` determine access. Defaults are `192.168.50.139`, port `8081`, and the shard owner's LAN address `192.168.50.81`. Open `http://192.168.50.139:8081/` from that allowed device.

It shows the bot census, a coordinate map view, current target, bot state, and the recent event log. An amber ring marks a bot in war mode; selecting it reports both its combat mode and target. Hovering the map shows exact facet X/Y coordinates and clicking uses the same zoom-safe conversion to populate the route editor. The optional reference-city layer is visual only and never creates authored route data. It queues enable, disable, population, spawn, and remove requests for the shard's own PlayerBots timer, so HTTP request threads never modify the game world directly. Keep the dashboard on a private network. The canvas is a live coordinate view, not a copy of Ultima Online map art.

## Road PKs

Choose `Player Killer` in the stored-spawn editor and save an explicitly authored road location. The server accepts that role only at an unguarded Felucca point, revalidates it before materializing, and the `Spawn road PKs` action creates only those authored definitions. PK bots target real nearby players, never other PlayerBots, and use the normal ServUO combat and notoriety path. They remain inert until PlayerBots are enabled. Trammel, other protected facets, and guarded Felucca locations are rejected.

## Native Felucca thief test

The thief slice is disabled after every shard start and has no roaming-player target mode. A GM must first run `[PlayerBots thieving on]`, place a live Thief PlayerBot next to one named consenting, non-staff player in Felucca, give that player a disposable top-level backpack item of ten stones or less, then run `[PlayerBots thievingtest <player name>]`. The command selects that one player for one native ServUO `Stealing` cursor invocation and clears the selection before the next bot tick. It never moves an item itself. Check the item, criminal flag, and server log after the test, then run `[PlayerBots thieving off]`.

## Native labor shifts

`[PlayerBots labor miner]`, `[PlayerBots labor lumberjack]`, and `[PlayerBots labor blacksmith]` create one ten-minute worker at the GM's location. Miners and lumberjacks use ServUO's target-by-resource harvest macro against only an adjacent valid mountain/tree tile, so the shard controls resources, skill rolls, tool wear, and pack delivery. Blacksmiths call the native craft item pipeline for daggers, consume only real iron ingots already in their pack, and must stand by the usual forge/anvil. Labor is not part of the regular population and stops cleanly after its shift. Start the test beside the appropriate world resource or workshop; the mod never creates ore, logs, ingots, or crafted goods directly.

## Death and corpse recovery

When a PlayerBot dies, its exact ServUO corpse serial is recorded for ten minutes. After the existing resurrection flow, the bot walks back if necessary and calls the engine's owner self-loot path on only that corpse. The engine decides capacity, restores equipped and backpack items, and leaves any remaining items in place. A deleted, inaccessible, or expired corpse clears the recovery state without replacing lost items.

## Native bot-only parties

On UOR-or-later shards, `[PlayerBots party [2-10]` forms a normal ServUO `Party` from at least two living, unpartied PlayerBots within 18 tiles of the GM. The party is intentionally transient and ends after 15 minutes or when its leader is lost. Members use the existing native combat and movement paths, with followers closing on their leader. The mod does not invent party damage, loot, healing, chat, or packets. Pre-UOR shards reject the command because native player parties did not exist in that era. A real player can opt in by standing within 18 tiles of a party leader, using `[JoinBotParty`, then using ServUO's normal `/accept`; the bots never send unsolicited invitations.

A player can instead lead a party by standing beside an eligible bot and using `[LeadBotParty`. Each use recruits one nearby bot through ServUO's native Party object. Player-led bots follow the player on the same facet and assist a valid combat target; they never teleport across a map or recruit players automatically.

## Native bot guild

`[PlayerBots guild [2-10]` creates or extends the persistent ServUO guild `PlayerBot Fellowship [PBF]` using nearby living, unguilded PlayerBots. It uses the engine's normal guild roster and notoriety rules. A player can join only by standing beside a living Fellowship bot and running `[JoinBotGuild`; the command rejects already-guilded players. Bots never solicit or change real-player membership themselves.

This is not endorsed by the ServUO project. Keep it in `Scripts/Custom/PlayerBots` so it remains a removable shard mod. Upstream attribution: https://github.com/Klein187/uo-offline, MIT License.
