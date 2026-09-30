# ServUO PlayerBot

ServUO pub57 / Age of Shadows PlayerBot mod. It is a ServUO-native implementation inspired by Klein187's [UO Offline PlayerBots](https://github.com/Klein187/uo-offline), not a direct code port. UO Offline targets ModernUO, so upstream commits must be reviewed and adapted rather than merged into this repository.

## Install

Copy `Scripts/Custom/PlayerBots` into the target ServUO checkout, then build the shard. The mod starts disabled with a zero population. Use the LAN dashboard or `[PlayerBots` GM command to set a population and enable it.

## Dependencies and attribution

This mod targets [ServUO pub57](https://github.com/ServUO/ServUO/tree/pub57). Its design and attribution source is [Klein187/uo-offline](https://github.com/Klein187/uo-offline), released under the [MIT License](https://github.com/Klein187/uo-offline/blob/main/LICENSE). The upstream MIT text retained for attribution is at `Scripts/Custom/PlayerBots/UPSTREAM-LICENSE.txt`.

The mod's own source is MIT licensed under [LICENSE](LICENSE).

## Upstream workflow

`C:\dev\uo-offline\_Klein187` is the local checkout that tracks Klein187's repository. After its GitHub fork is created, that checkout should use `origin` for the personal fork and `upstream` for `Klein187/uo-offline`.

This repository is independent. Its `uo-offline-upstream` remote is for research and review only; do not merge it. Its `servuo-upstream` remote tracks the server API surface. Review upstream PlayerBots changes, port only compatible behavior into this mod, test against the chosen ServUO revision, then commit and push this repository to its own GitHub remote. Use [UPSTREAM-CONVERSION-PIPELINE.md](UPSTREAM-CONVERSION-PIPELINE.md) and `tools/Sync-Upstream.ps1` for every upstream review.

## Current scope

The mod provides headless PlayerMobile bots, city travel, chat, banking, nearby creature combat, murder reporting, resurrection, a tokenless LAN dashboard, and rendered facet maps. The dashboard follows the UO Bot World map-editor interaction model with a larger sharp terrain view, drag pan, wheel/button zoom, an exact cursor-coordinate readout, a Layers panel, bot search, density overlay, bot inspection, role census, facet analytics, and event/census views. It can display shard reference cities plus authored waypoints, destinations, zones, spawn definitions, dungeon links, and optional direct destination links. Reference cities are visual orientation aids, not authored route data. Links are visual destination relationships, not pathfinding guarantees. It has independent targets for each facet, immediate and selected-facet removal controls, and a ServUO-owned XML-backed editor for waypoints, destinations, rectangular zones, spawn definitions, and dungeon links, plus safe route-data reload. Materialize stored spawns fills only missing bots for saved definitions; it does not delete unrelated bots. A dungeon link moves a bot through explicitly authored entrance/interior destinations, keeps it inside for a short period, then returns it to the entrance. The first expansion target is Trammel.

## Extension modules

The base mod has a behavior lifecycle seam in `PlayerBotBehaviorRegistry`. A self-contained add-on may register a `PlayerBotBehavior` from its own `Initialize` method. Higher-priority behavior runs before the built-in travel behavior, while the base mod retains the one scheduler, population control, dashboard queue, and headless-actor safety rules.

Native parties are available only on UOR-or-later shards. Bots can form short-lived native parties, join a real player's party only after that player explicitly requests it, assist eligible leader targets, and use real bandages already in their own packs. A dead bot leader causes a bot-only managed party to disband and reform around a living bot because ServUO's native leader is immutable. A bot leaves a player-led party when that leader dies, so neither kind of group silently resumes a player party after death. Follower catch-up uses the same bounded collision-checked local planner used for verified route endpoints; it never teleports a straggler to its leader. Pre-UOR shards suppress party behavior instead of emulating party packets.

The native `PlayerBot Fellowship [PBF]` may use ServUO's normal guild-chat path from its regular bank sitters. It is a low-frequency Fellowship status line, with an in-memory two-to-five-minute per-bot cooldown. Only an online player who deliberately joined PBF can receive it; it never impersonates a player or creates a custom packet path. Hiding and stealth bank macroers use ServUO's native skill handlers, not direct hidden-state toggles, so ordinary skill checks, armor limits, cooldowns, permitted stealth steps, and reveals remain in force.

`[PlayerBots labor miner]`, `[PlayerBots labor lumberjack]`, `[PlayerBots labor blacksmith]`, `[PlayerBots labor carpenter]`, `[PlayerBots labor fisher]`, and `[PlayerBots labor cooker]` create one ten-minute worker at the GM's location. Miners and lumberjacks use ServUO's target-by-resource harvest macro against an adjacent valid mountain or tree tile. Fishers use ServUO's normal Fishing pole and target path against a nearby valid water tile, leaving range, fish-bank, skill, catch, pack, and pole-wear rules to the engine. A nearby active blacksmith pays one gold per unit for a miner's real ore, a nearby carpenter pays the same for a lumberjack's real logs, and a nearby cooker pays the same for a fisher's real catch. These handoffs use ServUO stack movement so materials and gold are conserved. Cooks use native Fish carving and the `DefCooking` FishSteak recipe, including its normal heat-source rule. At the end of a shift, a worker only hauls its output toward an imported Bank destination when the validated graph can plan the route. A smith or carpenter may sell a real finished dagger or wooden shield to a nearby funded Hawker at ServUO's existing vendor buyback value. A fisher or cooker may likewise sell real fish or raw fish steaks using the Fisherman's 1-gold buyback and 6/3-gold retail values. The Hawker resells that same item at the matching standard vendor price. Cooked fish steaks have no corresponding deployed vendor-list price, so they remain banked rather than receiving an invented price. Otherwise, workers deposit only within range of a live Banker. Labor creates no ore, logs, ingots, boards, fish, food, or finished goods directly.

Keep networked or narrative features outside the base folder. A future LLM hero/villain package can provide companions, rival thieves, and named PKs without making an API key, internet connection, or a third-party server script a requirement for ordinary PlayerBots installations. Any borrowed source must retain its license and visible attribution in both the add-on and release notes.
