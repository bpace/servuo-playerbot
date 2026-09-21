# UO Offline Bot World overlay: dotted-line semantics

Research scope: local upstream checkout only, `C:\dev\uo-offline\_Klein187`. No ServUO code was changed.

The dotted-looking world-spanning traces in the screenshot are not live bot movement trails, traffic lanes, destinations being selected, or a separate simulation feature. The Bot World map draws its static waypoint navigation graph whenever **Graph edges** and **Waypoints** are enabled. At a continent-scale zoom, the solid graph edges visually combine with the closely spaced cyan waypoint squares, which makes a route read as a cyan dotted line.

`tools/map/map.html:256-272` draws every graph edge from the `EDGES` payload. An edge at or under 32 tiles is solid green; 33-38 tiles is solid yellow (risky); an edge over 38 tiles is red and explicitly dashed because it exceeds the A* leg limit and bots cannot traverse it. The map legend at `tools/map/map.html:162-169` names these three diagnostics. `tools/map/map.html:273-278` separately draws each waypoint as a cyan square, producing the dotted appearance over those lines at low zoom.

The graph data is static authored navigation data, not telemetry. `tools/map/serve_map.py:235-258` reads waypoint records and turns each `Connects` relationship into one de-duplicated edge in `mapdata.json`. This is the direct source of the world-spanning lines.

There are two other intentionally dashed relationship overlays, neither of which is bot traffic. `tools/map/map.html:312-324` draws a gold dashed line from a destination arrival tile to every waypoint assigned to that arrival. `tools/map/map.html:331-340` draws a type-colored dashed tether from a dungeon teleporter destination to its configured target/landing tile, marked by a hollow square. Those would only appear where such authoring data exists.

Live behavior uses a different overlay. `playerbots/source/CustomBots/LiveMapSnapshot.cs:261-270` serializes a selected bot's planned waypoint coordinates. `tools/map/map.html:448-470` draws that selected route as continuous magenta for remaining legs and grey for completed legs. It is never drawn as cyan or dotted.

Conclusion: copying the screenshot's visual should mean adding the accepted waypoint graph and diagnostic overlays, plus optional selected-bot route visualization. It should not be interpreted as a missing traffic or “dotted path following” behavior. The UO Offline browser map is a data authoring and route-health tool; the traveling behavior comes from the bot navigation system that consumes the same waypoint graph.
