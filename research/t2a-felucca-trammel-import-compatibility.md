# UO Offline T2A navigation-data compatibility

## Conclusion

Import the UO Offline graph as **candidate data**, not as already-valid live routes. Its captured coordinates fit the AoS shard's Felucca coordinate domain: all 3,952 waypoints fall within X `312..6123`, Y `8..4025`, and the 488 destinations within X `511..6123`, Y `8..3990`. Both are inside the target's `7168 x 4096` Felucca map. [waypoints.json](../../../../upstream/uo-offline/playerbots/data/Waypoints/waypoints.json) [destinations.json](../../../../upstream/uo-offline/playerbots/data/Destinations/destinations.json) [MapDefinitions.cs](../../../../upstream/ServUO-pub57/Scripts/Misc/MapDefinitions.cs#L27-L32)

The UO Offline graph is deliberately captured for map 0, not an abstract “T2A map”: its T2A map swap replaces `map0.mul`, `statics0.mul`, and `staidx0.mul`, and identifies those files as Felucca terrain and statics. Its own documentation confirms that the T2A `map0.mul` is `7168 x 4096`. [T2A-MAP.md](../../../../upstream/uo-offline/docs/T2A-MAP.md#L22-L37) [T2A-MAP.md](../../../../upstream/uo-offline/docs/T2A-MAP.md#L104-L122)

ServUO registers Felucca as map/file index 0 and Trammel as map/file index 1, each `7168 x 4096`. On a normal shard only their rules differ: Felucca has `FeluccaRules`; Trammel has `TrammelRules`, which impose movement, beneficial-action, and harmful-action restrictions. [MapDefinitions.cs](../../../../upstream/ServUO-pub57/Scripts/Misc/MapDefinitions.cs#L27-L32) [Map.cs](../../../../upstream/ServUO-pub57/Server/Map.cs#L121-L129)

Therefore, copying each accepted Felucca coordinate unchanged to Trammel is the correct **coordinate transform**. Do not call the two facets byte-identical. They are separate server file indices, and the UO Offline source itself explains that T2A terrain/statics differ materially from modern client data, including localized static-object changes. That is enough to make a waypoint's saved Z, a short edge, a door crossing, or a teleporter transition unsafe until tested on the actual AoS facet. [MapDefinitions.cs](../../../../upstream/ServUO-pub57/Scripts/Misc/MapDefinitions.cs#L27-L32) [T2A-MAP.md](../../../../upstream/uo-offline/docs/T2A-MAP.md#L104-L128)

“Felucca” is the later facet name, not the historical T2A name. The useful operational statement is: UO Offline's T2A data was captured against its map-0 Britannia/Felucca terrain, and its coordinates are in bounds for the AoS shard's Felucca.

## Import policy

1. Parse the UO Offline files as a separate immutable source set. Preserve original names, XYZ values, directed connections, destination type, and source facet `Felucca`; do not overwrite the manually-authored store.
2. Validate every candidate node on AoS Felucca using server truth: in bounds, standable at the source Z or a verified replacement Z, and not in a blocked/forbidden region. Validate every edge by an actual traversal/path query, including doors and teleporter behavior. Quarantine failures with a reason instead of silently fixing coordinates.
3. Materialize destinations only after their linked waypoint/arrival path validates. In particular, dungeon rooms, ascents/descents, and entrances must pass transition validation, not merely XY validation.
4. After Felucca acceptance, make a second candidate set for Trammel by retaining identical XY(Z only if revalidated) and re-running the same node/edge/destination checks against `Map.Trammel`. Reject all `pk_spawns.json` records for Trammel. The source file contains 12 red-spawn definitions; it is not graph data and must never be inferred from route records. [pk_spawns.json](../../../../upstream/uo-offline/playerbots/data/CustomSpawns/pk_spawns.json) [PlayerBotWorldData.cs](../Scripts/Custom/PlayerBots/PlayerBotWorldData.cs#L289-L302)

5. Keep the existing AoS policy authoritative: Player Killer spawns are Felucca-only and must also meet the server's unguarded-Felucca rule. A coordinate mirror does not authorize PK behavior in Trammel. [PlayerBotWorldData.cs](../Scripts/Custom/PlayerBots/PlayerBotWorldData.cs#L289-L302)

## Scope and risks

The requested 3,952-waypoint/8,554-edge graph, 488 destinations, six zones, and 12 PK-spawn records are UO Offline data captured and maintained for ModernUO. The graph comments say its Z values are critical and that its legs were selected for the upstream path follower's constraints; those are not proof that ServUO's collision, item placement, region setup, or movement implementation accepts them. [waypoints.json](../../../../upstream/uo-offline/playerbots/data/Waypoints/waypoints.json#L1-L4) [UPSTREAM-CONVERSION-PIPELINE.md](../UPSTREAM-CONVERSION-PIPELINE.md)

The graph occupies the classic `6144 x 4096` portion of the target coordinate plane. No UO Offline waypoint or destination reaches the extra `X=6144..7167` strip, so this import will not add coverage there. This is expected source coverage, not a coordinate conversion defect.
