using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Server.CustomBots
{
    // ServUO-owned authoring data. This deliberately does not consume the
    // ModernUO editor's JSON files: the two engines have different routing
    // contracts and must remain independently upgradeable.
    [XmlRoot("PlayerBotWorld")]
    public sealed class PlayerBotWorldDataFile
    {
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
    }

    public sealed class PlayerBotWaypoint
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string Facet;
        [XmlAttribute] public int X;
        [XmlAttribute] public int Y;
        [XmlAttribute] public int Z;
    }

    public sealed class PlayerBotDestination
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string Facet;
        [XmlAttribute] public string Kind;
        [XmlAttribute] public int X;
        [XmlAttribute] public int Y;
        [XmlAttribute] public int Z;
    }

    public sealed class PlayerBotZone
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public string Facet;
        [XmlAttribute] public string Kind;
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
                    if (map != null && String.Equals(destination.Facet, map.Name, StringComparison.OrdinalIgnoreCase)) matches.Add(destination);
                return matches.Count == 0 ? null : matches[Utility.Random(matches.Count)];
            }
        }

        internal static void AppendDashboardJson(System.Text.StringBuilder json)
        {
            lock (Sync)
            {
                json.Append(",\"world\":{\"waypoints\":").Append(_data.Waypoints.Count)
                    .Append(",\"destinations\":").Append(_data.Destinations.Count)
                    .Append(",\"zones\":").Append(_data.Zones.Count)
                    .Append(",\"spawns\":").Append(_data.Spawns.Count).Append("}");
            }
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
