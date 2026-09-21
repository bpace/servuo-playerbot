# UO Offline feature-parity audit

Scope: local primary source only. Upstream: `C:\dev\uo-offline\_Klein187\playerbots\source\CustomBots` and `C:\dev\uo-offline\_Klein187\tools\map`. Port: `C:\dev\servuo\servuo\_mods\playerbot\Scripts\Custom\PlayerBots`. This is an implementation audit, not a claim that ModernUO code can be copied into ServUO.

## Direct answers

No, the ServUO port is not feature-complete with UO Offline. It now has an intentionally narrow adaptation of the bank-sitter shape, population control, waypoint routing, native dungeon-pad trips, basic combat, murder reporting, resurrection, mounts/personas, Felucca road PK spawning, and a read-only/live dashboard. The UO Offline project has a much larger behavior ecosystem and ModernUO-specific navigation, persistence, and editor bridge layers that are not present here.

The bank wall detail is real upstream behavior, not an assumption. On spawn, 80% of `BankSitter` bots call `TryHugNearbyWall`; a wall-hugger stays unmounted, while the rest receive a camera-facing idle direction. See upstream `PlayerBot.cs:810-837`. The current port assigns every sitter a separated walkable home anywhere within 18 tiles of the authored bank coordinate, so it cannot deliberately prefer wall tiles yet. See port `PlayerBotService.cs:766-789,887-905`. This is the next required bank-sitter slice: find valid walkable tiles adjacent to the bank exterior/walls, reserve them per fixture, place the majority there, and leave a minority at social/player-watching positions.

UO Offline also deliberately separates permanent bank regulars from passing traffic. `BankFixtures` creates one persistent fixed-role spawner per Bank destination, fills it with five lifecycle-exempt sitters, scatters initial placement, and respawns replacements after two to five minutes. See upstream `BankFixtures.cs:2-17,30-55,112-171`. Its sitters have a stable scattered home and only walk back when displaced. See `BankSitterBehavior.cs:226-257,259-294,584-617`. Travelers are separate behavior instances with their own route/path state, not members of the bank fixture. The port keeps permanent bank-hub actors and makes ordinary bots choose a bank 20% of the time, but it does not yet model a distinct visitor stay, bank-box session, or a purpose-driven leave/return loop. See port `PlayerBotService.cs:704-753,629-653`.

The current port does implement the same six bank-role names and approximate role weights: Regular, Hawker, AFK, ResistMacro, HidingMacro, and StealthMacro. See port `PlayerBot.cs:18-27` and `PlayerBotService.cs:792-870`; upstream source is `BankSitterBehavior.cs:32-41,154-223`. It is still only partial behavioral parity: upstream hawkers stock and sell real items, talkers face nearby people and animate, resist macroers execute real spells and restock reagents, and hiding/stealth have their own state machines. See `BankSitterBehavior.cs:296-344,346-512,514-617`. The port currently emits fixed text, uses a generic animation for Resist, and toggles `Hidden`; it explicitly has no trade inventory. See `PlayerBotService.cs:827-859`.

## The screenshot's dotted lines

They are navigation/data diagnostics, not simulated traffic or movement trails. The map draws the static waypoint graph from `EDGES`; valid legs are solid green, near-limit legs solid yellow, and legs beyond the 38-tile A* limit red dashed. At continent zoom, the green lines plus cyan waypoint squares look dotted. See upstream `tools/map/map.html:256-278` and its legend at `162-169`; `tools/map/serve_map.py:235-258` builds those edges from authored waypoint connections.

There are also two actual dashed relationship overlays: gold destination-arrival-to-waypoint bindings and coloured dungeon-teleporter-to-landing tethers. See `tools/map/map.html:312-340`. A selected live bot's planned path is different: remaining route is solid magenta and completed route is grey. See `LiveMapSnapshot.cs:261-270` and `map.html:448-470`. The screenshot is a map/editor and route-health view, not evidence of a separate dotted-line traffic behavior.

## Major upstream layers still absent or deliberately reduced

| Layer | Upstream ownership | Port status |
| --- | --- | --- |
| Dedicated behavior objects and lifecycle transitions | `Behaviors/BehaviorRegistry.cs`, `BotLifecycleManager.cs`, `LifecycleTransitions.cs` | Reduced to one `PlayerBotService` dispatcher and coarse `PlayerBotRole`; no general attach/detach behavior registry or lifecycle population/session model. |
| Visitor, shopper, crafter, gatherer, street-character, corpse-reclaim, dungeon-crawler behaviors | `Behaviors/VisitorBehavior.cs`, `ShopperBehavior.cs`, `CrafterBehavior.cs`, `GathererBehavior.cs`, `StreetCharacterBehaviors.cs`, `CorpseReclaimBehavior.cs`, `DungeonCrawlerBehavior.cs` | Not implemented as behaviors. The port has basic local wandering, banking destination choice, and a constrained native dungeon trip, but not the economics/activity loops. |
| Real player commerce | `BotShop.cs`, `BotShopDeal.cs`, `BotTradeWindow.cs`, `BotBuyOffer.cs`, `BotBanking.cs` | Missing. This is why port hawkers must not claim to sell a real stocked item. |
| Social groups, parties, guilds, factions, duels and social graph | `BotSocialGraph.cs`, `BotPartySystem.cs`, `BotPlayerParty.cs`, `BotGuilds.cs`, `BotFactionWar.cs`, `BotDuelSystem.cs` | Missing. |
| Thief gameplay | `Behaviors/ThiefBehavior.cs`, `BotThiefTest.cs` | Port has a Felucca thief persona/skills but no stealing behavior loop. |
| Gathering, taming, pack animals, housing, sea events, treasure hunts | `GatherSpots.cs`, `BotTaming.cs`, `BotPackAnimal.cs`, `BotHousing.cs`, `BotSeaEvents.cs`, `BotTreasureHunts.cs` | Missing. |
| Full navigation stack | `Nav/PathFollower.cs`, `HpaGraph.cs`, `DistanceField.cs`, `DestinationFieldCache.cs`, `Walkable.cs` | Deliberately not ported. The port uses accepted authored short legs and local ServUO checks (`PlayerBotWorldData.cs:276-353`), which is safer but not equivalent obstacle/path planning. |
| World-map authoring/reload bridge | `LiveMapSnapshot.cs`, `EditorReloadWatcher.cs`, `tools/map/serve_map.py` | Reduced dashboard only. The port has owned XML world data and inspection/audit controls, not upstream's JSON/editor file bridge or full authoring tool. |

## Recommended priority

Implement the UO Offline bank-wall fixture rule first, then a real visitor/Traveler behavior split. That produces the visual the user described: most permanent bank users on walls opening boxes, some idle people-watchers, and a separate stream that crosses the space on real errands. Building shop, guild, or full HPA navigation before that would add systems without fixing the bank scene.

