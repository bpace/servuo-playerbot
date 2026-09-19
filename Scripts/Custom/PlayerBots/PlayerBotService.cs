using System;
using System.Collections.Generic;
using Server.Commands;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;

namespace Server.CustomBots
{
    // Lifecycle and behavior runner. One service timer keeps headless bots
    // moving without faking client packets or modifying ServUO's engine.
    public static class PlayerBotService
    {
        // Safe default for an online shard. An administrator must explicitly
        // set a population and enable the mod after a backup.
        public static bool Enabled = false;
        // Targets are facet-specific. A target only reconciles bots already
        // assigned to that facet, so choosing Trammel never drags a bot back
        // to Felucca.
        private static readonly Dictionary<string, int> FacetTargets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Felucca", 0 }, { "Trammel", 0 }, { "Ilshenar", 0 }, { "Malas", 0 }
        };
        public static string SpawnFacet = "Felucca";
        private static Timer _timer;
        private static readonly Queue<string> Events = new Queue<string>();

        public static int TargetPopulation
        {
            get
            {
                var total = 0;
                foreach (var target in FacetTargets.Values) total += target;
                return total;
            }
        }

        private sealed class City
        {
            public string Name;
            public Point3D Location;
            public Map Facet;

            public City(string name, Map facet, int x, int y, int z)
            {
                Name = name;
                Facet = facet;
                Location = new Point3D(x, y, z);
            }
        }

        // Classic locations exist on Felucca and Trammel. Bots retain their
        // current facet, so this works on an AoS shard without map hacks.
        private static readonly City[] Cities =
        {
            new City("Britain", Map.Felucca, 1336, 1997, 5), new City("Minoc", Map.Felucca, 2476, 418, 15),
            new City("Moonglow", Map.Felucca, 4467, 1283, 5), new City("Skara Brae", Map.Felucca, 643, 2236, 0),
            new City("Trinsic", Map.Felucca, 1820, 2821, 0), new City("Vesper", Map.Felucca, 2899, 676, 0), new City("Yew", Map.Felucca, 545, 990, 0),
            new City("Britain", Map.Trammel, 1336, 1997, 5), new City("Minoc", Map.Trammel, 2476, 418, 15),
            new City("Moonglow", Map.Trammel, 4467, 1283, 5), new City("Skara Brae", Map.Trammel, 643, 2236, 0),
            new City("Trinsic", Map.Trammel, 1820, 2821, 0), new City("Vesper", Map.Trammel, 2899, 676, 0), new City("Yew", Map.Trammel, 545, 990, 0),
            new City("Gargoyle City", Map.Ilshenar, 852, 730, -28), new City("Lakeshire", Map.Ilshenar, 1203, 1124, -25),
            new City("Mistas", Map.Ilshenar, 820, 1060, -30), new City("Luna", Map.Malas, 1015, 527, -65),
            new City("Umbra", Map.Malas, 1997, 1386, -85)
        };

        public static void Initialize()
        {
            CommandSystem.Register("PlayerBots", AccessLevel.GameMaster, OnCommand);
            EventSink.WorldLoad += OnWorldLoad;
            PlayerBotWorldData.Initialize();
            Enabled = PlayerBotWorldData.IsEnabled;
            _timer = Timer.DelayCall(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), Tick);
            Timer.DelayCall(TimeSpan.FromSeconds(10), RestoreFacetLocations);
            PlayerBotDashboard.Start();
        }

        private static void OnWorldLoad()
        {
            RestoreFacetLocations();
            Timer.DelayCall(TimeSpan.FromSeconds(10), ReconcilePopulation);
        }

        private static void RestoreFacetLocations()
        {
            var restored = 0;
            foreach (var bot in FindBots())
            {
                bot.Player = false;
                PlayerBotPersonas.EnsureAppearance(bot);
                if (bot.Map == Map.Internal)
                {
                    // Legacy v1 bots were saved as Player=true and lost their
                    // logout facet. They were created on Felucca, so restore
                    // those known records there. New records keep Player=false
                    // and preserve whatever facet was selected at spawn time.
                    var map = bot.LogoutMap == null || bot.LogoutMap == Map.Internal ? Map.Felucca : bot.LogoutMap;
                    bot.MoveToWorld(bot.LogoutLocation, map);
                    restored++;
                }
            }
            if (restored > 0) RecordEvent("Restored " + restored + " PlayerBot(s) from Internal to their saved facets.");
        }

        private static void OnCommand(CommandEventArgs e)
        {
            var action = e.Length == 0 ? "status" : e.GetString(0).ToLowerInvariant();
            if (action == "spawn")
            {
                var count = e.Length > 1 ? Math.Max(1, Math.Min(100, e.GetInt32(1))) : 1;
                for (var i = 0; i < count; i++)
                {
                    SpawnNear(e.Mobile);
                }
                RecordEvent("GM spawned " + count + " bot(s).");
                e.Mobile.SendMessage("Spawned {0} PlayerBot(s).", count);
                return;
            }
            if (action == "generate")
            {
                var created = MaterializeStoredSpawns();
                RecordEvent("GM materialized " + created + " stored spawn bot(s).");
                e.Mobile.SendMessage("Materialized {0} stored PlayerBot spawn(s).", created);
                return;
            }
            if (action == "roadpks")
            {
                var created = MaterializeRoadPks();
                RecordEvent("GM materialized " + created + " road PK bot(s).");
                e.Mobile.SendMessage("Materialized {0} road PK bot(s).", created);
                return;
            }
            if (action == "audit")
            {
                var map = e.Length > 1 ? GetMap(e.GetString(1)) : e.Mobile.Map;
                var message = PlayerBotWorldData.StartRouteAudit(map);
                RecordEvent(message);
                e.Mobile.SendMessage(message);
                return;
            }
            if (action == "population")
            {
                SetTarget(SpawnFacet, Math.Max(0, Math.Min(250, e.Length > 1 ? e.GetInt32(1) : GetTarget(SpawnFacet))));
                ReconcilePopulation();
                RecordEvent("GM set " + SpawnFacet + " target population to " + GetTarget(SpawnFacet) + ".");
                e.Mobile.SendMessage("PlayerBot {0} target population: {1}.", SpawnFacet, GetTarget(SpawnFacet));
                return;
            }
            if (action == "on" || action == "off")
            {
                Enabled = action == "on";
                PlayerBotWorldData.SetEnabled(Enabled);
                if (Enabled) ReconcilePopulation();
                RecordEvent("GM turned PlayerBots " + (Enabled ? "on" : "off") + ".");
                e.Mobile.SendMessage("PlayerBots are {0}.", Enabled ? "on" : "off");
                return;
            }
            if (action == "remove")
            {
                var bots = FindBots();
                foreach (var bot in bots) bot.Delete();
                RecordEvent("GM removed " + bots.Count + " bot(s).");
                e.Mobile.SendMessage("Removed {0} PlayerBot(s).", bots.Count);
                return;
            }
            e.Mobile.SendMessage("PlayerBots: {0} live, combined target {1}, system {2}. Commands: spawn [count], population [count], generate, audit [facet], on, off, remove.", FindBots().Count, TargetPopulation, Enabled ? "on" : "off");
        }

        private static void ReconcilePopulation()
        {
            if (!Enabled) return;
            var bots = FindBots();
            foreach (var facetName in FacetNames)
            {
                var facet = GetMap(facetName);
                var current = 0;
                foreach (var bot in bots) if (bot.Map == facet) current++;
                while (current < GetTarget(facetName))
                {
                    var bot = SpawnAt(RandomCity(facet), facet);
                    bots.Add(bot);
                    current++;
                    RecordEvent("Spawned " + bot.Name + " in " + bot.DestinationName + ".");
                }
            }
        }

        private static PlayerBot SpawnNear(Mobile from)
        {
            var map = from == null || from.Map == null ? Map.Felucca : from.Map;
            var point = from == null ? RandomCity(map).Location : from.Location;
            var bot = new PlayerBot((PlayerBotRole)Utility.Random(4));
            bot.MoveToWorld(new Point3D(point.X + Utility.RandomMinMax(-3, 3), point.Y + Utility.RandomMinMax(-3, 3), point.Z), map);
            EnsureDestination(bot);
            return bot;
        }

        private static PlayerBot SpawnAt(City city, Map map)
        {
            return SpawnAt(city.Location, map, (PlayerBotRole)Utility.Random(4));
        }

        private static PlayerBot SpawnAt(Point3D location, Map map, PlayerBotRole role)
        {
            var bot = new PlayerBot(role);
            bot.MoveToWorld(location, map);
            EnsureDestination(bot);
            return bot;
        }

        private static int MaterializeStoredSpawns()
        {
            return MaterializeStoredSpawns(false);
        }

        private static int MaterializeRoadPks()
        {
            return MaterializeStoredSpawns(true);
        }

        private static int MaterializeStoredSpawns(bool roadPksOnly)
        {
            var created = 0;
            var bots = FindBots();
            foreach (var definition in PlayerBotWorldData.GetSpawns())
            {
                var map = GetMap(definition.Facet);
                PlayerBotRole role;
                if (!Enum.TryParse(definition.Role, true, out role)) role = PlayerBotRole.Traveler;
                if (roadPksOnly != (role == PlayerBotRole.PlayerKiller)) continue;
                var location = new Point3D(definition.X, definition.Y, definition.Z);
                if (role == PlayerBotRole.PlayerKiller && !PlayerBotWorldData.IsLegalRoadPkLocation(map, location.X, location.Y, location.Z))
                {
                    RecordEvent("Ignored invalid road PK spawn definition " + definition.Name + ".");
                    continue;
                }
                var present = 0;
                foreach (var bot in bots)
                    if (bot.Map == map && bot.BotRole == role && String.Equals(bot.SpawnSource, definition.Name, StringComparison.OrdinalIgnoreCase)) present++;
                while (present < definition.Count)
                {
                    var point = role == PlayerBotRole.PlayerKiller ? location : new Point3D(location.X + Utility.RandomMinMax(-2, 2), location.Y + Utility.RandomMinMax(-2, 2), location.Z);
                    var bot = SpawnAt(point, map, role);
                    bot.SpawnSource = definition.Name;
                    if (role == PlayerBotRole.PlayerKiller)
                    {
                        bot.Destination = location;
                        bot.DestinationName = "Road PK: " + definition.Name;
                    }
                    bots.Add(bot);
                    present++;
                    created++;
                }
            }
            return created;
        }

        internal static List<PlayerBot> FindBots()
        {
            var bots = new List<PlayerBot>();
            foreach (Mobile mobile in World.Mobiles.Values)
                if (mobile is PlayerBot bot && !bot.Deleted) bots.Add(bot);
            return bots;
        }

        public static void EnsureDestination(PlayerBot bot)
        {
            if (bot == null || bot.Deleted || bot.Map == null || bot.Map == Map.Internal) return;
            if (bot.Destination != Point3D.Zero) return;
            AssignDestination(bot);
        }

        private static void Tick()
        {
            PlayerBotDashboard.ProcessPendingActions();
            var auditResult = PlayerBotWorldData.AdvanceRouteAudit(32);
            if (!String.IsNullOrEmpty(auditResult)) RecordEvent(auditResult);
            PlayerBotDashboard.RefreshSnapshot();
            if (!Enabled) return;
            foreach (var bot in FindBots()) Tick(bot);
        }

        private static void Tick(PlayerBot bot)
        {
            if (!bot.Alive)
            {
                if (DateTime.UtcNow >= bot.NextAction)
                {
                    bot.Resurrect();
                    bot.NextAction = DateTime.UtcNow + TimeSpan.FromMinutes(2);
                }
                return;
            }
            if (bot.Map == null || bot.Map == Map.Internal) return;

            if (TryFight(bot)) return;
            if (DateTime.UtcNow < bot.NextAction) return;

            EnsureDestination(bot);
            var travelTarget = bot.Destination;
            if (bot.RoutePoints != null && bot.RouteIndex < bot.RoutePoints.Count)
            {
                if (bot.InRange(bot.RoutePoints[bot.RouteIndex], 2))
                {
                    bot.RouteIndex++;
                    bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
                    return;
                }
                travelTarget = bot.RoutePoints[bot.RouteIndex];
            }
            else if (bot.InRange(bot.Destination, 2))
            {
                Arrive(bot);
                return;
            }

            // Long hops become a visible public-moongate trip. This avoids
            // teleporting every short walk while keeping the shard populated.
            if ((bot.RoutePoints == null || bot.RouteIndex >= bot.RoutePoints.Count)
                && bot.GetDistanceToSqrt(bot.Destination) > 120 && Utility.RandomDouble() < 0.08)
            {
                bot.Say("I am taking the moongate to " + bot.DestinationName + ".");
                bot.MoveToWorld(bot.Destination, bot.Map);
                RecordEvent(bot.Name + " used a moongate to " + bot.DestinationName + ".");
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                return;
            }

            var direction = bot.GetDirectionTo(travelTarget) | Direction.Running;
            if (!bot.Move(direction))
            {
                if (bot.RoutePoints != null) bot.RoutePoints.Clear();
                bot.RouteIndex = 0;
                AssignDestination(bot);
            }
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(800);
        }

        private static bool TryFight(PlayerBot bot)
        {
            if (bot.BotRole == PlayerBotRole.PlayerKiller) return TryFightPlayer(bot);
            if (bot.Combatant is Mobile current && !current.Deleted && current.Alive && bot.InRange(current, 12))
            {
                if (!bot.InRange(current, 1)) bot.Move(bot.GetDirectionTo(current) | Direction.Running);
                return true;
            }

            IPooledEnumerable nearby = bot.GetMobilesInRange(8);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var creature = mobile as BaseCreature;
                    if (creature == null || creature.Deleted || !creature.Alive || creature.Controlled || creature.Summoned) continue;
                    if (!bot.CanBeHarmful(creature, false)) continue;
                    bot.Combatant = creature;
                    bot.Warmode = true;
                    bot.DoHarmful(creature);
                    bot.Say("Have at thee!");
                    return true;
                }
            }
            finally { nearby.Free(); }
            return false;
        }

        private static bool TryFightPlayer(PlayerBot bot)
        {
            if (!PlayerBotWorldData.IsLegalRoadPkLocation(bot.Map, bot.X, bot.Y, bot.Z))
            {
                bot.Combatant = null;
                return false;
            }
            var current = bot.Combatant as PlayerMobile;
            if (IsRoadPkTarget(bot, current) && bot.InRange(current, 18))
            {
                if (!bot.InRange(current, bot.Weapon.MaxRange)) bot.Move(bot.GetDirectionTo(current) | Direction.Running);
                return true;
            }
            IPooledEnumerable nearby = bot.GetMobilesInRange(12);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var player = mobile as PlayerMobile;
                    if (!IsRoadPkTarget(bot, player)) continue;
                    bot.Combatant = player;
                    bot.Warmode = true;
                    bot.DoHarmful(player);
                    bot.Say("Your gold or your life!");
                    return true;
                }
            }
            finally { nearby.Free(); }
            return false;
        }

        private static bool IsRoadPkTarget(PlayerBot bot, PlayerMobile player)
        {
            return player != null && player.Player && !player.Deleted && player.Alive && !player.IsStaff()
                && player.Map == bot.Map && bot.CanBeHarmful(player, false);
        }

        private static void Arrive(PlayerBot bot)
        {
            if (bot.BotRole == PlayerBotRole.PlayerKiller)
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                return;
            }
            if (IsWanderDestination(bot))
            {
                AssignDestination(bot);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(8, 20));
                return;
            }
            if (!String.IsNullOrEmpty(bot.DungeonReturnName))
            {
                if (DateTime.UtcNow < bot.DungeonReturnAt)
                {
                    SayAtInterval(bot, "dungeon", "The depths are dangerous, but the loot is worth it.");
                    bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 30));
                    return;
                }
                var exit = PlayerBotWorldData.GetDestination(bot.DungeonReturnName, bot.Map);
                if (exit != null)
                {
                    bot.MoveToWorld(new Point3D(exit.X, exit.Y, exit.Z), bot.Map);
                    RecordEvent(bot.Name + " returned from " + bot.DestinationName + " to " + exit.Name + ".");
                }
                bot.DungeonReturnName = "";
                bot.DungeonReturnAt = DateTime.MinValue;
                AssignDestination(bot);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                return;
            }
            PlayerBotDestination interior;
            if (PlayerBotWorldData.TryEnterDungeon(bot, out interior))
            {
                var entrance = bot.DestinationName;
                bot.MoveToWorld(new Point3D(interior.X, interior.Y, interior.Z), bot.Map);
                bot.Destination = new Point3D(interior.X, interior.Y, interior.Z);
                bot.DestinationName = interior.Name;
                bot.DungeonReturnName = entrance;
                bot.DungeonReturnAt = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(2, 6));
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                RecordEvent(bot.Name + " entered " + interior.Name + " through " + entrance + ".");
                return;
            }
            if (bot.BotRole == PlayerBotRole.Banker)
            {
                Banker.Deposit(bot, 100, false);
                SayAtInterval(bot, "bank", "Balance looks good. Anyone buying reagents?");
            }
            else if (bot.BotRole == PlayerBotRole.Adventurer)
            {
                SayAtInterval(bot, "adventure", "Anyone want to hunt together?");
            }
            else
            {
                SayAtInterval(bot, "travel", "Safe travels from " + bot.DestinationName + ".");
            }
            if (TryAssignLocalWander(bot))
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(3, 10));
                return;
            }
            AssignDestination(bot);
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(8, 25));
        }

        private static void SayAtInterval(PlayerBot bot, string key, string text)
        {
            if (DateTime.UtcNow < bot.NextChat) return;
            bot.Say(text);
            bot.NextChat = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(2, 6));
        }

        private static void AssignDestination(PlayerBot bot)
        {
            if (bot.RoutePoints != null) bot.RoutePoints.Clear();
            bot.RouteIndex = 0;
            var authored = PlayerBotWorldData.RandomDestination(bot.Map);
            if (authored != null)
            {
                bot.Destination = new Point3D(authored.X, authored.Y, authored.Z);
                bot.DestinationName = authored.Name;
                PlayerBotWorldData.TryPlanRoute(bot, authored);
                return;
            }
            var city = RandomCity(bot.Map);
            bot.Destination = new Point3D(city.Location.X + Utility.RandomMinMax(-8, 8), city.Location.Y + Utility.RandomMinMax(-8, 8), city.Location.Z);
            bot.DestinationName = city.Name;
        }

        // This is deliberately local wandering, not fake pathfinding. It
        // spreads arrivals around an actual walkable town area while authored
        // waypoints and a true road graph remain separate work.
        private static bool TryAssignLocalWander(PlayerBot bot)
        {
            if (bot.Map == null || bot.Map == Map.Internal)
                return false;

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var x = bot.X + Utility.RandomMinMax(-24, 24);
                var y = bot.Y + Utility.RandomMinMax(-24, 24);
                var z = bot.Map.GetAverageZ(x, y);
                if (!bot.Map.CanFit(x, y, z, 16, false, false))
                    continue;

                bot.Destination = new Point3D(x, y, z);
                bot.DestinationName = "Wander: " + bot.DestinationName;
                return true;
            }

            return false;
        }

        private static bool IsWanderDestination(PlayerBot bot)
        {
            return !String.IsNullOrEmpty(bot.DestinationName)
                && bot.DestinationName.StartsWith("Wander: ", StringComparison.Ordinal);
        }

        public static void ReportMurder(PlayerBot victim)
        {
            foreach (AggressorInfo info in victim.Aggressors)
            {
                var killer = info.Attacker as PlayerMobile;
                if (killer == null || killer.Deleted || !info.CanReportMurder || info.Reported) continue;
                info.Reported = true;
                info.CanReportMurder = false;
                killer.Kills++;
                killer.ShortTermMurders++;
                killer.ResetKillTime();
                killer.SendLocalizedMessage(1049067);
                if (killer.Kills == 5) ReportMurdererGump.CheckMurderer(killer);
                RecordEvent(victim.Name + " reported " + killer.Name + " for murder.");
                break;
            }
        }

        internal static void ApplyDashboardAction(string action, int value, string facetName)
        {
            if (!String.IsNullOrEmpty(facetName)) SpawnFacet = GetMap(facetName).Name;
            if (action == "enable")
            {
                Enabled = true;
                PlayerBotWorldData.SetEnabled(true);
                ReconcilePopulation();
                RecordEvent("Dashboard turned PlayerBots on.");
            }
            else if (action == "disable")
            {
                Enabled = false;
                PlayerBotWorldData.SetEnabled(false);
                RecordEvent("Dashboard turned PlayerBots off.");
            }
            else if (action == "population")
            {
                SetTarget(SpawnFacet, Math.Max(0, Math.Min(250, value)));
                if (Enabled) ReconcilePopulation();
                RecordEvent("Dashboard set " + SpawnFacet + " target population to " + GetTarget(SpawnFacet) + ".");
            }
            else if (action == "spawn")
            {
                var count = Math.Max(1, Math.Min(50, value));
                var facet = GetSpawnMap();
                for (var i = 0; i < count; i++) SpawnAt(RandomCity(facet), facet);
                RecordEvent("Dashboard spawned " + count + " bot(s) on " + SpawnFacet + ".");
            }
            else if (action == "remove")
            {
                var bots = FindBots();
                foreach (var bot in bots) bot.Delete();
                RecordEvent("Dashboard removed " + bots.Count + " bot(s).");
            }
            else if (action == "removefacet")
            {
                var facet = GetSpawnMap();
                var removed = 0;
                foreach (var bot in FindBots())
                {
                    if (bot.Map != facet) continue;
                    bot.Delete();
                    removed++;
                }
                RecordEvent("Dashboard removed " + removed + " bot(s) from " + SpawnFacet + ".");
            }
            else if (action == "reloadworld")
            {
                PlayerBotWorldData.Reload();
                RecordEvent("Dashboard reloaded PlayerBot world data.");
            }
            else if (action == "audit")
            {
                RecordEvent(PlayerBotWorldData.StartRouteAudit(GetSpawnMap()));
            }
            else if (action == "generatespawns")
            {
                var created = MaterializeStoredSpawns();
                RecordEvent("Dashboard materialized " + created + " stored spawn bot(s).");
            }
            else if (action == "spawnroadpks")
            {
                var created = MaterializeRoadPks();
                RecordEvent("Dashboard materialized " + created + " road PK bot(s)." + (Enabled ? "" : " PlayerBots are disabled, so they will not act until enabled."));
            }
            else if (action == "regeneratespawns")
            {
                var removed = 0;
                foreach (var bot in FindBots())
                {
                    if (String.IsNullOrEmpty(bot.SpawnSource)) continue;
                    bot.Delete();
                    removed++;
                }
                var created = MaterializeStoredSpawns() + MaterializeRoadPks();
                RecordEvent("Dashboard regenerated stored spawns: removed " + removed + ", created " + created + ".");
            }
        }

        internal static void ApplyEditorAction(string action, string facetName, string name, string kind, string otherName, int x, int y, int z, int width, int height, int count)
        {
            string message;
            bool success;
            if (action == "editor-waypoint") success = PlayerBotWorldData.AddWaypoint(name, facetName, x, y, z, out message);
            else if (action == "editor-destination") success = PlayerBotWorldData.AddDestination(name, facetName, kind, x, y, z, out message);
            else if (action == "editor-zone") success = PlayerBotWorldData.AddZone(name, facetName, kind, x, y, width, height, out message);
            else if (action == "editor-portal") success = PlayerBotWorldData.AddDungeonLink(name, facetName, kind, otherName, out message);
            else success = PlayerBotWorldData.AddSpawn(name, facetName, kind, x, y, z, count, out message);
            RecordEvent("Editor " + (success ? "saved: " : "rejected: ") + message);
        }

        internal static int GetTarget(string facetName)
        {
            int value;
            return FacetTargets.TryGetValue(GetMap(facetName).Name, out value) ? value : 0;
        }

        private static void SetTarget(string facetName, int value)
        {
            FacetTargets[GetMap(facetName).Name] = value;
        }

        internal static readonly string[] FacetNames = { "Felucca", "Trammel", "Ilshenar", "Malas" };

        private static Map GetSpawnMap()
        {
            return GetMap(SpawnFacet);
        }

        internal static Map GetMap(string name)
        {
            if (String.Equals(name, "Trammel", StringComparison.OrdinalIgnoreCase)) return Map.Trammel;
            if (String.Equals(name, "Ilshenar", StringComparison.OrdinalIgnoreCase)) return Map.Ilshenar;
            if (String.Equals(name, "Malas", StringComparison.OrdinalIgnoreCase)) return Map.Malas;
            return Map.Felucca;
        }

        private static City RandomCity(Map facet)
        {
            var choices = new List<City>();
            foreach (var city in Cities) if (city.Facet == facet) choices.Add(city);
            return choices.Count == 0 ? Cities[0] : choices[Utility.Random(choices.Count)];
        }

        internal static List<string> GetEvents()
        {
            return new List<string>(Events);
        }

        internal static void RecordEvent(string message)
        {
            Events.Enqueue(DateTime.UtcNow.ToString("HH:mm:ss") + " " + message);
            while (Events.Count > 100) Events.Dequeue();
            Console.WriteLine("[PlayerBots] " + message);
        }
    }
}
