# PlayerBot feature guide

This is the operating reference for the ServUO pub57 PlayerBot mod. It is a ServUO-native implementation inspired by [UO Offline PlayerBots](https://github.com/Klein187/uo-offline), not a code port. The mod uses persistent, headless `PlayerMobile` actors and ServUO's normal world, combat, item, bank, party, guild, and save rules.

## Population and roles

`[PlayerBots population <0-250>` sets Felucca's maintained roaming-population target. `[PlayerBots on` enables reconciliation and `[PlayerBots off` pauses it. The normal random population contains Travelers, Bankers, Adventurers, Townies, and occasional Tamers. It does not create Player Killers, Thieves, or Treasure Hunters as generic population.

`[PlayerBots spawn [count] [role]` immediately creates one to one hundred bots close to the GM. Valid roles are `Traveler`, `Banker`, `Adventurer`, `Townie`, `PlayerKiller`, `Thief`, `Tamer`, and `TreasureHunter`. Use manual spawning for controlled tests, not routine population.

`[PlayerBots generate` materializes every missing bot from saved spawn definitions. `[PlayerBots roadpks` materializes only saved `PlayerKiller` definitions. A road PK definition is accepted only for an unguarded Felucca location. Player Killers engage nearby real players through normal ServUO combat and notoriety. They do not target PlayerBots. Thieves are inert unless a GM deliberately runs the consent-only thief test below.

`[PlayerBots remove` deletes every PlayerBot. `[PlayerBots status` shows the combined target, live count, and enabled state.

## Facets and expansion rules

The legal facets follow the shard's configured expansion: T2A allows Felucca; UOR adds Trammel; UOTD adds Ilshenar; AOS adds Malas; SE adds Tokuno; SA and later adds Ter Mur. ML, HS, TOL, and EJ add no stock facet. Dashboard and world-data operations reject an unavailable facet instead of redirecting it to Felucca.

The in-game population command manages Felucca. Use the dashboard to select another legal facet and set its population target. Tokuno and Ter Mur need staff-authored, validated destinations or stored spawns before generic population can use them.

## Dashboard

The optional LAN dashboard starts with the shard. Its config is `Config/PlayerBotsDashboard.cfg`:

```text
BindAddress=<shard LAN address>
Port=<TCP port above 1024>
AllowedAddress=<one administrator LAN address>
```

The server writes this file at first start. Keep it on a private network. The dashboard has no browser token, URL secret, cookie, or browser storage. It accepts requests only from `AllowedAddress` and queues changes for the shard timer, so HTTP threads never mutate the world directly.

Use the facet picker before setting a target, spawning, removing a facet's bots, or editing data. The map supports pan, zoom, exact cursor coordinates, bot search, inspection, density, census, events, and visual layers for cities, authored waypoints, destinations, zones, stored spawns, and dungeon links. Reference cities and direct links are orientation aids, not proof that a route is traversable.

The editor writes the mod's XML-backed world data. Add a waypoint or destination only at a real walkable coordinate. Add zones for authored behavior areas. Add stored spawns when a bot must exist at a specific point, role, and count. `Materialize stored spawns` makes only missing entries and never removes unrelated bots. Use `Reload world data` after an external XML edit.

Dungeon links connect a named entrance destination and interior destination. Run a dungeon audit before relying on them. A bot uses the actual verified transition, remains inside briefly, and returns by the same authored route. The editor never invents a teleporter or moves a bot through a wall.

## Navigation diagnostics

`[PlayerBots audit [facet]` starts a read-only route audit for the current or named legal facet. `[PlayerBots dungeonaudit [facet]` checks dungeon entrance pads. `[PlayerBots dungeontest [facet]` and `[PlayerBots partydungeontest [facet]` run the corresponding native trip tests after PlayerBots is enabled and an audited route exists.

`[PlayerBots stuck` lists the latest clustered native movement failures. It is telemetry only. It never teleports a bot, changes collision, or rewrites a route.

`[BotWhere [name]]` or `[PlayerBots where [name]]` lists matching bot locations, regions, and destinations. `[BotGoto <name>]` or `[PlayerBots goto <name>]` moves only the GM to a matching bot. `[BotInfo]` or `[PlayerBots info]` targets a nearby bot and opens a read-only inspector with its role, state, location, stats, and non-zero skills.

## Travel, banks, combat, death, and social activity

Roaming bots select authored destinations and use verified local or graph routes. On arrival they can wander locally, visit banks or taverns, chat, and bank real gold through a nearby native Banker. Bank hubs use the current expansion's legal facets. Hawkers trade only real marked goods and standard vendor pricing. They do not fabricate stock, materials, or currency.

Bank fixtures can be regular sitters, Hawkers, AFK sitters, or native Resist, Hiding, and Stealth macroers. Hiding and Stealth bots call ServUO's normal skill handlers. Resist bots cast only real low-circle self spells when they have the required mana and reagents. A bot that cannot use its assigned native skill acts as a normal sitter instead. Tavern bots can retell their own bot-party, guild, and treasure events; they do not impersonate players or invent transactions.

Bots use nearby creature combat through ServUO's ordinary combat and notoriety paths. A native murder report can add temporary heat to the closest authored destination. Heat only makes ordinary destination selection less likely. It never creates guard calls, alarms, teleports, or player-target scans.

When a bot dies, it records its own corpse for up to ten minutes. After its regular resurrection flow, it walks back and uses the engine's owner self-loot path. The server controls capacity and retained items. Deleted, inaccessible, or expired corpses are not replaced.

## Parties and guilds

Parties require UOR or later. `[PlayerBots party [2-10]` forms a normal ServUO party from nearby eligible bots. It ends after fifteen minutes or after leader loss. A player can request a nearby eligible bot with `[LeadBotParty`, then use the normal `/accept` flow. `[JoinBotParty` lets a nearby player opt into a bot-led party. Bots never send unsolicited player invitations, cross facets, or teleport to catch up.

Idle Travelers and Adventurers who independently reach the same tavern can form small bot-only groups. They must be unpartied, out of combat, and free of labor or recovery. A group can use an audited dungeon trip, otherwise it keeps to ordinary travel. Previous companions are a ranking preference only and never bypass eligibility.

`[PlayerBots guild [2-10]` creates or extends the persistent `PlayerBot Fellowship [PBF]` from nearby, living, unguilded bots. `[JoinBotGuild` lets a nearby unguilded player join through the native guild roster. Fellowship members can recruit eligible bot Travelers or Adventurers at taverns. Guild membership works on every supported era, while native Fellowship guild chat requires SE plus ServUO's new guild system. Earlier eras use ordinary local bank speech.

## Labor, commerce, taming, and treasure

`[PlayerBots labor miner|lumberjack|blacksmith|carpenter|fisher|cooker]` starts one ten-minute worker at the GM's location. Labor is separate from normal population. Start a worker only where its native action can work: a miner by a mountain, lumberjack by a tree, blacksmith by a forge and anvil, carpenter by the required workshop, fisher beside valid water, and cooker by a native heat source.

The worker uses ServUO's harvest, fishing, carving, cooking, crafting, item movement, and banking paths. A miner or lumberjack can use a persisted pack animal. Nearby workers can exchange real ore, logs, fish, and finished goods. Hawkers buy and resell supported real goods at existing vendor buyback and retail values. The system does not mint ore, logs, ingots, boards, fish, food, finished goods, or gold.

`[PlayerBots treasure` transfers one real unfinished `TreasureMap` from the GM's backpack to a nearby Treasure Hunter on that map's facet. The bot uses ServUO's native decode, dig, chest, guardian, and loot flow. It does not create maps, sites, chests, guardians, or rewards. Fishers can sell a real recovered treasure map to a nearby Hunter through the same bounded bot-to-bot system.

Tamers are a sparse regular-population role. A Tamer uses native Animal Taming on eligible wild creatures, controls one pet through ServUO ownership, and releases it when its bounded lifecycle ends. The mod does not create cosmetic mounts or take over player pets.

## Consent-only thief test

This test is disabled on every shard start. It is not roaming theft. A GM must run `[PlayerBots thieving on`, place a live Thief bot next to one named consenting, non-staff player in Felucca, and put a disposable top-level backpack item weighing ten stones or less on that player. Run `[PlayerBots thievingtest <player name>]` once. It invokes the native Stealing cursor once, clears the selected target before the next bot tick, and never moves an item directly. Check the item, criminal state, and server log, then run `[PlayerBots thieving off`.

## Extension seam and scope

`PlayerBotBehaviorRegistry` is the supported extension seam. A separate package can register a `PlayerBotBehavior` from its own `Initialize` method. Higher-priority behavior runs before normal travel, but the base mod keeps the scheduler, population control, dashboard queue, and headless-actor rules.

Keep LLM, internet-connected, or narrative packages outside this folder. This base mod has no API key, network-service, or third-party-script requirement.
