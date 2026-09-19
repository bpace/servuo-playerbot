using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Server.Regions;

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
        private static readonly object Sync = new object();
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PlayerBotWorldDataFile));
        private static PlayerBotWorldDataFile _data = new PlayerBotWorldDataFile();

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
            lock (Sync)
            {
                var matches = new List<PlayerBotDestination>();
                foreach (var destination in _data.Destinations)
                    if (map != null && String.Equals(destination.Facet, map.Name, StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(destination.Kind, "DungeonRoom", StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(destination.Kind, "DungeonAscend", StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(destination.Kind, "DungeonDescend", StringComparison.OrdinalIgnoreCase)) matches.Add(destination);
                return matches.Count == 0 ? null : matches[Utility.Random(matches.Count)];
            }
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
                        if (neighbor == null || seen.Contains(neighbor.Name) || !IsShortLeg(current, neighbor)) continue;
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
                bot.RoutePoints.Clear();
                foreach (var point in reverse) bot.RoutePoints.Add(new Point3D(point.X, point.Y, point.Z));
                bot.RoutePoints.Add(new Point3D(destination.X, destination.Y, destination.Z));
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
