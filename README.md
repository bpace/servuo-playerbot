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

The mod provides headless PlayerMobile bots, city travel, chat, banking, nearby creature combat, murder reporting, resurrection, a tokenless LAN dashboard, and rendered facet maps. The dashboard follows the UO Bot World map-editor interaction model with a larger sharp terrain view, drag pan, wheel/button zoom, a Layers panel, bot search, density overlay, bot inspection, role census, facet analytics, and event/census views. It can display authored waypoints, destinations, zones, spawn definitions, dungeon links, and optional direct destination links. Links are visual destination relationships, not pathfinding guarantees. It has independent targets for each facet, immediate and selected-facet removal controls, and a ServUO-owned XML-backed editor for waypoints, destinations, rectangular zones, spawn definitions, and dungeon links, plus safe route-data reload. Materialize stored spawns fills only missing bots for saved definitions; it does not delete unrelated bots. A dungeon link moves a bot through explicitly authored entrance/interior destinations, keeps it inside for a short period, then returns it to the entrance. The first expansion target is Trammel.
