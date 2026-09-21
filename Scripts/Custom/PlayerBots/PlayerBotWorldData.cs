using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Server.Items;
using Server.Regions;
using CalcMoves = Server.Movement.Movement;

namespace Server.CustomBots
{
    // ServUO-owned authoring data. This deliberately does not consume the
    // ModernUO editor's JSON files: the two engines have different routing
    // contracts and must remain independently upgradeable.
    [XmlRoot("PlayerBotWorld")]
    public sealed class PlayerBotWorldDataFile
    {
        [XmlAttribute("enabled")]
        public bool Enabled;

        [XmlArray("Waypoints")]
        [XmlArrayItem("Waypoint")]
        public List<PlayerBotWaypoint> Waypoints = new List<PlayerBotWaypoint>();

        [XmlArray("Destinations")]
        [XmlArrayItem("Destination")]
        public List<PlayerBotDestination> Destinations = new List<PlayerBotDestination>();

        [XmlArray("Zones")]
        [XmlArrayItem("Zone")]
        public List<PlayerBotZone> Zones = new List<PlayerBotZone>();

        [XmlArray("Spawns")]
        [XmlArrayItem("Spawn")]
        public List<PlayerBotSpawn> Spawns = new List<PlayerBotSpawn>();

        [XmlArray("DungeonLinks")]
        [XmlArrayItem("Link")]
        public List<PlayerBotDungeonLink> DungeonLinks = new List<PlayerBotDungeonLink>();
    }

    public sealed class PlayerBotWaypoint
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string Facet;
        [XmlAttribute] public int X;
        [XmlAttribute] public int Y;
        [XmlAttribute] public int Z;

        [XmlArray("Connects")]
        [XmlArrayItem("Name")]
        public List<string> Connects = new List<string>();
    }

    public sealed class PlayerBotDestination
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string Facet;
        [XmlAttribute] public string Kind;
        [XmlAttribute] public int X;
        [XmlAttribute] public int Y;
        [XmlAttribute] public int Z;
        [XmlAttribute] public string NearestWaypoint;
    }

    public sealed class PlayerBotZone
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string Facet;
        [XmlAttribute] public string Kind;

        [XmlArray("Points")]
        [XmlArrayItem("Point")]
        public List<PlayerBotZonePoint> Points = new List<PlayerBotZonePoint>();
    }

    public sealed class PlayerBotZonePoint
    {
        [XmlAttribute] public int X;
        [XmlAttribute] public int Y;
    }

    public sealed class PlayerBotSpawn
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string Facet;
        [XmlAttribute] public string Role;
        [XmlAttribute] public int X;
        [XmlAttribute] public int Y;
        [XmlAttribute] public int Z;
        [XmlAttribute] public int Count;
    }

    public sealed class PlayerBotDungeonLink
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string Facet;
        [XmlAttribute] public string EntranceName;
        [XmlAttribute] public string InteriorName;
    }

    public static class PlayerBotWorldData
    {
        private sealed class RouteLeg
        {
            public PlayerBotWaypoint From;
            public PlayerBotWaypoint To;
        }

        private sealed class RouteAudit
        {
            public string Facet;
            public int Nodes;
            public int InvalidNodes;
            public int Edges;
            public int ValidEdges;
            public int InvalidEdges;
            public int NextLeg;
            public bool Complete;
            public readonly List<RouteLeg> Pending = new List<RouteLeg>();
            public readonly HashSet<string> Accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly object Sync = new object();
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PlayerBotWorldDataFile));
        private static PlayerBotWorldDataFile _data = new PlayerBotWorldDataFile();
        private static readonly Dictionary<string, RouteAudit> RouteAudits = new Dictionary<string, RouteAudit>(StringComparer.OrdinalIgnoreCase);

        private static string PathName
        {
            get { return Path.Combine(Core.BaseDirectory, "Data", "PlayerBots", "world-data.xml"); }
        }

        public static void Initialize()
        {
            Reload();
        }

        public static void Reload()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(PathName))
                    {
                        SaveLocked();
                        return;
                    }
                    using (var stream = File.OpenRead(PathName))
                    {
                        _data = (PlayerBotWorldDataFile)Serializer.Deserialize(stream) ?? new PlayerBotWorldDataFile();
                    }
                }
                catch (Exception e)
                {
                    _data = new PlayerBotWorldDataFile();
                    Console.WriteLine("[PlayerBots] Could not load world-data.xml: " + e.Message);
                }
            }
        }

        public static bool IsEnabled
        {
            get
            {
                lock (Sync) return _data.Enabled;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            lock (Sync)
            {
                _data.Enabled = enabled;
                SaveLocked();
            }
        }

        public static bool AddWaypoint(string name, string facet, int x, int y, int z, out string message)
        {
            return AddPoint(true, name, facet, "", x, y, z, out message);
        }

        public static bool AddDestination(string name, string facet, string kind, int x, int y, int z, out string message)
        {
            return AddPoint(false, name, facet, kind, x, y, z, out message);
        }

        private static bool AddPoint(bool waypoint, string name, string facet, string kind, int x, int y, int z, out string message)
        {
            name = (name ?? "").Trim();
            var map = PlayerBotService.GetMap(facet);
            if (name.Length == 0 || name.Length > 64)
            {
                message = "Name must be between 1 and 64 characters.";
                return false;
            }
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            {
                message = "Point is outside " + map.Name + ".";
                return false;
            }
            lock (Sync)
            {
                if (waypoint)
                {
                    foreach (var point in _data.Waypoints)
                        if (String.Equals(point.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            message = "A waypoint already uses that name.";
                            return false;
                        }
                    _data.Waypoints.Add(new PlayerBotWaypoint { Name = name, Facet = map.Name, X = x, Y = y, Z = z });
                }
                else
                {
                    foreach (var point in _data.Destinations)
                        if (String.Equals(point.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            message = "A destination already uses that name.";
                            return false;
                        }
                    _data.Destinations.Add(new PlayerBotDestination { Name = name, Facet = map.Name, Kind = String.IsNullOrEmpty(kind) ? "Town" : kind, X = x, Y = y, Z = z });
                }
                SaveLocked();
            }
            message = (waypoint ? "Waypoint" : "Destination") + " saved and reloaded.";
            return true;
        }

        public static PlayerBotDestination RandomDestination(Map map)
        {
            return RandomDestination(map, null);
        }

        // Roles use this narrow selector to create a recognizable town
        // presence.  In particular, bankers should be found at banks rather
        // than visiting every service point with the rest of the population.
        public static PlayerBotDestination RandomDestination(Map map, string requiredKind)
        {
            lock (Sync)
            {
                var matches = new List<PlayerBotDestination>();
                foreach (var destination in _data.Destinations)
                    if (map != null && String.Equals(destination.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                        && (String.IsNullOrEmpty(requiredKind) || String.Equals(destination.Kind, requiredKind, StringComparison.OrdinalIgnoreCase))
                        && !String.Equals(destination.Kind, "DungeonRoom", StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(destination.Kind, "DungeonAscend", StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(destination.Kind, "DungeonDescend", StringComparison.OrdinalIgnoreCase)) matches.Add(destination);
                return matches.Count == 0 ? null : matches[Utility.Random(matches.Count)];
            }
        }

        // The bank-hub service consumes a snapshot so it never retains the
        // XML store lock while creating or moving mobiles.
        public static List<PlayerBotDestination> GetDestinations(Map map, string requiredKind)
        {
            var matches = new List<PlayerBotDestination>();
            if (map == null || String.IsNullOrEmpty(requiredKind)) return matches;

            lock (Sync)
            {
                foreach (var destination in _data.Destinations)
                    if (String.Equals(destination.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(destination.Kind, requiredKind, StringComparison.OrdinalIgnoreCase))
                        matches.Add(destination);
            }
            return matches;
        }

        // The graph import is data-only.  This is the single seam callers use
        // to turn that data into a safe, short-leg plan for a particular AoS
        // facet.  Every node and endpoint is checked against that facet before
        // it can enter a route; the identical XY coordinate system alone is
        // not treated as proof of matching statics or collision.
        internal static bool TryPlanRoute(PlayerBot bot, PlayerBotDestination destination)
        {
            if (bot == null || destination == null || bot.Map == null || bot.Map == Map.Internal)
                return false;

            lock (Sync)
            {
                var map = bot.Map;
                if (!String.Equals(destination.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                    || !IsWalkable(map, destination.X, destination.Y, destination.Z))
                    return false;

                var nodes = new Dictionary<string, PlayerBotWaypoint>(StringComparer.OrdinalIgnoreCase);
                foreach (var waypoint in _data.Waypoints)
                {
                    if (!String.Equals(waypoint.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                        || !IsWalkable(map, waypoint.X, waypoint.Y, waypoint.Z))
                        continue;
                    nodes[waypoint.Name] = waypoint;
                }
                if (nodes.Count == 0) return false;

                PlayerBotWaypoint start = FindNearest(nodes.Values, bot.Location, 64);
                PlayerBotWaypoint end = String.IsNullOrEmpty(destination.NearestWaypoint) ? null : FindNode(nodes, destination.NearestWaypoint);
                if (end == null) end = FindNearest(nodes.Values, new Point3D(destination.X, destination.Y, destination.Z), 64);
                if (start == null || end == null) return false;

                var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pending = new Queue<string>();
                seen.Add(start.Name);
                pending.Enqueue(start.Name);
                while (pending.Count > 0 && !seen.Contains(end.Name))
                {
                    var name = pending.Dequeue();
                    var current = FindNode(nodes, name);
                    if (current == null) continue;
                    foreach (var neighborName in current.Connects)
                    {
                        var neighbor = FindNode(nodes, neighborName);
                        if (neighbor == null || seen.Contains(neighbor.Name) || !IsShortLeg(current, neighbor) || !IsAcceptedLeg(map.Name, current, neighbor)) continue;
                        seen.Add(neighbor.Name);
                        previous[neighbor.Name] = current.Name;
                        pending.Enqueue(neighbor.Name);
                    }
                }
                if (!seen.Contains(end.Name)) return false;

                var reverse = new List<PlayerBotWaypoint>();
                var cursor = end.Name;
                while (cursor != null)
                {
                    var current = FindNode(nodes, cursor);
                    if (current == null) return false;
                    reverse.Add(current);
                    string parent;
                    if (!previous.TryGetValue(cursor, out parent)) break;
                    cursor = parent;
                }
                reverse.Reverse();

                // A graph node merely gets us close to a live endpoint.  Do
                // not turn that proximity into an unchecked straight-line
                // tail: entrances often sit behind rocks, walls, or a pad
                // that requires a short detour from the road node.
                var route = new List<Point3D>();
                if (!TryAppendLocalPath(map, bot.Location, new Point3D(start.X, start.Y, start.Z), route))
                    return false;
                foreach (var point in reverse)
                    AppendRoutePoint(route, new Point3D(point.X, point.Y, point.Z));
                if (!TryAppendLocalPath(map, new Point3D(end.X, end.Y, end.Z),
                    new Point3D(destination.X, destination.Y, destination.Z), route))
                    return false;

                bot.RoutePoints.Clear();
                foreach (var point in route) bot.RoutePoints.Add(point);
                bot.RouteIndex = 0;
                return bot.RoutePoints.Count > 0;
            }
        }

        private static PlayerBotWaypoint FindNode(Dictionary<string, PlayerBotWaypoint> nodes, string name)
        {
            PlayerBotWaypoint node;
            return name != null && nodes.TryGetValue(name, out node) ? node : null;
        }

        private static PlayerBotWaypoint FindNearest(IEnumerable<PlayerBotWaypoint> nodes, Point3D point, int maximumDistance)
        {
            PlayerBotWaypoint best = null;
            var bestSquared = maximumDistance * maximumDistance;
            foreach (var node in nodes)
            {
                var dx = node.X - point.X;
                var dy = node.Y - point.Y;
                var distance = dx * dx + dy * dy;
                if (distance > bestSquared) continue;
                bestSquared = distance;
                best = node;
            }
            return best;
        }

        private static bool IsShortLeg(PlayerBotWaypoint left, PlayerBotWaypoint right)
        {
            var dx = left.X - right.X;
            var dy = left.Y - right.Y;
            return dx * dx + dy * dy <= 38 * 38;
        }

        // Starts a read-only facet audit. It never rewrites imported data:
        // after completion, only verified legs enter route plans.
        internal static string StartRouteAudit(Map map)
        {
            if (map == null || map == Map.Internal) return "No playable facet selected.";
            lock (Sync)
            {
                var nodes = new Dictionary<string, PlayerBotWaypoint>(StringComparer.OrdinalIgnoreCase);
                var audit = new RouteAudit { Facet = map.Name };
                foreach (var point in _data.Waypoints)
                {
                    if (!String.Equals(point.Facet, map.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    audit.Nodes++;
                    if (!IsWalkable(map, point.X, point.Y, point.Z)) { audit.InvalidNodes++; continue; }
                    nodes[point.Name] = point;
                }
                foreach (var point in nodes.Values)
                {
                    foreach (var neighborName in point.Connects)
                    {
                        audit.Edges++;
                        var neighbor = FindNode(nodes, neighborName);
                        if (neighbor == null || !IsShortLeg(point, neighbor)) audit.InvalidEdges++;
                        else audit.Pending.Add(new RouteLeg { From = point, To = neighbor });
                    }
                }
                RouteAudits[map.Name] = audit;
                return "Queued " + map.Name + " route audit: " + audit.Nodes + " nodes and " + audit.Edges + " directed legs.";
            }
        }

        // Called by the existing game timer. The small budget prevents a full
        // 8,554-leg audit from stalling the world thread.
        internal static string AdvanceRouteAudit(int budget)
        {
            lock (Sync)
            {
                foreach (var audit in RouteAudits.Values)
                {
                    if (audit.Complete) continue;
                    var map = PlayerBotService.GetMap(audit.Facet);
                    for (var i = 0; i < budget && audit.NextLeg < audit.Pending.Count; i++, audit.NextLeg++)
                    {
                        var leg = audit.Pending[audit.NextLeg];
                        if (IsDirectLegWalkable(map, leg.From, leg.To))
                        {
                            audit.ValidEdges++;
                            audit.Accepted.Add(LegKey(leg.From, leg.To));
                        }
                        else audit.InvalidEdges++;
                    }
                    if (audit.NextLeg >= audit.Pending.Count)
                    {
                        audit.Complete = true;
                        return audit.Facet + " route audit complete: " + audit.ValidEdges + "/" + audit.Edges + " directed legs accepted; " + audit.InvalidNodes + " nodes and " + audit.InvalidEdges + " legs rejected.";
                    }
                }
            }
            return null;
        }

        private static bool IsAcceptedLeg(string facet, PlayerBotWaypoint from, PlayerBotWaypoint to)
        {
            RouteAudit audit;
            if (!RouteAudits.TryGetValue(facet, out audit) || !audit.Complete) return false;
            return audit.Accepted.Contains(LegKey(from, to));
        }

        internal static void StartFacetAudits()
        {
            StartRouteAudit(Map.Felucca);
            StartRouteAudit(Map.Trammel);
        }

        // The imported T2A records identify the surface entrance tiles, but
        // deliberately do not claim where an AoS shard's live teleporters
        // lead.  Read the actual world items before authoring any shortcut.
        // This is diagnostic only: it neither creates DungeonLinks nor moves
        // a bot.
        internal static string AuditDungeonEntrancePads(Map map)
        {
            if (map == null || map == Map.Internal) return "No playable facet selected.";

            var lines = new List<string>();
            var entrances = new List<PlayerBotDestination>();
            lock (Sync)
            {
                foreach (var destination in _data.Destinations)
                    if (String.Equals(destination.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(destination.Kind, "DungeonEntrance", StringComparison.OrdinalIgnoreCase))
                        entrances.Add(destination);
            }

            var found = 0;
            foreach (var entrance in entrances)
            {
                Teleporter matched = null;
                var bestDistance = 3;
                var nearby = map.GetItemsInRange(new Point3D(entrance.X, entrance.Y, entrance.Z), 2);
                foreach (Item item in nearby)
                {
                    var teleporter = item as Teleporter;
                    if (teleporter == null || teleporter.Deleted) continue;
                    var distance = Math.Max(Math.Abs(item.X - entrance.X), Math.Abs(item.Y - entrance.Y));
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    matched = teleporter;
                }
                nearby.Free();

                if (matched == null)
                {
                    lines.Add(entrance.Name + " | no Teleporter within 2 tiles of " + entrance.X + "," + entrance.Y + "," + entrance.Z);
                    continue;
                }

                found++;
                var destinationMap = matched.MapDest ?? map;
                string interior;
                var routeLegs = 0;
                string returnPad = null;
                var returnLegs = 0;
                bool approach;
                lock (Sync)
                {
                    approach = HasVerifiedGraphApproachLocked(map, new Point3D(matched.X, matched.Y, matched.Z));
                    interior = FindVerifiedDungeonInteriorLocked(map, entrance, matched.PointDest, out routeLegs);
                    if (interior != null && destinationMap == map)
                        returnPad = FindVerifiedDungeonReturnPadLocked(map, entrance, matched.PointDest, out returnLegs);
                }
                lines.Add(entrance.Name + " | pad=" + matched.X + "," + matched.Y + "," + matched.Z
                    + " active=" + matched.Active + " | destination=" + matched.PointDest.X + "," + matched.PointDest.Y + "," + matched.PointDest.Z
                    + "@" + destinationMap.Name + " | approach=" + approach + " | interior=" + (interior ?? "none")
                    + (interior == null ? "" : " routeLegs=" + routeLegs)
                    + (returnPad == null ? "" : " | returnPad=" + returnPad + " returnLegs=" + returnLegs));
            }

            try
            {
                var directory = System.IO.Path.GetDirectoryName(PathName);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllLines(System.IO.Path.Combine(directory, "dungeon-pad-audit-" + map.Name + ".txt"), lines.ToArray());
            }
            catch (Exception e)
            {
                Console.WriteLine("[PlayerBots] Could not write dungeon pad audit: " + e.Message);
            }

            foreach (var line in lines) Console.WriteLine("[PlayerBots] Dungeon pad audit " + map.Name + ": " + line);
            return "Dungeon pad audit " + map.Name + ": " + found + "/" + entrances.Count
                + " imported entrance tiles have a nearby live Teleporter. Details are in Data/PlayerBots/dungeon-pad-audit-" + map.Name + ".txt.";
        }

        // A landing candidate is useful only when the shard accepts both
        // endpoints and the already-audited graph can route between them.
        // Names narrow the imported dungeon set; terrain and route audit are
        // still the authority, so this cannot manufacture a cross-map jump.
        private static string FindVerifiedDungeonInteriorLocked(Map map, PlayerBotDestination entrance, Point3D landing, out int routeLegs)
        {
            routeLegs = 0;
            var marker = entrance.Name.IndexOf(" L1 Entrance", StringComparison.OrdinalIgnoreCase);
            var dungeon = marker > 0 ? entrance.Name.Substring(0, marker) : entrance.Name;
            PlayerBotDestination best = null;
            var bestDistance = Int32.MaxValue;
            var bestLegs = 0;
            foreach (var candidate in _data.Destinations)
            {
                if (!String.Equals(candidate.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                    || !String.Equals(candidate.Kind, "DungeonRoom", StringComparison.OrdinalIgnoreCase)
                    || !candidate.Name.StartsWith(dungeon, StringComparison.OrdinalIgnoreCase)) continue;
                var distance = Math.Max(Math.Abs(candidate.X - landing.X), Math.Abs(candidate.Y - landing.Y));
                if (distance > 128 || distance >= bestDistance) continue;
                int legs;
                if (!HasVerifiedRouteLocked(map, landing, candidate, out legs)) continue;
                best = candidate;
                bestDistance = distance;
                bestLegs = legs;
            }
            routeLegs = bestLegs;
            return best == null ? null : best.Name;
        }

        // An exit is trusted only when its physical pad lies on the same
        // accepted graph component as the verified landing and its live
        // destination returns to the surface entrance area. This lets a
        // future behavior use the shard's Teleporter rather than MoveToWorld.
        private static string FindVerifiedDungeonReturnPadLocked(Map map, PlayerBotDestination entrance, Point3D landing, out int routeLegs)
        {
            routeLegs = 0;
            Teleporter best = null;
            var bestLegs = Int32.MaxValue;
            foreach (Item item in World.Items.Values)
            {
                var teleporter = item as Teleporter;
                if (teleporter == null || teleporter.Deleted || !teleporter.Active || teleporter.Map != map) continue;
                var targetMap = teleporter.MapDest ?? map;
                if (targetMap != map
                    || Math.Max(Math.Abs(teleporter.PointDest.X - entrance.X), Math.Abs(teleporter.PointDest.Y - entrance.Y)) > 12) continue;
                int legs;
                var pad = new PlayerBotDestination { X = teleporter.X, Y = teleporter.Y, Z = teleporter.Z };
                if (!HasVerifiedRouteLocked(map, landing, pad, out legs) || legs >= bestLegs) continue;
                best = teleporter;
                bestLegs = legs;
            }
            routeLegs = best == null ? 0 : bestLegs;
            return best == null ? null : best.X + "," + best.Y + "," + best.Z + "->"
                + best.PointDest.X + "," + best.PointDest.Y + "," + best.PointDest.Z;
        }

        private static bool HasVerifiedRouteLocked(Map map, Point3D startPoint, PlayerBotDestination destination, out int routeLegs)
        {
            routeLegs = 0;
            if (!IsWalkable(map, startPoint.X, startPoint.Y, startPoint.Z)
                || !IsWalkable(map, destination.X, destination.Y, destination.Z)) return false;
            var nodes = new Dictionary<string, PlayerBotWaypoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var waypoint in _data.Waypoints)
            {
                if (!String.Equals(waypoint.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                    || !IsWalkable(map, waypoint.X, waypoint.Y, waypoint.Z)) continue;
                nodes[waypoint.Name] = waypoint;
            }
            var start = FindNearest(nodes.Values, startPoint, 64);
            var end = String.IsNullOrEmpty(destination.NearestWaypoint) ? null : FindNode(nodes, destination.NearestWaypoint);
            if (end == null) end = FindNearest(nodes.Values, new Point3D(destination.X, destination.Y, destination.Z), 64);
            if (start == null || end == null) return false;
            if (!HasLocalPath(map, startPoint, new Point3D(start.X, start.Y, start.Z))
                || !HasLocalPath(map, new Point3D(end.X, end.Y, end.Z), new Point3D(destination.X, destination.Y, destination.Z)))
                return false;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>();
            var distances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            seen.Add(start.Name);
            distances[start.Name] = 0;
            pending.Enqueue(start.Name);
            while (pending.Count > 0 && !seen.Contains(end.Name))
            {
                var current = FindNode(nodes, pending.Dequeue());
                if (current == null) continue;
                foreach (var neighborName in current.Connects)
                {
                    var neighbor = FindNode(nodes, neighborName);
                    if (neighbor == null || seen.Contains(neighbor.Name) || !IsShortLeg(current, neighbor)
                        || !IsAcceptedLeg(map.Name, current, neighbor)) continue;
                    seen.Add(neighbor.Name);
                    distances[neighbor.Name] = distances[current.Name] + 1;
                    pending.Enqueue(neighbor.Name);
                }
            }
            if (!distances.TryGetValue(end.Name, out routeLegs)) return false;
            return true;
        }

        // A native entrance must be reachable from the accepted surface graph
        // through actual ServUO movement, not just lie within a radius of it.
        private static bool HasVerifiedGraphApproachLocked(Map map, Point3D point)
        {
            if (!IsWalkable(map, point.X, point.Y, point.Z)) return false;
            var nodes = new List<PlayerBotWaypoint>();
            foreach (var waypoint in _data.Waypoints)
            {
                if (String.Equals(waypoint.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                    && IsWalkable(map, waypoint.X, waypoint.Y, waypoint.Z))
                    nodes.Add(waypoint);
            }
            var nearest = FindNearest(nodes, point, 64);
            return nearest != null && HasLocalPath(map, new Point3D(nearest.X, nearest.Y, nearest.Z), point);
        }

        private static string LegKey(PlayerBotWaypoint from, PlayerBotWaypoint to)
        {
            return from.Name + "\n" + to.Name;
        }

        // Replays a leg tile by tile through the collision and diagonal check
        // used by ServUO movement. This mirrors the bot's direct leg walking.
        private static bool IsDirectLegWalkable(Map map, PlayerBotWaypoint from, PlayerBotWaypoint to)
        {
            return TryBuildDirectPath(map, new Point3D(from.X, from.Y, from.Z), new Point3D(to.X, to.Y, to.Z), 40, out _);
        }

        private static bool HasLocalPath(Map map, Point3D from, Point3D to)
        {
            var ignored = new List<Point3D>();
            return TryAppendLocalPath(map, from, to, ignored);
        }

        // Keep graph connector paths bounded. First preserve the inexpensive
        // direct-leg behavior, then use ServUO's A* only for a nearby blocked
        // endpoint such as an outdoor dungeon pad.
        private static bool TryAppendLocalPath(Map map, Point3D from, Point3D to, List<Point3D> route)
        {
            if (!IsWalkable(map, from.X, from.Y, from.Z) || !IsWalkable(map, to.X, to.Y, to.Z)
                || Math.Abs(from.X - to.X) > 64 || Math.Abs(from.Y - to.Y) > 64)
                return false;

            List<Point3D> steps;
            if (!TryBuildDirectPath(map, from, to, 64, out steps))
            {
                var path = new MovementPath(from, to, map);
                if (!path.Success || path.Directions == null || path.Directions.Length > 256) return false;
                steps = new List<Point3D>();
                var cursor = from;
                foreach (var direction in path.Directions)
                {
                    int nextZ;
                    if (!CalcMoves.CheckMovement(from, map, cursor, direction, out nextZ)) return false;
                    var x = cursor.X;
                    var y = cursor.Y;
                    CalcMoves.Offset(direction, ref x, ref y);
                    cursor = new Point3D(x, y, nextZ);
                    steps.Add(cursor);
                }
                if (cursor.X != to.X || cursor.Y != to.Y || Math.Abs(cursor.Z - to.Z) >= 16) return false;
            }
            foreach (var step in steps) AppendRoutePoint(route, step);
            return true;
        }

        private static bool TryBuildDirectPath(Map map, Point3D from, Point3D to, int maximumSteps, out List<Point3D> steps)
        {
            steps = new List<Point3D>();
            if (!IsWalkable(map, from.X, from.Y, from.Z) || !IsWalkable(map, to.X, to.Y, to.Z)) return false;
            var cursor = from;
            var safety = 0;
            while ((cursor.X != to.X || cursor.Y != to.Y) && safety++ < maximumSteps)
            {
                var direction = DirectionTo(cursor, to);
                int nextZ;
                if (!CalcMoves.CheckMovement(from, map, cursor, direction, out nextZ)) return false;
                var x = cursor.X;
                var y = cursor.Y;
                CalcMoves.Offset(direction, ref x, ref y);
                cursor = new Point3D(x, y, nextZ);
                steps.Add(cursor);
            }
            return cursor.X == to.X && cursor.Y == to.Y && Math.Abs(cursor.Z - to.Z) < 16;
        }

        private static void AppendRoutePoint(List<Point3D> route, Point3D point)
        {
            if (route.Count == 0 || route[route.Count - 1] != point) route.Add(point);
        }

        private static Direction DirectionTo(Point3D from, Point3D to)
        {
            var dx = Math.Sign(to.X - from.X);
            var dy = Math.Sign(to.Y - from.Y);
            if (dx > 0) return dy < 0 ? Direction.Right : dy > 0 ? Direction.Down : Direction.East;
            if (dx < 0) return dy < 0 ? Direction.Up : dy > 0 ? Direction.Left : Direction.West;
            return dy < 0 ? Direction.North : Direction.South;
        }

        private static bool IsWalkable(Map map, int x, int y, int z)
        {
            return map != null && x >= 0 && y >= 0 && x < map.Width && y < map.Height && map.CanFit(x, y, z, 16, false, false);
        }

        internal static PlayerBotDestination GetDestination(string name, Map map)
        {
            lock (Sync)
            {
                return map == null ? null : FindDestinationLocked(name, map.Name);
            }
        }

        internal static List<PlayerBotSpawn> GetSpawns()
        {
            lock (Sync)
            {
                var copies = new List<PlayerBotSpawn>();
                foreach (var spawn in _data.Spawns)
                    copies.Add(new PlayerBotSpawn { Name = spawn.Name, Facet = spawn.Facet, Role = spawn.Role, X = spawn.X, Y = spawn.Y, Z = spawn.Z, Count = spawn.Count });
                return copies;
            }
        }

        public static bool AddZone(string name, string facet, string kind, int x, int y, int width, int height, out string message)
        {
            name = (name ?? "").Trim();
            var map = PlayerBotService.GetMap(facet);
            width = Math.Max(1, Math.Min(512, width));
            height = Math.Max(1, Math.Min(512, height));
            if (name.Length == 0 || name.Length > 64 || x < 0 || y < 0 || x + width >= map.Width || y + height >= map.Height)
            {
                message = "Zone needs a name and a rectangle inside " + map.Name + ".";
                return false;
            }
            lock (Sync)
            {
                foreach (var zone in _data.Zones)
                    if (String.Equals(zone.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        message = "A zone already uses that name.";
                        return false;
                    }
                var zoneData = new PlayerBotZone { Name = name, Facet = map.Name, Kind = String.IsNullOrEmpty(kind) ? "Area" : kind };
                zoneData.Points.Add(new PlayerBotZonePoint { X = x, Y = y });
                zoneData.Points.Add(new PlayerBotZonePoint { X = x + width, Y = y });
                zoneData.Points.Add(new PlayerBotZonePoint { X = x + width, Y = y + height });
                zoneData.Points.Add(new PlayerBotZonePoint { X = x, Y = y + height });
                _data.Zones.Add(zoneData);
                SaveLocked();
            }
            message = "Zone saved and reloaded.";
            return true;
        }

        public static bool AddSpawn(string name, string facet, string role, int x, int y, int z, int count, out string message)
        {
            name = (name ?? "").Trim();
            var map = PlayerBotService.GetMap(facet);
            if (name.Length == 0 || name.Length > 64 || x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            {
                message = "Spawn needs a name and a point inside " + map.Name + ".";
                return false;
            }
            if (String.Equals(role, "PlayerKiller", StringComparison.OrdinalIgnoreCase) && !IsLegalRoadPkLocation(map, x, y, z))
            {
                message = "Road PK spawns must be at an unguarded Felucca location.";
                return false;
            }
            count = Math.Max(1, Math.Min(50, count));
            lock (Sync)
            {
                foreach (var spawn in _data.Spawns)
                    if (String.Equals(spawn.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        message = "A spawn already uses that name.";
                        return false;
                    }
                _data.Spawns.Add(new PlayerBotSpawn { Name = name, Facet = map.Name, Role = String.IsNullOrEmpty(role) ? "Traveler" : role, X = x, Y = y, Z = z, Count = count });
                SaveLocked();
            }
            message = "Spawn definition saved. It is data only until the spawn generator is added.";
            return true;
        }

        internal static bool IsLegalRoadPkLocation(Map map, int x, int y, int z)
        {
            if (map != Map.Felucca || map.Rules != MapRules.FeluccaRules) return false;
            var region = Region.Find(new Point3D(x, y, z), map);
            var guarded = region == null ? null : region.GetRegion(typeof(GuardedRegion)) as GuardedRegion;
            return guarded == null || guarded.IsDisabled();
        }

        public static bool AddDungeonLink(string name, string facet, string entranceName, string interiorName, out string message)
        {
            name = (name ?? "").Trim();
            entranceName = (entranceName ?? "").Trim();
            interiorName = (interiorName ?? "").Trim();
            var map = PlayerBotService.GetMap(facet);
            if (name.Length == 0 || name.Length > 64 || entranceName.Length == 0 || interiorName.Length == 0 || String.Equals(entranceName, interiorName, StringComparison.OrdinalIgnoreCase))
            {
                message = "Dungeon link needs a name plus different entrance and interior destinations.";
                return false;
            }
            lock (Sync)
            {
                var entrance = FindDestinationLocked(entranceName, map.Name);
                var interior = FindDestinationLocked(interiorName, map.Name);
                if (entrance == null || interior == null)
                {
                    message = "Create both named destinations on " + map.Name + " before linking them.";
                    return false;
                }
                foreach (var link in _data.DungeonLinks)
                    if (String.Equals(link.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        message = "A dungeon link already uses that name.";
                        return false;
                    }
                _data.DungeonLinks.Add(new PlayerBotDungeonLink { Name = name, Facet = map.Name, EntranceName = entrance.Name, InteriorName = interior.Name });
                SaveLocked();
            }
            message = "Dungeon link saved and reloaded.";
            return true;
        }

        internal static bool TryEnterDungeon(PlayerBot bot, out PlayerBotDestination interior)
        {
            interior = null;
            if (bot == null || bot.Map == null) return false;
            lock (Sync)
            {
                foreach (var link in _data.DungeonLinks)
                {
                    if (!String.Equals(link.Facet, bot.Map.Name, StringComparison.OrdinalIgnoreCase) || !String.Equals(link.EntranceName, bot.DestinationName, StringComparison.OrdinalIgnoreCase)) continue;
                    interior = FindDestinationLocked(link.InteriorName, bot.Map.Name);
                    return interior != null;
                }
            }
            return false;
        }

        // This is intentionally separate from legacy DungeonLinks.  A native
        // trip exists only when the current shard has live, active entrance
        // and return teleporters and both audit-backed graph checks succeed.
        internal sealed class NativeDungeonTrip
        {
            public PlayerBotDestination Entrance;
            public PlayerBotDestination Interior;
            public Point3D EntrancePad;
            public Point3D Landing;
            public Point3D ReturnPad;
        }

        internal static bool TryGetNativeDungeonTrip(Map map, string entranceName, out NativeDungeonTrip trip)
        {
            trip = null;
            if (map == null || map == Map.Internal || String.IsNullOrEmpty(entranceName)) return false;

            lock (Sync)
            {
                var entrance = FindDestinationLocked(entranceName, map.Name);
                if (entrance == null || !String.Equals(entrance.Kind, "DungeonEntrance", StringComparison.OrdinalIgnoreCase)) return false;

                Teleporter entry = null;
                var entryDistance = 3;
                var nearby = map.GetItemsInRange(new Point3D(entrance.X, entrance.Y, entrance.Z), 2);
                foreach (Item item in nearby)
                {
                    var teleporter = item as Teleporter;
                    if (teleporter == null || teleporter.Deleted || !teleporter.Active || (teleporter.MapDest ?? map) != map) continue;
                    var distance = Math.Max(Math.Abs(teleporter.X - entrance.X), Math.Abs(teleporter.Y - entrance.Y));
                    if (distance >= entryDistance) continue;
                    entry = teleporter;
                    entryDistance = distance;
                }
                nearby.Free();
                if (entry == null) return false;
                if (!HasVerifiedGraphApproachLocked(map, new Point3D(entry.X, entry.Y, entry.Z))) return false;

                int interiorLegs;
                var interiorName = FindVerifiedDungeonInteriorLocked(map, entrance, entry.PointDest, out interiorLegs);
                var interior = String.IsNullOrEmpty(interiorName) ? null : FindDestinationLocked(interiorName, map.Name);
                if (interior == null) return false;

                Teleporter exit = null;
                var exitLegs = Int32.MaxValue;
                nearby = map.GetItemsInRange(entry.PointDest, 128);
                foreach (Item item in nearby)
                {
                    var teleporter = item as Teleporter;
                    if (teleporter == null || teleporter.Deleted || !teleporter.Active || teleporter.Map != map
                        || (teleporter.MapDest ?? map) != map
                        || Math.Max(Math.Abs(teleporter.PointDest.X - entrance.X), Math.Abs(teleporter.PointDest.Y - entrance.Y)) > 12) continue;
                    int legs;
                    var pad = new PlayerBotDestination { X = teleporter.X, Y = teleporter.Y, Z = teleporter.Z };
                    if (!HasVerifiedRouteLocked(map, entry.PointDest, pad, out legs) || legs >= exitLegs) continue;
                    exit = teleporter;
                    exitLegs = legs;
                }
                nearby.Free();
                if (exit == null) return false;

                trip = new NativeDungeonTrip
                {
                    Entrance = entrance,
                    Interior = interior,
                    EntrancePad = new Point3D(entry.X, entry.Y, entry.Z),
                    Landing = entry.PointDest,
                    ReturnPad = new Point3D(exit.X, exit.Y, exit.Z)
                };
                return true;
            }
        }

        internal static bool TryGetAnyNativeDungeonTrip(Map map, out NativeDungeonTrip trip)
        {
            trip = null;
            if (map == null || map == Map.Internal) return false;
            var names = new List<string>();
            lock (Sync)
            {
                foreach (var destination in _data.Destinations)
                    if (String.Equals(destination.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(destination.Kind, "DungeonEntrance", StringComparison.OrdinalIgnoreCase))
                        names.Add(destination.Name);
            }
            foreach (var name in names)
                if (TryGetNativeDungeonTrip(map, name, out trip)) return true;
            return false;
        }

        private static PlayerBotDestination FindDestinationLocked(string name, string facet)
        {
            foreach (var destination in _data.Destinations)
                if (String.Equals(destination.Name, name, StringComparison.OrdinalIgnoreCase) && String.Equals(destination.Facet, facet, StringComparison.OrdinalIgnoreCase)) return destination;
            return null;
        }

        internal static void AppendDashboardJson(System.Text.StringBuilder json)
        {
            lock (Sync)
            {
                json.Append(",\"world\":{\"waypoints\":").Append(_data.Waypoints.Count)
                    .Append(",\"destinations\":").Append(_data.Destinations.Count)
                    .Append(",\"zones\":").Append(_data.Zones.Count)
                    .Append(",\"spawns\":").Append(_data.Spawns.Count).Append(",\"dungeonLinks\":").Append(_data.DungeonLinks.Count);
                json.Append(",\"waypointData\":[");
                for (var i = 0; i < _data.Waypoints.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    var point = _data.Waypoints[i];
                    json.Append("{\"n\":\"").Append(Escape(point.Name)).Append("\",\"f\":\"").Append(Escape(point.Facet))
                        .Append("\",\"x\":").Append(point.X).Append(",\"y\":").Append(point.Y).Append("}");
                }
                json.Append("],\"roadData\":[");
                var firstRoad = true;
                var waypointIndex = new Dictionary<string, PlayerBotWaypoint>(StringComparer.OrdinalIgnoreCase);
                foreach (var waypoint in _data.Waypoints) waypointIndex[waypoint.Facet + "\n" + waypoint.Name] = waypoint;
                foreach (var point in _data.Waypoints)
                {
                    foreach (var neighborName in point.Connects)
                    {
                        PlayerBotWaypoint neighbor;
                        if (!waypointIndex.TryGetValue(point.Facet + "\n" + neighborName, out neighbor)
                            || !IsShortLeg(point, neighbor) || !IsAcceptedLeg(point.Facet, point, neighbor)) continue;
                        if (!firstRoad) json.Append(',');
                        firstRoad = false;
                        json.Append("{\"f\":\"").Append(Escape(point.Facet)).Append("\",\"x\":").Append(point.X)
                            .Append(",\"y\":").Append(point.Y).Append(",\"a\":").Append(neighbor.X).Append(",\"b\":").Append(neighbor.Y).Append('}');
                    }
                }
                json.Append("],\"destinationData\":[");
                for (var i = 0; i < _data.Destinations.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    var point = _data.Destinations[i];
                    json.Append("{\"n\":\"").Append(Escape(point.Name)).Append("\",\"f\":\"").Append(Escape(point.Facet))
                        .Append("\",\"k\":\"").Append(Escape(point.Kind)).Append("\",\"x\":").Append(point.X).Append(",\"y\":").Append(point.Y).Append("}");
                }
                json.Append("],\"zoneData\":[");
                for (var i = 0; i < _data.Zones.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    var zone = _data.Zones[i];
                    json.Append("{\"n\":\"").Append(Escape(zone.Name)).Append("\",\"f\":\"").Append(Escape(zone.Facet)).Append("\",\"p\":[");
                    for (var j = 0; j < zone.Points.Count; j++)
                    {
                        if (j > 0) json.Append(',');
                        json.Append('[').Append(zone.Points[j].X).Append(',').Append(zone.Points[j].Y).Append(']');
                    }
                    json.Append("]}");
                }
                json.Append("],\"spawnData\":[");
                for (var i = 0; i < _data.Spawns.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    var spawn = _data.Spawns[i];
                    json.Append("{\"n\":\"").Append(Escape(spawn.Name)).Append("\",\"f\":\"").Append(Escape(spawn.Facet))
                        .Append("\",\"r\":\"").Append(Escape(spawn.Role)).Append("\",\"x\":").Append(spawn.X).Append(",\"y\":").Append(spawn.Y).Append("}");
                }
                json.Append("],\"dungeonLinkData\":[");
                for (var i = 0; i < _data.DungeonLinks.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    var link = _data.DungeonLinks[i];
                    json.Append("{\"n\":\"").Append(Escape(link.Name)).Append("\",\"f\":\"").Append(Escape(link.Facet))
                        .Append("\",\"a\":\"").Append(Escape(link.EntranceName)).Append("\",\"b\":\"").Append(Escape(link.InteriorName)).Append("\"}");
                }
                json.Append("]}");
            }
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        private static void SaveLocked()
        {
            var directory = System.IO.Path.GetDirectoryName(PathName);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            var temporary = PathName + ".tmp";
            using (var stream = File.Create(temporary)) Serializer.Serialize(stream, _data);
            if (File.Exists(PathName)) File.Copy(PathName, PathName + ".bak", true);
            File.Copy(temporary, PathName, true);
            File.Delete(temporary);
        }
    }
}
