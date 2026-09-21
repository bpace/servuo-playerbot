# UO Offline bank-behavior gap

Checked 2026-09-21 against the local UO Offline source at `C:\dev\uo-offline\_Klein187\playerbots\source\CustomBots`.

The ServUO port currently makes its bank population generic local wanderers. That is not the UO Offline model.

`BankFixtures.cs` creates a persistent fixed-role crowd for every bank. Each fixture is lifecycle-exempt and the spawner owns replacements, so a bank remains populated without being folded into the roaming-population cycle. `GenerateBotsCommand.cs` also distributes small BankSitter groups across authored bank arrival points instead of spawning a city-wide group on one point. Its spawners use `UseSpiralScan`, which avoids spawning a pile of mobiles on the same tile.

`Behaviors/BankSitterBehavior.cs` gives every sitter a role: Regular, Hawker, Afk, ResistMacro, HidingMacro, or StealthMacro. On attachment it selects a scattered home tile, then the normal roles return to that stable home when displaced. It does not make the whole bank crowd continuously wander. Regulars and hawkers speak on independent cooldowns; AFK bots remain still; macro roles have their own staggered timers. Stealth is the only role that deliberately makes short local moves.

Travel is also split from decisions. `AdventurerBehavior.cs` and `TravelerBehavior.cs` use a behavior decision tick plus a per-actor step timer, with `PathFollower` handling individual route steps. A shared global two-second move pulse is specifically too slow for walking and produces synchronized actors when every bot is serviced on that pulse.

Porting implication: retain the ServUO-native PlayerMobile and route systems, but replace the generic bank-hub wander loop with a BankSitter state layer. It needs a persistent role, a reserved home tile, independent action timing, a move-back rule, and a limited stealth-only drift rule. The existing bank-hub source marker can serve as the fixture owner until a serializable ServUO spawner item is added.
