# UO Offline graveyard versus commerce next slice

Checked 2026-09-28 against upstream commit `7f38c7cd586dc67dc96a4857754e65987351fe2e` in `C:\dev\uo-offline\_Klein187`. Sources below are upstream first-party code and data. No production code was changed.

## Decision

Build the Graveyard to Adventurer handoff next. It is a narrow extension of the port's existing route arrival and bounded-visit model. Do not start BotShop yet: its first honest slice is a real economy subsystem, not vendor dialogue or a cosmetic handoff.

## What upstream does at a graveyard

`TravelerBehavior` treats an arrival as a behavior handoff. It sends a Traveler reaching a `Graveyard` to `Adventurer` 75% of the time; if that handoff does not occur, the Traveler leaves immediately rather than idling in danger. The handoff is a 5-15 minute timed visit, and it only happens when that Traveler is the bot's active behavior. [TravelerBehavior.cs:2106-2119, 2159-2165, 2220-2243, 2621-2624](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/Behaviors/TravelerBehavior.cs#L2106-L2119)

The shared base behavior stores expiry and returns the bot to travel; `AdventurerBehavior` waits for combat to end before taking that exit. [PlayerBotBehavior.cs:70-96](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/Behaviors/PlayerBotBehavior.cs#L70-L96) [AdventurerBehavior.cs:552-556](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/Behaviors/AdventurerBehavior.cs#L552-L556)

Graveyards are authored destinations, including Britain GY's polygon, arrival points, and waypoint binding. Upstream weights them for default, healer, mage, and thief classes, while excluding crafters from graveyard and dungeon travel. [destinations.json:1600-1650](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/data/Destinations/destinations.json#L1600-L1650) [DestinationType.cs:209-215, 369-379](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/Behaviors/DestinationType.cs#L209-L215)

Full `AdventurerBehavior` is not portable as a small copy: it owns ModernUO A*-based patrol, combat posture, supply/rest, bard and stuck-recovery paths. [AdventurerBehavior.cs:2-20, 416-495, 700-949](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/Behaviors/AdventurerBehavior.cs#L416-L495)

## Why BotShop is not the next narrow slice

BotShop creates real items in the seller's backpack, tracks stock and per-buyer haggling state, and computes an asking price and floor. [BotShop.cs:100-155, 280-350, 670-708](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/BotShop.cs#L280-L350)

`BotShopDeal` then finds an eligible buyer, checks demand and banking wealth, walks the buyer to the seller, bargains on a timer, withdraws funding if needed, and transfers both the item and gold. [BotShopDeal.cs:1-20, 64-119, 341-401](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/BotShopDeal.cs#L64-L119) It also depends on banking semantics; upstream explicitly avoids inventing money and uses actual banker-range transactions. [BotBanking.cs:1-24, 59-149](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/BotBanking.cs#L59-L149) `BotEconomy` limits bank-floor deals and only initiates them from stocked bank sitters. [BotEconomy.cs:227-261](https://github.com/Klein187/uo-offline/blob/7f38c7cd586dc67dc96a4857754e65987351fe2e/playerbots/source/CustomBots/BotEconomy.cs#L227-L261)

That scope also crosses player speech and trade-window handling. A partial port that only says trade lines would repeat the fake-commerce behavior upstream explicitly replaced.

## Recommended implementation boundary

Add a ServUO-native Graveyard session for normal travelers that arrive at an imported `Graveyard` destination: use an explicit bounded 5-15 minute state, patrol only on accepted local walkable legs around the authored location, engage hostile non-player creatures through normal ServUO combat, and resume ordinary routing only after the timer has elapsed and combat is clear. Do not copy the ModernUO pathfinding/combat framework. Keep it neutral on both facets; Felucca-only gates remain for PK and active stealing. This gives visible, honest graveyard activity without claiming full Adventurer parity or creating fake commerce.
