# ServUO PlayerBot

Full feature and command reference: [FEATURE-GUIDE.md](FEATURE-GUIDE.md).

## Install

1. Stop the shard and take a world-save backup.
2. Copy the complete `Scripts/Custom/PlayerBots` folder into the same path in a ServUO pub57 checkout. Do not copy individual `.cs` files.
3. Build and start the shard normally. PlayerBots starts disabled and creates no bots.
4. On first start, review `Config/PlayerBotsDashboard.cfg` before using the optional dashboard. Set `BindAddress`, `Port`, and `AllowedAddress` for the shard and the one LAN client allowed to administer it.

## Basic operation

Run these as a GameMaster in game:

```text
[PlayerBots population 10
[PlayerBots on
```

This creates and maintains ten of the mod's preprogrammed roaming bots on Felucca. Start small, check shard load and behavior, then raise the target in steps. `[PlayerBots population 50` changes the target to fifty. `[PlayerBots status` reports the current state.

To add saved, authored spawn definitions instead of random roaming bots, use `[PlayerBots generate`. To add only authored Felucca road PK definitions, use `[PlayerBots roadpks`. Both require definitions created through the dashboard's stored-spawn editor. Details are in the feature guide.

`[PlayerBots off` stops new population work but leaves existing bots in the world. `[PlayerBots remove` deletes every PlayerBot.

## Uninstall

Run `[PlayerBots off`, then `[PlayerBots remove` and confirm the reported count is zero. Stop the shard, remove `Scripts/Custom/PlayerBots`, and build again. Remove `Config/PlayerBotsDashboard.cfg` if you no longer need its LAN dashboard settings.
