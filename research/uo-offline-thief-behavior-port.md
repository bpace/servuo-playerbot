# UO Offline ThiefBehavior port boundary

Checked 2026-09-28 against local upstream commit `7f38c7cd586dc67dc96a4857754e65987351fe2e` at `C:/dev/uo-offline/_Klein187`.

## Required behavior contract

Upstream `Behaviors/ThiefBehavior.cs` is a stateful real-theft behavior, not a visual persona. A thief selects a player-shaped mark, approaches or sneaks, filters items by engine steal constraints, calls the server's native `Stealing.OnUse` skill, and invokes its real target cursor on the selected item. The engine decides the skill outcome, criminal status, victim notoriety, guard response, stolen-item return, and skill cooldown. Source: lines 4-36, 64-115, 920-990.

The behavior maintains explicit Prowl, Approach, Case, Getaway, Hidden, Fence, Roam, and Exit states. It avoids combat, runs/hides when attacked or criminal, banks a successful haul, and has per-mark cooldowns. Source: lines 123-180, 359-486, 638-764, 1758-1811.

## ServUO adaptation rules

The current port has only a Thief persona: Stealing/Hiding skill values, dark clothing, and a dagger. It has no behavior loop. Active stealing must remain strictly `Map.Felucca`; this is already a standing port invariant.

The first honest ServUO slice must use ServUO's actual `Server.SkillHandlers.Stealing` flow against an actual item belonging to a valid nearby `PlayerMobile`, never directly move items or manufacture success. It needs persisted thief state for a mark serial, per-mark cooldown, and getaway window. It must reject staff, PlayerBots, pets, dead targets, inaccessible items, containers, insured/blessed/newbied items, items above the engine weight cap, and any non-Felucca map. A failed attempt must defer to the native engine's criminal/guard rules; the bot should then flee and hide rather than fight.

Banking a successful haul, dungeon roaming, stealth navigation, and the full mark-appraisal system are later slices. They depend on verified ServUO stealing-cursor behavior, item ownership semantics, native guild requirements, and the current port's missing general behavior registry.

## Acceptance evidence

Before deployment, compile the mod against the AoS ServUO Scripts project and use a static check that no thief entry point can run outside Felucca or bypass `Stealing.OnUse`. After deployment, test only with a disposable non-staff player character carrying a disposable eligible item, then verify the native criminal/stealing result in shard logs and dashboard state. Do not test theft against an unaware real player.

2026-10-03 decision: retain the consent-only GM test as the safe ServUO-native boundary. Autonomous upstream-style player targeting is unavailable because the source system would operate on real player inventory and cause native criminal, guard, notoriety, and item-loss consequences without explicit consent.
