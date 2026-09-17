# PlayerBots for ServUO pub57 / AoS

ServUO-native PlayerBots v1. It is inspired by the MIT-licensed PlayerBots system in Klein187/uo-offline, but is a fresh implementation for ServUO's net48 runtime and APIs.

The system adds persistent headless `PlayerMobile` characters. They roam between cities, use a visible moongate-style long trip, chat, bank gold, seek monsters, fight using ServUO combat rules, report murders, and resurrect after a delay. It is disabled by default and caps the management command at 250.

Use `[PlayerBots status`, `[PlayerBots spawn 5`, `[PlayerBots population 50`, `[PlayerBots on`, `[PlayerBots off`, and `[PlayerBots remove` as a GameMaster. Start with `[PlayerBots population 10`, then `[PlayerBots on` on the AoS shard and inspect CPU, save time, pathing, banks, and murder reporting before increasing it.

## Local dashboard

The mod also provides a LAN dashboard with no browser token, URL secret, cookie, or browser storage. On first startup it creates `Config/PlayerBotsDashboard.cfg`; its `BindAddress`, `Port`, and `AllowedAddress` determine access. Defaults are `192.168.50.139`, port `8081`, and the shard owner's LAN address `192.168.50.81`. Open `http://192.168.50.139:8081/` from that allowed device.

It shows the bot census, a coordinate map view, current target, bot state, and the recent event log. It queues enable, disable, population, spawn, and remove requests for the shard's own PlayerBots timer, so HTTP request threads never modify the game world directly. Keep the dashboard on a private network. The canvas is a live coordinate view, not a copy of Ultima Online map art.

This is not endorsed by the ServUO project. Keep it in `Scripts/Custom/PlayerBots` so it remains a removable shard mod. Upstream attribution: https://github.com/Klein187/uo-offline, MIT License.
