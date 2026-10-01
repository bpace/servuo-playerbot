using System;
using System.Collections.Generic;
using Server.Commands;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.SkillHandlers;
using Server.Spells;
using Server.Spells.First;
using Server.Spells.Fourth;

namespace Server.CustomBots
{
    // Lifecycle and behavior runner. One service timer keeps headless bots
    // moving without faking client packets or modifying ServUO's engine.
    public static class PlayerBotService
    {
        // Safe default for an online shard. An administrator must explicitly
        // set a population and enable the mod after a backup.
        public static bool Enabled = false;
        // Native player theft has real item and criminal consequences. It is
        // deployed disabled and needs an explicit in-game GM enable after a
        // consenting disposable-character test is arranged.
        public static bool NativeThievingEnabled = false;
        // This is intentionally process-local and normally zero. Native
        // stealing only runs while the GM test command has selected one
        // consenting player, then the target is cleared immediately.
        private static int NativeThievingTestTargetSerial;
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
        private static DateTime _nextRouteAudit = DateTime.MinValue;
        private static DateTime _nextDashboardRefresh = DateTime.MinValue;
        private static readonly TimeSpan BankHubReconcileInterval = TimeSpan.FromSeconds(20);
        private static DateTime _nextBankHubReconcile = DateTime.MinValue;
        private static readonly TimeSpan AutonomousLaborReconcileInterval = TimeSpan.FromMinutes(3);
        private static DateTime _nextAutonomousLaborReconcile = DateTime.MinValue;
        private static readonly Dictionary<int, PlayerBotLaborKind> PendingAutonomousLabor = new Dictionary<int, PlayerBotLaborKind>();
        private const string BankHubPrefix = "BankHub:";
        private const int BankSitterLayoutVersion = 2;
        private const int TrammelBritainBankCrowd = 18;
        private const int TrammelOtherBankCrowd = 4;
        private const int FeluccaBritainBankCrowd = 12;
        private const int FeluccaOtherBankCrowd = 3;

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
            CommandSystem.Register("JoinBotParty", AccessLevel.Player, OnJoinBotPartyCommand);
            CommandSystem.Register("LeadBotParty", AccessLevel.Player, OnLeadBotPartyCommand);
            CommandSystem.Register("JoinBotGuild", AccessLevel.Player, OnJoinBotGuildCommand);
            EventSink.WorldLoad += OnWorldLoad;
            PlayerBotWorldData.Initialize();
            PlayerBotBehaviorRegistry.Initialize();
            PlayerBotWorldData.StartFacetAudits();
            Enabled = PlayerBotWorldData.IsEnabled;
            // Actors need independent sub-second scheduling. Background work
            // remains throttled inside Tick, so this does not multiply audit
            // or dashboard cost.
            _timer = Timer.DelayCall(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250), Tick);
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
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(0, 1500));
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
                if (IsBankHubBot(bot)) InitializeBankSitter(bot, true);
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
            if (action == "dungeonaudit")
            {
                var map = e.Length > 1 ? GetMap(e.GetString(1)) : e.Mobile.Map;
                var message = PlayerBotWorldData.AuditDungeonEntrancePads(map);
                RecordEvent(message);
                e.Mobile.SendMessage(message);
                return;
            }
            if (action == "dungeontest")
            {
                var map = e.Length > 1 ? GetMap(e.GetString(1)) : e.Mobile.Map;
                var message = StartNativeDungeonTest(map);
                RecordEvent(message);
                e.Mobile.SendMessage(message);
                return;
            }
            if (action == "partydungeontest")
            {
                var map = e.Length > 1 ? GetMap(e.GetString(1)) : e.Mobile.Map;
                var message = StartNativePartyDungeonTest(map);
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
            if (action == "thieving")
            {
                NativeThievingEnabled = e.Length > 1 && String.Equals(e.GetString(1), "on", StringComparison.OrdinalIgnoreCase);
                NativeThievingTestTargetSerial = 0;
                e.Mobile.SendMessage("Native PlayerBot thieving is {0}.", NativeThievingEnabled ? "enabled" : "disabled");
                RecordEvent("GM " + (NativeThievingEnabled ? "enabled" : "disabled") + " native PlayerBot thieving.");
                return;
            }
            if (action == "thievingtest")
            {
                var message = StartNativeThievingTest(e.Length > 1 ? String.Join(" ", e.Arguments, 1, e.Length - 1) : "");
                RecordEvent(message);
                e.Mobile.SendMessage(message);
                return;
            }
            if (action == "labor")
            {
                var message = PlayerBotLabor.StartNear(e.Mobile, e.Length > 1 ? e.GetString(1) : "");
                RecordEvent(message);
                e.Mobile.SendMessage(message);
                return;
            }
            if (action == "party")
            {
                var message = PlayerBotParties.FormNear(e.Mobile, e.Length > 1 ? e.GetInt32(1) : 3);
                RecordEvent(message);
                e.Mobile.SendMessage(message);
                return;
            }
            if (action == "guild")
            {
                var message = PlayerBotGuilds.FormNear(e.Mobile, e.Length > 1 ? e.GetInt32(1) : 3);
                RecordEvent(message);
                e.Mobile.SendMessage(message);
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
            e.Mobile.SendMessage("PlayerBots: {0} live, combined target {1}, system {2}. Commands: spawn [count], population [count], generate, audit [facet], dungeonaudit [facet], dungeontest [facet], partydungeontest [facet], labor miner|lumberjack|blacksmith, party [2-10], guild [2-10], thieving on|off, thievingtest <player name>, on, off, remove.", FindBots().Count, TargetPopulation, Enabled ? "on" : "off");
        }

        private static void OnJoinBotPartyCommand(CommandEventArgs e)
        {
            var message = PlayerBotParties.InvitePlayer(e.Mobile);
            e.Mobile.SendMessage(message);
        }

        private static void OnLeadBotPartyCommand(CommandEventArgs e)
        {
            var message = PlayerBotParties.AddBotToPlayerParty(e.Mobile);
            e.Mobile.SendMessage(message);
        }

        private static void OnJoinBotGuildCommand(CommandEventArgs e)
        {
            var message = PlayerBotGuilds.JoinPlayer(e.Mobile);
            e.Mobile.SendMessage(message);
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

        private static bool IsFeluccaCrimeRole(PlayerBotRole role)
        {
            return role == PlayerBotRole.PlayerKiller || role == PlayerBotRole.Thief;
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
                if (IsFeluccaCrimeRole(role) && map != Map.Felucca)
                {
                    RecordEvent("Ignored Felucca-only " + role + " spawn definition " + definition.Name + " on " + map.Name + ".");
                    continue;
                }
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

        private static string StartNativeDungeonTest(Map map)
        {
            if (!Enabled) return "Enable PlayerBots before starting a native dungeon test.";
            PlayerBotWorldData.NativeDungeonTrip trip;
            if (!PlayerBotWorldData.TryGetAnyNativeDungeonTrip(map, out trip))
                return "No verified native dungeon trip is available on " + (map == null ? "this map" : map.Name) + ". Finish the facet audit first.";

            PlayerBot selected = null;
            var bestDistance = Double.MaxValue;
            foreach (var bot in FindBots())
            {
                if (bot.Map != map || !bot.Alive || bot.BotRole == PlayerBotRole.PlayerKiller
                    || !String.IsNullOrEmpty(bot.DungeonTravelState)) continue;
                var distance = bot.GetDistanceToSqrt(trip.EntrancePad);
                if (distance >= bestDistance) continue;
                selected = bot;
                bestDistance = distance;
            }
            if (selected == null) return "No eligible non-PK PlayerBot is available on " + map.Name + ".";

            AssignNativeDungeonTrip(selected, trip);
            selected.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
            return "Native dungeon test assigned " + selected.Name + " to " + trip.Entrance.Name + ". It will walk and use the real pads.";
        }

        private static string StartNativePartyDungeonTest(Map map)
        {
            if (!Enabled) return "Enable PlayerBots before starting a native party dungeon test.";
            PlayerBotWorldData.NativeDungeonTrip trip;
            if (!PlayerBotWorldData.TryGetAnyNativeDungeonTrip(map, out trip))
                return "No verified native dungeon trip is available on " + (map == null ? "this map" : map.Name) + ". Finish the facet audit first.";

            foreach (var leader in FindBots())
            {
                var party = Server.Engines.PartySystem.Party.Get(leader);
                if (party == null || party.Leader != leader || leader.Map != map || !leader.Alive) continue;
                var members = new List<PlayerBot>();
                foreach (var member in party.Members)
                {
                    var bot = member.Mobile as PlayerBot;
                    if (bot == null || !bot.Alive || bot.Map != map || bot.BotRole == PlayerBotRole.PlayerKiller
                        || !String.IsNullOrEmpty(bot.DungeonTravelState)) continue;
                    members.Add(bot);
                }
                if (members.Count < 2) continue;
                foreach (var bot in members)
                {
                    AssignNativeDungeonTrip(bot, trip);
                    bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
                }
                return "Native party dungeon test assigned " + members.Count + " bots led by " + leader.Name + " to " + trip.Entrance.Name + ". Each will use the verified real pads.";
            }
            return "No eligible native PlayerBot party with at least two living bot members is available on " + map.Name + ". Form a nearby party first.";
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
            var now = DateTime.UtcNow;
            if (now >= _nextRouteAudit)
            {
                var auditResult = PlayerBotWorldData.AdvanceRouteAudit(32);
                if (!String.IsNullOrEmpty(auditResult)) RecordEvent(auditResult);
                _nextRouteAudit = now + TimeSpan.FromSeconds(2);
            }
            if (now >= _nextDashboardRefresh)
            {
                PlayerBotDashboard.RefreshSnapshot();
                _nextDashboardRefresh = now + TimeSpan.FromSeconds(2);
            }
            if (!Enabled) return;
            if (now >= _nextBankHubReconcile)
            {
                ReconcileBankHubs();
                _nextBankHubReconcile = now + BankHubReconcileInterval;
            }
            PlayerBotGuilds.ReconcileAutonomousMembership();
            PlayerBotParties.ReconcileAutonomousParties();
            ReconcileAutonomousLabor();
            foreach (var bot in FindBots()) Tick(bot);
        }

        private static void Tick(PlayerBot bot)
        {
            if (bot == null) return;
            NormalizeCombatState(bot);
            if (DateTime.UtcNow < bot.NextAction) return;
            PlayerBotBehaviorRegistry.Tick(bot);
        }

        // PlayerBots own their combat scheduling rather than inheriting a
        // BaseCreature AI loop. Every path that starts a fight sets warmode,
        // so clear both engine fields here when neither a valid target nor a
        // nearby live aggressor remains. This must run before NextAction so a
        // bank sitter or paused traveler cannot visibly stay in war mode.
        private static void NormalizeCombatState(PlayerBot bot)
        {
            if (bot == null || bot.Deleted) return;
            var target = bot.Combatant as Mobile;
            if (IsActiveCombatTarget(bot, target) || HasActiveAggressor(bot)) return;
            bot.Combatant = null;
            bot.Warmode = false;
        }

        private static bool IsActiveCombatTarget(PlayerBot bot, Mobile target)
        {
            if (bot == null || target == null || target.Deleted || !target.Alive || target.Map != bot.Map) return false;
            var range = bot.BotRole == PlayerBotRole.PlayerKiller ? 18 : 12;
            return bot.InRange(target, range);
        }

        private static bool HasActiveAggressor(PlayerBot bot)
        {
            if (bot == null) return false;
            var range = bot.BotRole == PlayerBotRole.PlayerKiller ? 18 : 12;
            foreach (AggressorInfo info in bot.Aggressors)
            {
                var attacker = info.Attacker as Mobile;
                if (attacker != null && !attacker.Deleted && attacker.Alive
                    && attacker.Map == bot.Map && bot.InRange(attacker, range)) return true;
            }
            return false;
        }

        // Core behavior adapters keep the lifecycle registry's interface
        // small. Optional packages can register a behavior without gaining
        // ownership of the service timer, population reconciler, or engine
        // mutation helpers.
        internal static void TickDeadBehavior(PlayerBot bot)
        {
            if (bot == null || bot.Alive) return;
            bot.Resurrect();
            bot.NextAction = DateTime.UtcNow + (bot.CorpseRecoverySerial != 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromMinutes(2));
        }

        internal static void TrackCorpseRecovery(PlayerBot bot, Corpse corpse)
        {
            if (bot == null || corpse == null || corpse.Deleted || corpse.Owner != bot) return;
            bot.CorpseRecoverySerial = corpse.Serial.Value;
            bot.CorpseRecoveryUntil = DateTime.UtcNow + TimeSpan.FromMinutes(10);
            RecordEvent(bot.Name + " left a corpse and will reclaim it after resurrection.");
        }

        internal static bool HasCorpseRecoveryBehavior(PlayerBot bot)
        {
            return bot != null && bot.Alive && bot.CorpseRecoverySerial != 0;
        }

        internal static void TickCorpseRecoveryBehavior(PlayerBot bot)
        {
            if (bot == null) return;
            if (DateTime.UtcNow >= bot.CorpseRecoveryUntil)
            {
                ClearCorpseRecovery(bot, "corpse recovery expired");
                return;
            }

            var corpse = World.FindItem((Serial)bot.CorpseRecoverySerial) as Corpse;
            if (corpse == null || corpse.Deleted || corpse.Owner != bot || corpse.Map != bot.Map)
            {
                ClearCorpseRecovery(bot, "corpse was gone or unreachable");
                return;
            }
            if (!bot.InRange(corpse.GetWorldLocation(), 1))
            {
                bot.Move(bot.GetDirectionTo(corpse.GetWorldLocation()));
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
                return;
            }

            // This is ServUO's own owner-only self-loot path. It restores
            // equipped and backpack items subject to the normal capacity and
            // corpse rules; PlayerBots never copy or fabricate an item.
            corpse.Open(bot, true);
            if (corpse.Items.Count == 0 && (corpse.EquipItems == null || corpse.EquipItems.Count == 0))
                ClearCorpseRecovery(bot, "reclaimed its corpse");
            else
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        }

        private static void ClearCorpseRecovery(PlayerBot bot, string outcome)
        {
            bot.CorpseRecoverySerial = 0;
            bot.CorpseRecoveryUntil = DateTime.MinValue;
            RecordEvent(bot.Name + " " + outcome + ".");
        }

        internal static bool IsBankSitterBehavior(PlayerBot bot)
        {
            return IsBankHubBot(bot);
        }

        internal static void TickBankSitterBehavior(PlayerBot bot)
        {
            TickBankSitter(bot);
        }

        internal static bool IsDestinationVisitorBehavior(PlayerBot bot)
        {
            return IsDestinationVisitor(bot);
        }

        internal static void TickDestinationVisitorBehavior(PlayerBot bot)
        {
            TickDestinationVisitor(bot);
        }

        internal static bool ShouldFightBehavior(PlayerBot bot)
        {
            if (bot == null || bot.Map == null || bot.Map == Map.Internal) return false;
            // Thief and PK behavior needs its original travel fallthrough
            // when there is no actionable mark. Their native checks remain
            // in TickTravelBehavior instead of claiming every service tick.
            if (bot.BotRole == PlayerBotRole.Thief || bot.BotRole == PlayerBotRole.PlayerKiller) return false;
            return bot.Combatant is Mobile || HasNearbyCreature(bot);
        }

        internal static bool IsEligibleForAutonomousParty(PlayerBot bot)
        {
            return bot != null && !bot.Deleted && bot.Alive && bot.Map != null && bot.Map != Map.Internal
                && (bot.BotRole == PlayerBotRole.Traveler || bot.BotRole == PlayerBotRole.Adventurer)
                && !IsBankHubBot(bot) && !IsDestinationVisitor(bot)
                && bot.LaborKind == PlayerBotLaborKind.None && bot.CorpseRecoverySerial == 0
                && String.IsNullOrEmpty(bot.DungeonTravelState);
        }

        // Typed sites are opt-in world data. At most one low-frequency worker
        // is assigned, only after the authored point passed native harvest
        // validation and this specific bot has an audited route to it. The
        // worker keeps its normal mobile and uses the existing Labor adapter
        // on arrival; this scheduler never creates resources or moves a bot.
        private static void ReconcileAutonomousLabor()
        {
            var now = DateTime.UtcNow;
            if (now < _nextAutonomousLaborReconcile) return;
            _nextAutonomousLaborReconcile = now + AutonomousLaborReconcileInterval;
            if (Utility.RandomDouble() >= 0.25) return;

            foreach (var worker in FindBots())
            {
                if (!IsEligibleForAutonomousLabor(worker)) continue;
                var firstKind = Utility.Random(6);
                for (var offset = 0; offset < 6; offset++)
                {
                    var kind = (firstKind + offset) % 6;
                    if ((kind == 0 && TryAssignAutonomousLabor(worker, "MiningSpot", PlayerBotLaborKind.Miner))
                        || (kind == 1 && TryAssignAutonomousLabor(worker, "LumberSpot", PlayerBotLaborKind.Lumberjack))
                        || (kind == 2 && TryAssignAutonomousLabor(worker, "FishingSpot", PlayerBotLaborKind.Fisher))
                        || (kind == 3 && TryAssignAutonomousLabor(worker, "Smithy", PlayerBotLaborKind.Blacksmith, true))
                        || (kind == 4 && TryAssignAutonomousLabor(worker, "Carpentry", PlayerBotLaborKind.Carpenter, true))
                        || (kind == 5 && TryAssignAutonomousLabor(worker, "Kitchen", PlayerBotLaborKind.Cooker, true))) return;
                }
            }
        }

        private static bool IsEligibleForAutonomousLabor(PlayerBot bot)
        {
            return IsEligibleForAutonomousParty(bot) && bot.Combatant == null
                && Server.Engines.PartySystem.Party.Get(bot) == null && !PendingAutonomousLabor.ContainsKey(bot.Serial.Value);
        }

        private static bool TryAssignAutonomousLabor(PlayerBot worker, string siteKind, PlayerBotLaborKind laborKind, bool requiresWorkshopStock = false)
        {
            var sites = PlayerBotWorldData.GetDestinations(worker.Map, siteKind);
            if (sites.Count == 0) return false;
            while (sites.Count > 0)
            {
                var index = Utility.Random(sites.Count);
                var site = sites[index];
                sites.RemoveAt(index);
                if (requiresWorkshopStock && !HasWorkshopStockAt(worker.Map, new Point3D(site.X, site.Y, site.Z), laborKind)) continue;
                var occupied = false;
                foreach (var bot in FindBots())
                    if (bot != worker && bot.Map == worker.Map && String.Equals(bot.DestinationName, site.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        occupied = true;
                        break;
                    }
                if (occupied || (!worker.InRange(new Point3D(site.X, site.Y, site.Z), 2)
                    && !PlayerBotWorldData.TryPlanRoute(worker, site))) continue;

                worker.Destination = new Point3D(site.X, site.Y, site.Z);
                worker.DestinationName = site.Name;
                worker.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                PendingAutonomousLabor[worker.Serial.Value] = laborKind;
                RecordEvent(worker.Name + " is traveling to " + site.Name + " for a " + laborKind.ToString().ToLowerInvariant() + " shift.");
                return true;
            }
            return false;
        }

        private static bool HasWorkshopStockAt(Map map, Point3D location, PlayerBotLaborKind laborKind)
        {
            foreach (var bot in FindBots())
                if (bot != null && !bot.Deleted && bot.Alive && bot.Map == map
                    && bot.BankRole == PlayerBotBankRole.Hawker && bot.InRange(location, 4)
                    && ((laborKind == PlayerBotLaborKind.Blacksmith && PlayerBotShop.HasWorkshopOre(bot))
                        || (laborKind == PlayerBotLaborKind.Carpenter && PlayerBotShop.HasWorkshopLogs(bot))
                        || (laborKind == PlayerBotLaborKind.Cooker && PlayerBotShop.HasWorkshopFish(bot)))) return true;
            return false;
        }

        // A real player may explicitly recruit an idle visitor at their side.
        // Unlike autonomous formation, this allows a short tavern visit, but
        // keeps permanent bank fixtures, active labor, recovery, dungeons,
        // and ongoing combat out of player-led parties.
        internal static bool IsEligibleForPlayerLedParty(PlayerBot bot)
        {
            return bot != null && !bot.Deleted && bot.Alive && bot.Map != null && bot.Map != Map.Internal
                && (bot.BotRole == PlayerBotRole.Traveler || bot.BotRole == PlayerBotRole.Adventurer)
                && !IsBankHubBot(bot) && bot.LaborKind == PlayerBotLaborKind.None && bot.CorpseRecoverySerial == 0
                && String.IsNullOrEmpty(bot.DungeonTravelState) && bot.Combatant == null;
        }

        internal static void RecordPartyEvent(string message)
        {
            RecordEvent(message);
        }

        internal static void TickCombatBehavior(PlayerBot bot)
        {
            if (!TryFight(bot)) return;
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 850));
        }

        internal static void TickTravelBehavior(PlayerBot bot)
        {
            if (bot == null || bot.Map == null || bot.Map == Map.Internal) return;
            if ((bot.BotRole == PlayerBotRole.Thief || bot.BotRole == PlayerBotRole.PlayerKiller) && TryFight(bot))
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 850));
                return;
            }
            if (AdvanceNativeDungeonTravel(bot)) return;

            EnsureDestination(bot);
            var travelTarget = bot.Destination;
            if (bot.RoutePoints != null && bot.RouteIndex < bot.RoutePoints.Count)
            {
                if (bot.InRange(bot.RoutePoints[bot.RouteIndex], 2))
                {
                    bot.RouteIndex++;
                    bot.NextAction = DateTime.UtcNow + MoveDelay();
                    return;
                }
                travelTarget = bot.RoutePoints[bot.RouteIndex];
            }
            else if (bot.InRange(bot.Destination, 2))
            {
                Arrive(bot);
                return;
            }

            var direction = bot.GetDirectionTo(travelTarget) | Direction.Running;
            if (!bot.Move(direction))
            {
                if (bot.RoutePoints != null) bot.RoutePoints.Clear();
                bot.RouteIndex = 0;
                AssignDestination(bot);
            }
            bot.NextAction = DateTime.UtcNow + MoveDelay();
        }

        internal static void ResumeTravelAfterLabor(PlayerBot bot)
        {
            if (bot == null || bot.Deleted || bot.Map == null || bot.Map == Map.Internal) return;
            PendingAutonomousLabor.Remove(bot.Serial.Value);
            AssignDestination(bot);
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(2, 6));
        }

        internal static string GetLaborStatus(PlayerBot bot)
        {
            if (bot == null) return "";
            PlayerBotLaborKind pending;
            if (PendingAutonomousLabor.TryGetValue(bot.Serial.Value, out pending))
                return "Traveling to " + pending.ToString().ToLowerInvariant() + " shift";
            if (bot.LaborReturning) return "Hauling to " + bot.LaborReturnName;
            if (PlayerBotLabor.IsActive(bot)) return "Working as " + bot.LaborKind.ToString().ToLowerInvariant();
            return "";
        }

        private static bool TryStartAutonomousLabor(PlayerBot bot)
        {
            PlayerBotLaborKind kind;
            if (bot == null || !PendingAutonomousLabor.TryGetValue(bot.Serial.Value, out kind)) return false;
            PendingAutonomousLabor.Remove(bot.Serial.Value);
            if (bot.Combatant != null || Server.Engines.PartySystem.Party.Get(bot) != null) return false;
            if (!PlayerBotLabor.StartAutonomousShift(bot, kind)) return false;
            RecordEvent(bot.Name + " started a " + kind.ToString().ToLowerInvariant() + " shift at " + bot.DestinationName + ".");
            return true;
        }

        private static bool HasNearbyCreature(PlayerBot bot)
        {
            IPooledEnumerable nearby = bot.GetMobilesInRange(8);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var creature = mobile as BaseCreature;
                    if (creature != null && !creature.Deleted && creature.Alive && !creature.Controlled && !creature.Summoned
                        && bot.CanBeHarmful(creature, false)) return true;
                }
            }
            finally { nearby.Free(); }
            return false;
        }

        private static bool TryFight(PlayerBot bot)
        {
            if (bot.BotRole == PlayerBotRole.Thief)
                return bot.Map == Map.Felucca && TryThief(bot);
            if (bot.BotRole == PlayerBotRole.PlayerKiller)
                return bot.Map == Map.Felucca && TryFightPlayer(bot);
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

        private static string StartNativeThievingTest(string playerName)
        {
            if (!NativeThievingEnabled) return "Native thieving is disabled. Use [PlayerBots thieving on] first.";
            if (String.IsNullOrWhiteSpace(playerName)) return "Usage: [PlayerBots thievingtest <consenting player name>]";

            PlayerMobile mark = null;
            var matches = 0;
            foreach (Mobile mobile in World.Mobiles.Values)
            {
                var candidate = mobile as PlayerMobile;
                if (candidate == null || candidate is PlayerBot || candidate.Deleted || !candidate.Player
                    || !String.Equals(candidate.Name, playerName, StringComparison.OrdinalIgnoreCase)) continue;
                mark = candidate;
                matches++;
            }
            if (matches != 1) return matches == 0 ? "No connected player matches '" + playerName + ".'" : "More than one player matches '" + playerName + ".' Use a unique character name.";
            if (!mark.Alive || mark.IsStaff() || mark.Map != Map.Felucca || mark.Backpack == null)
                return "The test player must be a live, non-staff character in Felucca with a backpack.";

            PlayerBot thief = null;
            foreach (var candidate in FindBots())
            {
                if (candidate.BotRole != PlayerBotRole.Thief || candidate.Deleted || !candidate.Alive || candidate.Map != Map.Felucca
                    || !candidate.InRange(mark, 1)) continue;
                thief = candidate;
                break;
            }
            if (thief == null) return "Move a live Thief PlayerBot adjacent to the consenting test player, then retry.";
            if (thief.Criminal) return "The selected thief is criminal. Wait for its criminal flag to clear before testing.";
            if (FindStealableItem(mark) == null) return "The test player's top-level backpack needs one movable, non-newbied, non-container item weighing 10 stones or less.";

            NativeThievingTestTargetSerial = mark.Serial.Value;
            try
            {
                thief.NextThiefAction = DateTime.MinValue;
                return TryThief(thief)
                    ? "Native theft test attempted against consenting player " + mark.Name + ". Check the thief, backpack, criminal flag, and server log for the engine result."
                    : "Native theft test could not start. Recheck adjacency, Felucca, and the disposable item.";
            }
            finally
            {
                NativeThievingTestTargetSerial = 0;
            }
        }

        // The engine owns the actual theft result. The target is an explicit,
        // one-shot GM test selection, then ServUO's native Stealing cursor
        // resolves the attempt. This code never moves an item directly.
        private static bool TryThief(PlayerBot bot)
        {
            if (!NativeThievingEnabled || NativeThievingTestTargetSerial == 0) return false;
            if (DateTime.UtcNow < bot.NextThiefAction) return false;
            if (bot.Criminal)
            {
                try { Hiding.OnUse(bot); } catch { }
                bot.NextThiefAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(8, 16));
                return true;
            }
            var mark = World.FindMobile((Serial)NativeThievingTestTargetSerial) as PlayerMobile;
            if (!IsEligibleThievingTestTarget(bot, mark)) return false;
            var item = FindStealableItem(mark);
            if (item == null) return false;
            bot.NpcGuild = NpcGuild.ThievesGuild;
            Stealing.OnUse(bot);
            if (bot.Target != null) bot.Target.Invoke(bot, item);
            bot.NextThiefAction = DateTime.UtcNow + TimeSpan.FromMinutes(4);
            RecordEvent(bot.Name + " attempted a native Felucca theft test against " + mark.Name + ".");
            return true;
        }

        private static bool IsEligibleThievingTestTarget(PlayerBot bot, PlayerMobile mark)
        {
            return bot != null && mark != null && !(mark is PlayerBot) && mark.Player && !mark.Deleted && mark.Alive
                && !mark.IsStaff() && bot.Map == Map.Felucca && mark.Map == Map.Felucca && mark.Backpack != null && bot.InRange(mark, 1);
        }

        private static Item FindStealableItem(PlayerMobile mark)
        {
            if (mark == null || mark.Backpack == null) return null;
            foreach (Item candidate in mark.Backpack.Items)
            {
                if (candidate == null || candidate.Deleted || !candidate.Movable || candidate is Container
                    || candidate.LootType == LootType.Newbied || candidate.TotalWeight + candidate.Weight > 10) continue;
                return candidate;
            }
            return null;
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

        private static bool AdvanceNativeDungeonTravel(PlayerBot bot)
        {
            if (String.Equals(bot.DungeonTravelState, "Entering", StringComparison.Ordinal)
                && bot.InRange(bot.DungeonLanding, 2))
            {
                var interior = PlayerBotWorldData.GetDestination(bot.DungeonInteriorName, bot.Map);
                if (interior == null)
                {
                    ClearNativeDungeonTrip(bot);
                    AssignDestination(bot);
                    return true;
                }
                bot.DungeonTravelState = "Exploring";
                bot.Destination = new Point3D(interior.X, interior.Y, interior.Z);
                bot.DestinationName = interior.Name;
                bot.DungeonReturnAt = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(2, 6));
                PlayerBotWorldData.TryPlanRoute(bot, interior);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
                RecordEvent(bot.Name + " entered " + interior.Name + " through " + bot.DungeonReturnName + ".");
                return true;
            }
            if (String.Equals(bot.DungeonTravelState, "Leaving", StringComparison.Ordinal))
            {
                var entrance = PlayerBotWorldData.GetDestination(bot.DungeonReturnName, bot.Map);
                if (entrance != null && bot.InRange(new Point3D(entrance.X, entrance.Y, entrance.Z), 12)
                    && !bot.InRange(bot.DungeonReturnPad, 2))
                {
                    RecordEvent(bot.Name + " returned from " + bot.DungeonInteriorName + " to " + entrance.Name + ".");
                    ClearNativeDungeonTrip(bot);
                    AssignDestination(bot);
                    bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                    return true;
                }
            }
            return false;
        }

        private static void StepOntoDungeonPad(PlayerBot bot, Point3D pad)
        {
            if (bot.Location == pad)
            {
                // The real item did not transfer the bot. Never manufacture
                // a coordinate move; abandon this trip and pick a safe route.
                ClearNativeDungeonTrip(bot);
                AssignDestination(bot);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                return;
            }
            bot.Move(bot.GetDirectionTo(pad) | Direction.Running);
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(800);
        }

        private static void PlanNativeDungeonLeg(PlayerBot bot, Point3D destination)
        {
            if (bot.RoutePoints != null) bot.RoutePoints.Clear();
            bot.RouteIndex = 0;
            PlayerBotWorldData.TryPlanRoute(bot, new PlayerBotDestination
            {
                Facet = bot.Map.Name,
                X = destination.X,
                Y = destination.Y,
                Z = destination.Z
            });
        }

        private static void ClearNativeDungeonTrip(PlayerBot bot)
        {
            bot.DungeonTravelState = "";
            bot.DungeonInteriorName = "";
            bot.DungeonReturnName = "";
            bot.DungeonReturnAt = DateTime.MinValue;
            bot.DungeonLanding = Point3D.Zero;
            bot.DungeonReturnPad = Point3D.Zero;
        }

        private static void Arrive(PlayerBot bot)
        {
            if (bot.BotRole == PlayerBotRole.PlayerKiller)
            {
                if (bot.Map != Map.Felucca)
                {
                    RecordEvent("Converted misplaced road PK " + bot.Name + " to Traveler outside Felucca.");
                    bot.BotRole = PlayerBotRole.Traveler;
                    AssignDestination(bot);
                    bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                    return;
                }
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                return;
            }
            if (IsBankHubBot(bot) && AssignBankHubWander(bot))
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(350, 1800));
                return;
            }
            if (TryStartAutonomousLabor(bot)) return;
            if (TryStartDestinationVisit(bot)) return;
            if (IsGraveyardDestination(bot))
            {
                // UO Offline does not leave a Traveler idling at a dangerous
                // graveyard after declining its Adventurer handoff.
                AssignDestination(bot);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                return;
            }
            if (IsWanderDestination(bot))
            {
                // Townies and bankers make several small circuits around a
                // place before choosing a new destination.  This makes a
                // watched street look inhabited instead of briefly crossed.
                var keepWandering = bot.BotRole == PlayerBotRole.Townie
                    || bot.BotRole == PlayerBotRole.Banker;
                if (keepWandering && Utility.RandomDouble() < 0.72 && TryAssignLocalWander(bot))
                {
                    bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(3, 9));
                    return;
                }
                AssignDestination(bot);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(8, 20));
                return;
            }
            if (String.Equals(bot.DungeonTravelState, "Entering", StringComparison.Ordinal))
            {
                StepOntoDungeonPad(bot, bot.Destination);
                return;
            }
            if (String.Equals(bot.DungeonTravelState, "Exploring", StringComparison.Ordinal))
            {
                if (DateTime.UtcNow < bot.DungeonReturnAt)
                {
                    SayAtInterval(bot, "dungeon", "The depths are dangerous, but the loot is worth it.");
                    bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 30));
                    return;
                }
                bot.DungeonTravelState = "Leaving";
                bot.Destination = bot.DungeonReturnPad;
                bot.DestinationName = "Return to " + bot.DungeonReturnName;
                PlanNativeDungeonLeg(bot, bot.Destination);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
                return;
            }
            if (String.Equals(bot.DungeonTravelState, "Leaving", StringComparison.Ordinal))
            {
                StepOntoDungeonPad(bot, bot.Destination);
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

        // ServUO adaptation of UO Offline VisitorBehavior: a normal traveler
        // reaches a destination, spends a bounded themed session there, then
        // returns to the ordinary travel scheduler. It stays separate from
        // permanent BankSitter fixtures.
        private static bool TryStartDestinationVisit(PlayerBot bot)
        {
            var destination = PlayerBotWorldData.GetDestination(bot.DestinationName, bot.Map);
            if (destination == null) return false;

            var isBank = String.Equals(destination.Kind, "Bank", StringComparison.OrdinalIgnoreCase);
            var isGraveyard = String.Equals(destination.Kind, "Graveyard", StringComparison.OrdinalIgnoreCase);
            if (!isBank && !IsVisitorDestinationKind(destination.Kind)) return false;
            if (isGraveyard && bot.BotRole != PlayerBotRole.Traveler && bot.BotRole != PlayerBotRole.Adventurer)
                return false;
            // Upstream hands a Traveler off at a graveyard 75% of the time.
            // A declined handoff leaves immediately in Arrive, never idling
            // in a dangerous place as a generic traveler.
            if (isGraveyard && Utility.RandomDouble() > 0.75) return false;
            if (!isBank && !isGraveyard && Utility.RandomDouble() > 0.85) return false;

            bot.BankVisitName = destination.Name;
            bot.BankVisitKind = destination.Kind;
            bot.BankVisitMode = "";
            bot.BankVisitFollowSerial = 0;
            bot.BankVisitFollowUntil = DateTime.MinValue;
            bot.NextBankVisitFollow = DateTime.MinValue;
            if (isBank)
            {
                var streetRoll = Utility.RandomDouble();
                if (streetRoll < 0.08) bot.BankVisitMode = "Beggar";
                else if (streetRoll < 0.15) bot.BankVisitMode = "Newbie";
            }
            bot.BankVisitHome = isBank
                ? GetBankVisitorPoint(destination, bot.Map, bot)
                : GetDestinationVisitorPoint(destination, bot.Map, bot);
            var streetVisit = !String.IsNullOrEmpty(bot.BankVisitMode);
            bot.BankVisitUntil = DateTime.UtcNow + TimeSpan.FromMinutes(isGraveyard
                ? Utility.RandomMinMax(5, 15)
                : streetVisit ? Utility.RandomMinMax(10, 25)
                : Utility.RandomMinMax(isBank ? 2 : 1, isBank ? 6 : 3));
            bot.NextBankVisitAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 35));
            bot.Destination = bot.BankVisitHome;
            bot.DestinationName = "Visiting " + destination.Name;
            if (bot.RoutePoints != null) bot.RoutePoints.Clear();
            bot.RouteIndex = 0;
            bot.NextAction = DateTime.UtcNow + MoveDelay();
            return true;
        }

        private static bool IsDestinationVisitor(PlayerBot bot)
        {
            return !IsBankHubBot(bot) && bot.BankVisitHome != Point3D.Zero;
        }

        private static void TickDestinationVisitor(PlayerBot bot)
        {
            var now = DateTime.UtcNow;
            if (now >= bot.BankVisitUntil)
            {
                // A timed Adventurer visit finishes after its active fight,
                // not in the middle of it. Do not acquire a new target here.
                var current = bot.Combatant as Mobile;
                if (IsGraveyardVisit(bot) && current != null && !current.Deleted && current.Alive && bot.InRange(current, 12))
                {
                    if (!bot.InRange(current, 1)) bot.Move(bot.GetDirectionTo(current) | Direction.Running);
                    bot.NextAction = now + TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 850));
                    return;
                }
                RecordEvent(bot.Name + " finished visiting " + bot.BankVisitName + ".");
                AssignDestination(bot);
                bot.NextAction = now + TimeSpan.FromSeconds(Utility.RandomMinMax(2, 7));
                return;
            }

            if (TryTickBankStreetFollow(bot, now)) return;

            if (!bot.InRange(bot.BankVisitHome, 1))
            {
                if (!bot.Move(bot.GetDirectionTo(bot.BankVisitHome) | Direction.Running))
                {
                    var bank = PlayerBotWorldData.GetDestination(bot.BankVisitName, bot.Map);
                    if (bank == null)
                    {
                        AssignDestination(bot);
                        bot.NextAction = now + TimeSpan.FromSeconds(5);
                        return;
                    }
                    bot.BankVisitHome = String.Equals(bot.BankVisitKind, "Bank", StringComparison.OrdinalIgnoreCase)
                        ? GetBankVisitorPoint(bank, bot.Map, bot)
                        : GetDestinationVisitorPoint(bank, bot.Map, bot);
                    bot.Destination = bot.BankVisitHome;
                }
                bot.NextAction = now + MoveDelay();
                return;
            }

            if (IsGraveyardVisit(bot) && TryFight(bot))
            {
                bot.NextAction = now + TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 850));
                return;
            }
            if (now < bot.NextBankVisitAction)
            {
                bot.NextAction = bot.NextBankVisitAction;
                return;
            }
            DoDestinationVisitAction(bot);

            bot.NextBankVisitAction = now + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 38));
            bot.NextAction = bot.NextBankVisitAction;
        }

        private static void ClearBankVisit(PlayerBot bot)
        {
            bot.BankVisitName = "";
            bot.BankVisitKind = "";
            bot.BankVisitHome = Point3D.Zero;
            bot.BankVisitUntil = DateTime.MinValue;
            bot.NextBankVisitAction = DateTime.MinValue;
            bot.BankVisitMode = "";
            bot.BankVisitFollowSerial = 0;
            bot.BankVisitFollowUntil = DateTime.MinValue;
            bot.NextBankVisitFollow = DateTime.MinValue;
        }

        // Party members can arrive from a higher-priority visitor state. Once
        // a native party exists, release that state so Party behavior takes
        // over without a hidden move or a stale "Visiting" destination.
        internal static void ResumeTravelAfterPartyFormation(PlayerBot bot)
        {
            if (bot == null || bot.Deleted) return;
            ClearBankVisit(bot);
            AssignDestination(bot);
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(2, 6));
        }

        // Tavern parties may become expeditions only through the same audited
        // native-dungeon trip used by the GM test command. Every member must
        // be eligible before any state is changed, and each bot still walks
        // to and activates the verified physical pads independently.
        internal static bool TryAssignNativePartyDungeonTrip(IList<PlayerBot> members)
        {
            if (members == null || members.Count < 2) return false;
            var map = members[0] == null ? null : members[0].Map;
            if (map == null || map == Map.Internal) return false;
            foreach (var bot in members)
            {
                if (bot == null || bot.Deleted || !bot.Alive || bot.Map != map
                    || bot.BotRole == PlayerBotRole.PlayerKiller || !String.IsNullOrEmpty(bot.DungeonTravelState))
                    return false;
            }

            PlayerBotWorldData.NativeDungeonTrip trip;
            if (!PlayerBotWorldData.TryGetAnyNativeDungeonTrip(map, out trip)) return false;
            foreach (var bot in members)
            {
                AssignNativeDungeonTrip(bot, trip);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
            }
            return true;
        }

        private static bool TryTickBankStreetFollow(PlayerBot bot, DateTime now)
        {
            if (!String.Equals(bot.BankVisitKind, "Bank", StringComparison.OrdinalIgnoreCase)
                || String.IsNullOrEmpty(bot.BankVisitMode)) return false;

            if (bot.BankVisitFollowSerial != 0)
            {
                var target = World.FindMobile((Serial)bot.BankVisitFollowSerial) as PlayerMobile;
                if (IsStreetFollowTarget(bot, target) && now < bot.BankVisitFollowUntil)
                {
                    if (!bot.InRange(target, 1)) bot.Move(bot.GetDirectionTo(target) | Direction.Running);
                    bot.NextAction = now + TimeSpan.FromMilliseconds(Utility.RandomMinMax(550, 950));
                    return true;
                }
                bot.BankVisitFollowSerial = 0;
                bot.BankVisitFollowUntil = DateTime.MinValue;
            }

            if (now < bot.NextBankVisitFollow) return false;
            IPooledEnumerable nearby = bot.GetMobilesInRange(8);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var player = mobile as PlayerMobile;
                    if (!IsStreetFollowTarget(bot, player)) continue;
                    bot.BankVisitFollowSerial = player.Serial.Value;
                    bot.BankVisitFollowUntil = now + TimeSpan.FromSeconds(25);
                    bot.NextBankVisitFollow = now + TimeSpan.FromMinutes(4);
                    bot.Say(String.Equals(bot.BankVisitMode, "Beggar", StringComparison.OrdinalIgnoreCase)
                        ? "Spare a coin for a hungry soul?"
                        : "Hello! Is this Britain Bank?");
                    bot.NextAction = now + TimeSpan.FromMilliseconds(Utility.RandomMinMax(550, 950));
                    return true;
                }
            }
            finally { nearby.Free(); }
            bot.NextBankVisitFollow = now + TimeSpan.FromMinutes(4);
            return false;
        }

        private static bool IsStreetFollowTarget(PlayerBot bot, PlayerMobile player)
        {
            return player != null && player.Player && !(player is PlayerBot) && !player.Deleted
                && player.Alive && !player.IsStaff() && player.Map == bot.Map && bot.InRange(player, 12);
        }

        private static bool IsVisitorDestinationKind(string kind)
        {
            return IsVendorDestinationKind(kind)
                || String.Equals(kind, "Healer", StringComparison.OrdinalIgnoreCase)
                || String.Equals(kind, "Inn", StringComparison.OrdinalIgnoreCase)
                || String.Equals(kind, "Tavern", StringComparison.OrdinalIgnoreCase)
                || String.Equals(kind, "Stables", StringComparison.OrdinalIgnoreCase)
                || String.Equals(kind, "Shrine", StringComparison.OrdinalIgnoreCase)
                || String.Equals(kind, "Graveyard", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGraveyardDestination(PlayerBot bot)
        {
            var destination = PlayerBotWorldData.GetDestination(bot.DestinationName, bot.Map);
            return destination != null && String.Equals(destination.Kind, "Graveyard", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGraveyardVisit(PlayerBot bot)
        {
            return String.Equals(bot.BankVisitKind, "Graveyard", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVendorDestinationKind(string kind)
        {
            return !String.IsNullOrEmpty(kind)
                && kind.StartsWith("Vendor", StringComparison.OrdinalIgnoreCase);
        }

        private static void DoDestinationVisitAction(PlayerBot bot)
        {
            var action = Utility.Random(100);
            if (IsVendorDestinationKind(bot.BankVisitKind))
            {
                // Mirrors UO Offline ShopperBehavior: presence, vendor speech,
                // and facing are the visit itself. It does not invent a gold
                // transfer until the separate BotShop transaction layer exists.
                FaceNearestVendor(bot);
                if (action < 48)
                {
                    var lines = new[] { "vendor buy", "vendor sell", "vendor view", "show me your wares", "let me see your goods" };
                    bot.Say(lines[Utility.Random(lines.Length)]);
                }
                else if (action < 65) bot.Say("Just browsing, thank you.");
                else if (action < 82) bot.Direction = (Direction)Utility.Random(8);
                return;
            }
            if (String.Equals(bot.BankVisitKind, "Bank", StringComparison.OrdinalIgnoreCase))
            {
                if (String.Equals(bot.BankVisitMode, "Beggar", StringComparison.OrdinalIgnoreCase))
                {
                    if (action < 45) bot.Say("Spare a coin for a warm meal?");
                    else if (action < 68) bot.Say("The roads have been hard lately.");
                    else bot.Direction = (Direction)Utility.Random(8);
                    return;
                }
                if (String.Equals(bot.BankVisitMode, "Newbie", StringComparison.OrdinalIgnoreCase))
                {
                    if (action < 42) bot.Say("Could someone point me toward the healer?");
                    else if (action < 65) bot.Say("I just arrived in town.");
                    else bot.Direction = (Direction)Utility.Random(8);
                    return;
                }
                if (action < 22) bot.Say("bank");
                else if (action < 34) bot.Say("Anyone looking for a hunt?");
                else if (action < 48) bot.Animate(32, 5, 1, true, false, 0);
                else if (action < 62) bot.Direction = (Direction)Utility.Random(8);
                return;
            }
            if (String.Equals(bot.BankVisitKind, "Healer", StringComparison.OrdinalIgnoreCase))
            {
                if (action < 30) bot.Say("Could you tend this wound?");
                else if (action < 52) bot.Animate(32, 5, 1, true, false, 0);
                else bot.Direction = (Direction)Utility.Random(8);
                return;
            }
            if (String.Equals(bot.BankVisitKind, "Inn", StringComparison.OrdinalIgnoreCase))
            {
                if (action < 32) bot.Say("A real bed at last.");
                else if (action < 52) bot.Say("Anyone have news from the roads?");
                else bot.Direction = (Direction)Utility.Random(8);
                return;
            }
            if (String.Equals(bot.BankVisitKind, "Tavern", StringComparison.OrdinalIgnoreCase))
            {
                if (action < 30) bot.Say("Any hunters looking for company?");
                else if (action < 52) bot.Say("A drink and a tale from the road.");
                else if (action < 70) bot.Say("Looking for a party before nightfall.");
                else if (action < 84) bot.Direction = (Direction)Utility.Random(8);
                return;
            }
            if (String.Equals(bot.BankVisitKind, "Stables", StringComparison.OrdinalIgnoreCase))
            {
                if (action < 35) bot.Say("*feeds a horse an apple*");
                else if (action < 55) bot.Animate(32, 5, 1, true, false, 0);
                else bot.Direction = (Direction)Utility.Random(8);
                return;
            }
            if (String.Equals(bot.BankVisitKind, "Shrine", StringComparison.OrdinalIgnoreCase))
            {
                if (action < 55)
                {
                    bot.Animate(32, 5, 1, true, false, 0);
                    var mantra = ShrineMantra(bot.BankVisitName);
                    if (!String.IsNullOrEmpty(mantra)) bot.Say(mantra);
                }
                else bot.Direction = (Direction)Utility.Random(8);
                return;
            }
            if (IsGraveyardVisit(bot))
            {
                if (action < 30) bot.Say("The dead do not rest easy here.");
                else if (action < 52) bot.Say("Keep your weapon ready.");
                else if (action < 72)
                {
                    var graveyard = PlayerBotWorldData.GetDestination(bot.BankVisitName, bot.Map);
                    if (graveyard != null)
                    {
                        bot.BankVisitHome = GetDestinationVisitorPoint(graveyard, bot.Map, bot);
                        bot.Destination = bot.BankVisitHome;
                    }
                }
                else bot.Direction = (Direction)Utility.Random(8);
                return;
            }
            bot.Direction = (Direction)Utility.Random(8);
        }

        private static void FaceNearestVendor(PlayerBot bot)
        {
            IPooledEnumerable nearby = bot.GetMobilesInRange(8);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var vendor = mobile as BaseVendor;
                    if (vendor == null || vendor.Deleted) continue;
                    bot.Direction = bot.GetDirectionTo(vendor.Location);
                    return;
                }
            }
            finally { nearby.Free(); }
        }

        private static string ShrineMantra(string name)
        {
            var shrine = name ?? "";
            if (shrine.IndexOf("Compassion", StringComparison.OrdinalIgnoreCase) >= 0) return "Mu";
            if (shrine.IndexOf("Honesty", StringComparison.OrdinalIgnoreCase) >= 0) return "Ahm";
            if (shrine.IndexOf("Honor", StringComparison.OrdinalIgnoreCase) >= 0) return "Summ";
            if (shrine.IndexOf("Humility", StringComparison.OrdinalIgnoreCase) >= 0) return "Lum";
            if (shrine.IndexOf("Justice", StringComparison.OrdinalIgnoreCase) >= 0) return "Beh";
            if (shrine.IndexOf("Sacrifice", StringComparison.OrdinalIgnoreCase) >= 0) return "Cah";
            if (shrine.IndexOf("Spirituality", StringComparison.OrdinalIgnoreCase) >= 0) return "Om";
            if (shrine.IndexOf("Valor", StringComparison.OrdinalIgnoreCase) >= 0) return "Ra";
            return "";
        }

        private static void AssignDestination(PlayerBot bot)
        {
            if (bot.RoutePoints != null) bot.RoutePoints.Clear();
            bot.RouteIndex = 0;
            ClearNativeDungeonTrip(bot);
            ClearBankVisit(bot);
            if (IsBankHubBot(bot) && AssignBankHubWander(bot)) return;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var authored = bot.BotRole == PlayerBotRole.Banker
                    ? PlayerBotWorldData.RandomDestination(bot.Map, "Bank")
                    : (Utility.RandomDouble() < 0.20
                        ? PlayerBotWorldData.RandomDestination(bot.Map, "Bank")
                        : PlayerBotWorldData.RandomDestination(bot.Map));
                if (authored == null) break;
                if (String.Equals(authored.Kind, "DungeonEntrance", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerBotWorldData.NativeDungeonTrip trip;
                    if (!PlayerBotWorldData.TryGetNativeDungeonTrip(bot.Map, authored.Name, out trip)) continue;
                    AssignNativeDungeonTrip(bot, trip);
                    return;
                }
                bot.Destination = new Point3D(authored.X, authored.Y, authored.Z);
                bot.DestinationName = authored.Name;
                PlayerBotWorldData.TryPlanRoute(bot, authored);
                return;
            }
            var city = RandomCity(bot.Map);
            bot.Destination = new Point3D(city.Location.X + Utility.RandomMinMax(-8, 8), city.Location.Y + Utility.RandomMinMax(-8, 8), city.Location.Z);
            bot.DestinationName = city.Name;
        }

        private static void AssignNativeDungeonTrip(PlayerBot bot, PlayerBotWorldData.NativeDungeonTrip trip)
        {
            if (bot == null || trip == null) return;
            ClearNativeDungeonTrip(bot);
            bot.Destination = trip.EntrancePad;
            bot.DestinationName = trip.Entrance.Name;
            bot.DungeonTravelState = "Entering";
            bot.DungeonInteriorName = trip.Interior.Name;
            bot.DungeonReturnName = trip.Entrance.Name;
            bot.DungeonLanding = trip.Landing;
            bot.DungeonReturnPad = trip.ReturnPad;
            PlanNativeDungeonLeg(bot, trip.EntrancePad);
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

        // Banks are the shard's social hubs. These specially marked bots are
        // spawned once around every Trammel bank and remain there, circulating
        // in a small walkable area. Britain receives the intentionally larger
        // crowd; everyone else still gets a visible local gathering.
        private static void ReconcileBankHubs()
        {
            ReconcileBankHubs(Map.Trammel, TrammelBritainBankCrowd, TrammelOtherBankCrowd);
            ReconcileBankHubs(Map.Felucca, FeluccaBritainBankCrowd, FeluccaOtherBankCrowd);
        }

        private static void ReconcileBankHubs(Map map, int britainCrowd, int otherCrowd)
        {
            var banks = PlayerBotWorldData.GetDestinations(map, "Bank");
            foreach (var bank in banks)
            {
                var source = BankHubPrefix + bank.Name;
                var desired = String.Equals(bank.Name, "Britain Bank", StringComparison.OrdinalIgnoreCase)
                    ? britainCrowd : otherCrowd;
                var current = 0;
                foreach (var bot in FindBots())
                    if (!bot.Deleted && bot.Alive && bot.Map == map
                        && String.Equals(bot.SpawnSource, source, StringComparison.OrdinalIgnoreCase)) current++;

                while (current < desired && FindBots().Count < 250)
                {
                    SpawnBankHubBot(bank, map, source);
                    current++;
                }
            }
        }

        private static void SpawnBankHubBot(PlayerBotDestination bank, Map map, string source)
        {
            var roleRoll = Utility.Random(10);
            var role = map == Map.Felucca
                ? (roleRoll < 3 ? PlayerBotRole.Banker
                    : roleRoll < 6 ? PlayerBotRole.Townie
                    : roleRoll < 8 ? PlayerBotRole.Traveler
                    : roleRoll < 9 ? PlayerBotRole.Adventurer
                    : PlayerBotRole.Thief)
                : (roleRoll < 4 ? PlayerBotRole.Banker
                    : roleRoll < 7 ? PlayerBotRole.Townie
                    : roleRoll < 9 ? PlayerBotRole.Traveler
                    : PlayerBotRole.Adventurer);
            var bot = new PlayerBot(role);
            bot.SpawnSource = source;
            bot.MoveToWorld(GetBankHubPoint(bank, map, null), map);
            InitializeBankSitter(bot, false);
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(0, 2200));
        }

        private static bool IsBankHubBot(PlayerBot bot)
        {
            return bot != null && !String.IsNullOrEmpty(bot.SpawnSource)
                && bot.SpawnSource.StartsWith(BankHubPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AssignBankHubWander(PlayerBot bot)
        {
            return InitializeBankSitter(bot, false);
        }

        // ServUO counterpart to UO Offline's BankSitterBehavior.OnAttached.
        // A sitter owns one stable, separate home tile instead of repeatedly
        // choosing a shared bank coordinate as a generic wanderer.
        private static bool InitializeBankSitter(PlayerBot bot, bool relocateLegacyBot)
        {
            if (!IsBankHubBot(bot) || bot.Map == null || bot.Map == Map.Internal) return false;
            var bankName = bot.SpawnSource.Substring(BankHubPrefix.Length);
            var bank = PlayerBotWorldData.GetDestination(bankName, bot.Map);
            if (bank == null) return false;

            if (!bot.BankSitterInitialized || bot.BankHome == Point3D.Zero
                || bot.BankSitterLayoutVersion < BankSitterLayoutVersion)
            {
                if (!bot.BankSitterInitialized) bot.BankRole = RollBankSitterRole();

                var wallHome = Point3D.Zero;
                var wallFacing = Direction.North;
                bot.BankWallSitter = Utility.RandomDouble() < 0.80
                    && TryGetBankWallHome(bank, bot.Map, bot, out wallHome, out wallFacing);
                bot.BankHome = bot.BankWallSitter ? wallHome : GetBankHubPoint(bank, bot.Map, bot);
                if (bot.BankWallSitter)
                {
                    bot.Direction = wallFacing;
                    if (bot.Mount != null) bot.Mount.Rider = null;
                }
                bot.NextBankAction = DateTime.UtcNow + BankActionDelay(bot.BankRole);
                bot.BankSitterInitialized = true;
                bot.BankSitterLayoutVersion = BankSitterLayoutVersion;
                if (relocateLegacyBot || bot.BankWallSitter) bot.MoveToWorld(bot.BankHome, bot.Map);
            }

            bot.Destination = bot.BankHome;
            bot.DestinationName = bank.Name + " bank sitter";
            if (bot.RoutePoints != null) bot.RoutePoints.Clear();
            bot.RouteIndex = 0;
            return true;
        }

        private static PlayerBotBankRole RollBankSitterRole()
        {
            var roll = Utility.Random(100);
            if (roll < 30) return PlayerBotBankRole.Regular;
            if (roll < 50) return PlayerBotBankRole.Hawker;
            if (roll < 65) return PlayerBotBankRole.Afk;
            if (roll < 80) return PlayerBotBankRole.ResistMacro;
            if (roll < 90) return PlayerBotBankRole.HidingMacro;
            return PlayerBotBankRole.StealthMacro;
        }

        private static void TickBankSitter(PlayerBot bot)
        {
            if (!InitializeBankSitter(bot, false)) return;

            // UO Offline sitters only walk back when displaced. This avoids
            // the marching-circle look produced by a generic wander target.
            if (!bot.InRange(bot.BankHome, 1))
            {
                if (!bot.Move(bot.GetDirectionTo(bot.BankHome)))
                {
                    bot.BankSitterInitialized = false;
                    InitializeBankSitter(bot, false);
                }
                bot.NextAction = DateTime.UtcNow + MoveDelay();
                return;
            }

            var now = DateTime.UtcNow;
            if (now < bot.NextBankAction)
            {
                bot.NextAction = bot.NextBankAction;
                return;
            }

            switch (bot.BankRole)
            {
                case PlayerBotBankRole.Regular:
                    if (Utility.RandomDouble() < 0.10 && PlayerBotShop.TryBuyNearbyHawkerStock(bot))
                        break;
                    if (!PlayerBotGuilds.TryGuildChat(bot))
                        TryBankSitterSpeech(bot, new[] { "bank", "LFG", "WTB regs", "anyone headed to a dungeon?" }, 0.25);
                    break;
                case PlayerBotBankRole.Hawker:
                    var wts = PlayerBotShop.WtsLine(bot);
                    if (!String.IsNullOrEmpty(wts) && Utility.RandomDouble() < 0.55) bot.Say(wts);
                    break;
                case PlayerBotBankRole.ResistMacro:
                    TickResistMacro(bot, now);
                    return;
                case PlayerBotBankRole.HidingMacro:
                    if (bot.Hidden)
                    {
                        bot.RevealingAction();
                        bot.NextBankAction = now + BankActionDelay(bot.BankRole);
                    }
                    else
                    {
                        bot.NextBankAction = now + Hiding.OnUse(bot);
                    }
                    bot.NextAction = bot.NextBankAction;
                    return;
                case PlayerBotBankRole.StealthMacro:
                    if (!bot.Hidden)
                    {
                        bot.NextBankAction = now + Hiding.OnUse(bot);
                    }
                    else if (!bot.IsStealthing)
                    {
                        bot.NextBankAction = now + Stealth.OnUse(bot);
                    }
                    else
                    {
                        var drift = GetBankSitterDriftPoint(bot);
                        if (drift != Point3D.Zero) bot.Move(bot.GetDirectionTo(drift));
                        bot.NextBankAction = now + BankActionDelay(bot.BankRole);
                    }
                    bot.NextAction = bot.NextBankAction;
                    return;
            }

            bot.NextBankAction = now + BankActionDelay(bot.BankRole);
            bot.NextAction = bot.NextBankAction;
        }

        // UO Offline's bank resist macro casts weak self-debuffs to exercise
        // the real resistance path. Do not create a reagent kit or bypass a
        // cast: a bot only continues while its own pack can support a native
        // spell, and CheckHSequence owns mana, reagent, skill, and outcome.
        private static void TickResistMacro(PlayerBot bot, DateTime now)
        {
            if (bot.Target != null)
            {
                bot.Target.Invoke(bot, bot);
                bot.NextBankAction = now + BankActionDelay(PlayerBotBankRole.ResistMacro);
                bot.NextAction = bot.NextBankAction;
                return;
            }

            if (bot.Spell != null)
            {
                bot.NextBankAction = now + TimeSpan.FromSeconds(1);
                bot.NextAction = bot.NextBankAction;
                return;
            }

            var spell = PickResistSpell(bot);
            if (spell == null)
            {
                bot.BankRole = PlayerBotBankRole.Regular;
                bot.NextBankAction = now + BankActionDelay(bot.BankRole);
                bot.NextAction = bot.NextBankAction;
                return;
            }

            bot.NextBankAction = now + TimeSpan.FromSeconds(spell.Cast() ? 1 : 6);
            bot.NextAction = bot.NextBankAction;
        }

        private static Spell PickResistSpell(PlayerBot bot)
        {
            if (bot == null || bot.Backpack == null) return null;
            var pack = bot.Backpack;
            if (pack.GetAmount(typeof(Bloodmoss)) > 0 && pack.GetAmount(typeof(Nightshade)) > 0)
                return new ClumsySpell(bot, null);
            if (pack.GetAmount(typeof(Garlic)) > 0 && pack.GetAmount(typeof(Nightshade)) > 0)
                return new WeakenSpell(bot, null);
            if (pack.GetAmount(typeof(Ginseng)) > 0 && pack.GetAmount(typeof(Nightshade)) > 0)
                return new FeeblemindSpell(bot, null);
            if (bot.Skills[SkillName.Magery].Base >= 26.0
                && pack.GetAmount(typeof(Garlic)) > 0 && pack.GetAmount(typeof(Nightshade)) > 0
                && pack.GetAmount(typeof(SulfurousAsh)) > 0)
                return new CurseSpell(bot, null);
            return null;
        }

        private static void TryBankSitterSpeech(PlayerBot bot, string[] lines, double chance)
        {
            if (Utility.RandomDouble() >= chance || lines == null || lines.Length == 0) return;
            bot.Say(lines[Utility.Random(lines.Length)]);
        }

        private static TimeSpan BankActionDelay(PlayerBotBankRole role)
        {
            switch (role)
            {
                case PlayerBotBankRole.Hawker: return TimeSpan.FromSeconds(Utility.RandomMinMax(10, 25));
                case PlayerBotBankRole.ResistMacro: return TimeSpan.FromSeconds(Utility.RandomMinMax(8, 15));
                case PlayerBotBankRole.HidingMacro: return TimeSpan.FromSeconds(Utility.RandomMinMax(4, 10));
                case PlayerBotBankRole.StealthMacro: return TimeSpan.FromSeconds(Utility.RandomMinMax(2, 5));
                case PlayerBotBankRole.Afk: return TimeSpan.FromMinutes(Utility.RandomMinMax(8, 15));
                default: return TimeSpan.FromSeconds(Utility.RandomMinMax(15, 45));
            }
        }

        private static Point3D GetBankSitterDriftPoint(PlayerBot bot)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var x = bot.BankHome.X + Utility.RandomMinMax(-3, 3);
                var y = bot.BankHome.Y + Utility.RandomMinMax(-3, 3);
                var z = bot.Map.GetAverageZ(x, y);
                var point = new Point3D(x, y, z);
                if (bot.Map.CanFit(x, y, z, 16, false, false) && IsBankHubPointFree(bot.Map, point, bot)) return point;
            }
            return Point3D.Zero;
        }

        private static Point3D GetBankHubPoint(PlayerBotDestination bank, Map map, PlayerBot movingBot)
        {
            for (var attempt = 0; attempt < 48; attempt++)
            {
                var x = bank.X + Utility.RandomMinMax(-18, 18);
                var y = bank.Y + Utility.RandomMinMax(-18, 18);
                var z = map.GetAverageZ(x, y);
                var point = new Point3D(x, y, z);
                if (map.CanFit(x, y, z, 16, false, false) && IsBankHubPointFree(map, point, movingBot)) return point;
            }
            return new Point3D(bank.X, bank.Y, bank.Z);
        }

        // Visitors get a short-lived, non-wall position near the counter. It
        // is kept distinct from BankSitter wall homes so passing traffic does
        // not displace people who are deliberately using a bank box.
        private static Point3D GetBankVisitorPoint(PlayerBotDestination bank, Map map, PlayerBot movingBot)
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var x = bank.X + Utility.RandomMinMax(-10, 10);
                var y = bank.Y + Utility.RandomMinMax(-10, 10);
                var z = map.GetAverageZ(x, y);
                var point = new Point3D(x, y, z);
                if (map.CanFit(x, y, z, 16, false, false) && IsBankHubPointFree(map, point, movingBot)) return point;
            }
            return new Point3D(bank.X, bank.Y, bank.Z);
        }

        private static Point3D GetDestinationVisitorPoint(PlayerBotDestination destination, Map map, PlayerBot movingBot)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var x = destination.X + Utility.RandomMinMax(-3, 3);
                var y = destination.Y + Utility.RandomMinMax(-3, 3);
                var z = map.GetAverageZ(x, y);
                var point = new Point3D(x, y, z);
                if (map.CanFit(x, y, z, 16, false, false) && IsBankHubPointFree(map, point, movingBot)) return point;
            }
            return new Point3D(destination.X, destination.Y, destination.Z);
        }

        // Direct ServUO adaptation of UO Offline PlayerBot.TryHugNearbyWall:
        // standing tile is walkable, one cardinal neighbor is impassable, and
        // the sitter faces away from the wall toward the room.
        private static bool TryGetBankWallHome(PlayerBotDestination bank, Map map, PlayerBot movingBot, out Point3D home, out Direction facing)
        {
            var candidates = new List<KeyValuePair<Point3D, Direction>>();
            for (var x = bank.X - 18; x <= bank.X + 18; x++)
            for (var y = bank.Y - 18; y <= bank.Y + 18; y++)
            {
                var z = map.GetAverageZ(x, y);
                var point = new Point3D(x, y, z);
                if (!map.CanFit(x, y, z, 16, false, false) || !IsBankHubPointFree(map, point, movingBot)) continue;

                Direction wall;
                if (!map.CanFit(x, y - 1, z, 16, false, false)) wall = Direction.North;
                else if (!map.CanFit(x + 1, y, z, 16, false, false)) wall = Direction.East;
                else if (!map.CanFit(x, y + 1, z, 16, false, false)) wall = Direction.South;
                else if (!map.CanFit(x - 1, y, z, 16, false, false)) wall = Direction.West;
                else continue;
                candidates.Add(new KeyValuePair<Point3D, Direction>(point, wall));
            }

            if (candidates.Count == 0)
            {
                home = Point3D.Zero;
                facing = Direction.North;
                return false;
            }
            var selected = candidates[Utility.Random(candidates.Count)];
            home = selected.Key;
            facing = (Direction)(((int)selected.Value + 4) & 7);
            return true;
        }

        private static bool IsBankHubPointFree(Map map, Point3D point, PlayerBot movingBot)
        {
            foreach (var other in FindBots())
                if (other != movingBot && !other.Deleted && other.Alive && other.Map == map && other.InRange(point, 2)) return false;
            return true;
        }

        private static TimeSpan MoveDelay()
        {
            return TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 1050));
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
                PlayerBotWorldData.StartFacetAudits();
                RecordEvent("Dashboard reloaded PlayerBot world data.");
            }
            else if (action == "audit")
            {
                RecordEvent(PlayerBotWorldData.StartRouteAudit(GetSpawnMap()));
            }
            else if (action == "dungeonaudit")
            {
                RecordEvent(PlayerBotWorldData.AuditDungeonEntrancePads(GetSpawnMap()));
            }
            else if (action == "dungeontest")
            {
                RecordEvent(StartNativeDungeonTest(GetSpawnMap()));
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
                    if (String.IsNullOrEmpty(bot.SpawnSource) || IsBankHubBot(bot)) continue;
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
