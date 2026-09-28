# Next two UO Offline parity slices after graveyard Adventurer visits

## Decision

Implement tavern visits first, then rare Beggar/Newbie bank visitors. Both extend the deployed bounded-visitor model and do not need the missing transaction, stock, inventory, path-field, party, or world-authoring systems.

## 1. Tavern visitor handoff

**Scope.** Treat an authored `Tavern` destination exactly like the already-deployed Healer, Inn, Stables, and Shrine visitor destinations: an eligible ordinary arrival has an 85% chance of a one-to-three-minute visit at a separated nearby tile, produces tavern-specific small-talk or looking-for-group lines, occasionally turns or makes a small local move, then resumes ordinary routing. A declined arrival leaves immediately. Do not add vendor transactions, item transfers, gold movement, liquor consumption effects, or a permanent tavern role.

**Why this is smallest.** The ServUO port already persists all generic visitor data and runs its timed destination loop in `Scripts/Custom/PlayerBots/PlayerBotService.cs:667-758`. Its destination-kind filter omits only `Tavern` from an otherwise matching visitor family at `:770-777`; themed actions already dispatch by kind at `:800-864`. The change is therefore one eligibility case plus one themed action branch, with no save version change and no new state.

**First-party source.** UO Offline explicitly maps `Tavern` to `Visitor`, with an 85% arrival handoff and a one-to-three-minute visit in `C:/dev/uo-offline/_Klein187/playerbots/source/CustomBots/Behaviors/TravelerBehavior.cs:2654-2666`. Its `VisitorBehavior` treats a tavern as bounded small-talk/LFG/WTB presence in `.../VisitorBehavior.cs:63-67`, and returns the bot to Traveler when the visit ends at `:120-125`. The upstream destination data contains real Tavern records, including four Britain taverns, in `C:/dev/uo-offline/_Klein187/playerbots/data/Destinations/destinations_generated.json:374-407`.

**Acceptance boundary.** Existing authored Tavern points begin producing short visits. If the live world-data store has none, this feature is inert until an administrator authors one. It must not alter bank fixtures, graveyard Adventurer combat, Felucca PK/thief gates, or BotShop.

## 2. Rare Beggar/Newbie bank visitors

**Scope.** On a non-fixture bot's genuine Bank arrival, retain the current bounded bank-visitor session but give a small subset a street mode: 8% Beggar, the next 7% Newbie, and otherwise the existing bank visitor behavior. A street visitor lasts ten to twenty-five minutes, uses role-specific chatter, occasionally follows one real non-bot player within eight tiles for up to twenty-five seconds, waits four minutes before another follow attempt, then returns to its bank-home area and eventually resumes normal travel. It cannot follow staff, pets, bots, dead mobiles, or targets across facets. Permanent bank sitters remain untouched.

**Why it is next.** The deployed visitor framework already provides separated bank homes, expiry, action cadence, routing resumption, and serialization. The smallest honest adaptation needs only a `BankVisitMode` plus follow-target serial/until/cooldown state, and must extend the existing bank visitor action branch in `Scripts/Custom/PlayerBots/PlayerBotService.cs:815-821`; it does not need a separate role population, economy, new navigation graph, or authored data type. It is intentionally second because it adds persisted state and live-player target safeguards, unlike tavern visits.

**First-party source.** UO Offline makes these rare outcomes of a genuine Bank arrival: Beggar below 8%, Newbie below 15%, otherwise BankSitter, with a 40% total handoff chance, in `C:/dev/uo-offline/_Klein187/playerbots/source/CustomBots/Behaviors/TravelerBehavior.cs:2601-2619`. The shared street behavior starts a ten-to-twenty-five-minute visit at `.../StreetCharacterBehaviors.cs:38-43`, accepts only a real player within eight tiles and follows for twenty-five seconds at `:23-27` and `:85-103`, applies the four-minute cooldown at `:66-70`, and ends ordinary behavior on expiry at `:45-55`. Beggar/Newbie dialogue and distinct timing are defined at `:115-143`; the upstream behavior registry identifies them as a discrete behavior family at `.../BehaviorRegistry.cs:54-55`.

**Acceptance boundary.** A street visitor may only emerge from a non-fixture bank session and always retains a valid nearby home. It must cancel its follow on death, map change, deletion, target invalidation, or session expiry. No gold solicitation, payment, stealing, player party invite, or player-controlled trade is included.

## Explicitly deferred

BotShop is still not a safe small foundation: the upstream Shopper handoff only becomes meaningful through the separate BotShop deal, stock, bank, price, and inventory systems. Corpse recovery is also deferred because UO Offline’s corpse run assumes its full Ghost, resurrection-site, death-manager, equipment-restoration, and path-follower stack; see `C:/dev/uo-offline/_Klein187/playerbots/source/CustomBots/BotDeathManager.cs:355-710` and `.../Behaviors/CorpseReclaimBehavior.cs:27-123`. Gathering needs zone polygons, packs, yields, and delivery/economy behavior. Full dungeon crawling needs authored interiors and transition/path systems. None is comparable in size or safety to the two selected slices.
