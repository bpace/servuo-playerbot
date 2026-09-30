using System;
using System.Collections.Generic;
using Server.Engines.PartySystem;
using Server.Items;

namespace Server.CustomBots
{
    // A deliberately small adapter over ServUO's own transient Party object.
    // It never serializes a party, fakes a packet, or changes party loot and
    // chat rules. The registry only owns expiry and follower movement.
    public static class PlayerBotParties
    {
        private static readonly Dictionary<int, DateTime> ManagedLeaders = new Dictionary<int, DateTime>();
        private static readonly HashSet<int> PlayerLedLeaders = new HashSet<int>();
        private static readonly Dictionary<int, DateTime> PlayerLedSeparationSince = new Dictionary<int, DateTime>();
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan PlayerLedSeparationLimit = TimeSpan.FromMinutes(2);
        private static DateTime NextAutonomousReconcile = DateTime.MinValue;
        private static readonly TimeSpan AutonomousReconcileInterval = TimeSpan.FromSeconds(45);

        public static string FormNear(Mobile caller, int requestedCount)
        {
            if (!Core.UOR)
                return "Native parties require a UOR-or-later expansion. This shard is configured earlier than UOR.";
            if (caller == null || caller.Map == null || caller.Map == Map.Internal)
                return "Stand on a normal game facet before forming a PlayerBot party.";

            var count = Math.Max(2, Math.Min(Party.Capacity, requestedCount));
            var candidates = new List<PlayerBot>();
            foreach (var bot in PlayerBotService.FindBots())
            {
                if (bot == null || bot.Deleted || !bot.Alive || bot.Map != caller.Map || Party.Get(bot) != null)
                    continue;
                if (!bot.InRange(caller.Location, 18))
                    continue;
                candidates.Add(bot);
            }
            candidates.Sort(delegate(PlayerBot left, PlayerBot right)
            {
                return left.GetDistanceToSqrt(caller).CompareTo(right.GetDistanceToSqrt(caller));
            });

            if (candidates.Count < 2)
                return "Need at least two living, unpartied PlayerBots within 18 tiles to form a native party.";

            var leader = candidates[0];
            var party = new Party(leader);
            leader.Party = party;
            var added = 1;
            for (var i = 1; i < candidates.Count && added < count; i++)
            {
                party.Add(candidates[i]);
                added++;
            }
            ManagedLeaders[leader.Serial.Value] = DateTime.UtcNow + Lifetime;
            return "Formed a native PlayerBot party led by " + leader.Name + " with " + added + " members for up to 15 minutes.";
        }

        public static bool IsManagedMember(PlayerBot bot)
        {
            if (!Core.UOR || bot == null || bot.Deleted) return false;
            var party = Party.Get(bot);
            if (party == null || party.Leader == null) return false;
            return ManagedLeaders.ContainsKey(party.Leader.Serial.Value)
                || PlayerLedLeaders.Contains(party.Leader.Serial.Value);
        }

        // Player membership is never automatic: the player runs this command
        // and then completes ServUO's ordinary /accept invitation flow.
        public static string InvitePlayer(Mobile player)
        {
            if (!Core.UOR)
                return "Native parties require a UOR-or-later expansion. This shard is configured earlier than UOR.";
            if (player == null || player.Deleted || !player.Alive || player.Map == null || player.Map == Map.Internal)
                return "You must be alive on a normal game facet to join a PlayerBot party.";
            if (Party.Get(player) != null)
                return "Leave your current party before requesting a PlayerBot party.";

            Party closest = null;
            var closestDistance = Double.MaxValue;
            foreach (var bot in PlayerBotService.FindBots())
            {
                var party = Party.Get(bot);
                if (party == null || party.Leader != bot || !ManagedLeaders.ContainsKey(bot.Serial.Value)) continue;
                if (party.Candidates.Contains(player))
                    return "You already have an invitation from " + bot.Name + ". Type /accept or /decline.";
                if (party.Members.Count + party.Candidates.Count >= Party.Capacity || !bot.InRange(player.Location, 18)) continue;
                var distance = bot.GetDistanceToSqrt(player);
                if (distance >= closestDistance) continue;
                closest = party;
                closestDistance = distance;
            }
            if (closest == null)
                return "No nearby PlayerBot party has room. Form a bot party nearby or try again later.";

            Party.Invite(closest.Leader, player);
            return "Invitation sent from " + closest.Leader.Name + ". Type /accept to join for the remaining party run.";
        }

        // The player explicitly asks to lead. The engine still owns the
        // normal Party object; the forced accept is only for a headless bot
        // that cannot click ServUO's client invitation gump.
        public static string AddBotToPlayerParty(Mobile player)
        {
            if (!Core.UOR)
                return "Native parties require a UOR-or-later expansion. This shard is configured earlier than UOR.";
            if (player == null || player.Deleted || !player.Alive || player.Map == null || player.Map == Map.Internal)
                return "You must be alive on a normal game facet to lead a PlayerBot party.";

            var party = Party.Get(player);
            if (party != null && party.Leader != player)
                return "Only a party leader can recruit a PlayerBot.";
            if (party != null && party.Members.Count + party.Candidates.Count >= Party.Capacity)
                return "Your party is full.";

            PlayerBot closest = null;
            var closestDistance = Double.MaxValue;
            foreach (var bot in PlayerBotService.FindBots())
            {
                if (!PlayerBotService.IsEligibleForAutonomousParty(bot) || Party.Get(bot) != null || bot.Map != player.Map
                    || !bot.InRange(player, 18)) continue;
                var distance = bot.GetDistanceToSqrt(player);
                if (distance >= closestDistance) continue;
                closest = bot;
                closestDistance = distance;
            }
            if (closest == null)
                return "No eligible unpartied PlayerBot is within 18 tiles.";

            Party.Invite(player, closest);
            party = Party.Get(player);
            if (party == null) return "ServUO could not create the native party.";
            party.OnAccept(closest, true);
            PlayerLedLeaders.Add(player.Serial.Value);
            return closest.Name + " joined your native party and will follow your lead.";
        }

        public static void Tick(PlayerBot bot)
        {
            var party = Party.Get(bot);
            if (party == null || party.Leader == null) return;
            var leader = party.Leader as PlayerBot;
            if (leader == null)
            {
                TickPlayerLedParty(bot, party, party.Leader);
                return;
            }
            DateTime expires;
            if (!ManagedLeaders.TryGetValue(party.Leader.Serial.Value, out expires)) return;
            if (leader.Deleted || expires <= DateTime.UtcNow)
            {
                ManagedLeaders.Remove(party.Leader.Serial.Value);
                party.Disband();
                return;
            }
            if (!leader.Alive)
            {
                PromoteLivingBotLeader(party, leader, expires);
                return;
            }

            // Dungeon parties share a destination, not a fabricated map move.
            // Each member still takes its own validated route and activates its
            // own live pad before normal follower behavior resumes.
            if (!String.IsNullOrEmpty(bot.DungeonTravelState))
            {
                PlayerBotService.TickTravelBehavior(bot);
                return;
            }

            // Every member still uses the existing native combat adapter.
            // Party medicine only consumes a real Bandage already in a bot's
            // pack; the group does not fabricate damage, loot, or travel.
            var leaderTarget = leader.Combatant as Mobile;
            if (bot != leader && leaderTarget != null && !leaderTarget.Deleted && leaderTarget.Alive
                && bot.Map == leaderTarget.Map && bot.InRange(leaderTarget, 12) && bot.CanBeHarmful(leaderTarget, false))
            {
                bot.Combatant = leaderTarget;
                bot.Warmode = true;
            }
            if (TryHealPartyMember(bot, party)) return;
            if (PlayerBotService.ShouldFightBehavior(bot))
            {
                PlayerBotService.TickCombatBehavior(bot);
                return;
            }

            if (bot == leader)
            {
                PlayerBotService.TickTravelBehavior(bot);
                return;
            }

            FollowLeader(bot, leader);
        }

        private static void TickPlayerLedParty(PlayerBot bot, Party party, Mobile leader)
        {
            if (leader == null || leader.Deleted || !PlayerLedLeaders.Contains(leader.Serial.Value)) return;
            // A real player's party is never rebuilt or silently resumed by
            // bots after that player's death. Leaving the native party here
            // prevents a headless follower from remaining tied to a corpse.
            if (!leader.Alive)
            {
                PlayerLedSeparationSince.Remove(bot.Serial.Value);
                party.Remove(bot);
                if (party.Members.Count <= 1) PlayerLedLeaders.Remove(leader.Serial.Value);
                return;
            }
            // A headless bot has no client-side moongate or recall action to
            // follow a player across facets. Keep the native party intact for
            // a short grace period, then leave rather than pinning the bot to
            // an unreachable leader forever.
            if (bot.Map != leader.Map)
            {
                LeavePlayerPartyAfterSeparation(bot, party, leader);
                return;
            }
            PlayerLedSeparationSince.Remove(bot.Serial.Value);
            var target = leader.Combatant as Mobile;
            if (target != null && !target.Deleted && target.Alive && target.Map == bot.Map
                && bot.InRange(target, 12) && bot.CanBeHarmful(target, false))
            {
                bot.Combatant = target;
                bot.Warmode = true;
            }
            if (TryHealPartyMember(bot, party)) return;
            if (PlayerBotService.ShouldFightBehavior(bot))
            {
                PlayerBotService.TickCombatBehavior(bot);
                return;
            }
            FollowLeader(bot, leader);
        }

        private static void LeavePlayerPartyAfterSeparation(PlayerBot bot, Party party, Mobile leader)
        {
            var now = DateTime.UtcNow;
            DateTime since;
            if (!PlayerLedSeparationSince.TryGetValue(bot.Serial.Value, out since))
            {
                PlayerLedSeparationSince[bot.Serial.Value] = now;
                bot.NextAction = now + TimeSpan.FromSeconds(5);
                return;
            }
            if (now - since < PlayerLedSeparationLimit)
            {
                bot.NextAction = now + TimeSpan.FromSeconds(5);
                return;
            }

            PlayerLedSeparationSince.Remove(bot.Serial.Value);
            party.Remove(bot);
            if (party.Members.Count <= 1) PlayerLedLeaders.Remove(leader.Serial.Value);
            bot.NextAction = now + TimeSpan.FromSeconds(2);
        }

        private static void FollowLeader(PlayerBot bot, Mobile leader)
        {
            if (bot == null || leader == null || bot.Map != leader.Map)
            {
                ClearFollowRoute(bot);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(650, 1000));
                return;
            }
            if (bot.InRange(leader.Location, 2))
            {
                ClearFollowRoute(bot);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(650, 1000));
                return;
            }

            Point3D target;
            if (TryGetFollowRouteTarget(bot, out target))
            {
                if (!bot.Move(bot.GetDirectionTo(target) | Direction.Running))
                    ClearFollowRoute(bot);
            }
            else if (PlayerBotWorldData.TryPlanLocalRoute(bot, leader.Location)
                && TryGetFollowRouteTarget(bot, out target))
            {
                bot.Move(bot.GetDirectionTo(target) | Direction.Running);
            }
            else
            {
                // The planner only accepts nearby paths. This preserves the
                // original ordinary movement behavior while a distant leader
                // closes the gap, without treating a party as a teleport.
                bot.Move(bot.GetDirectionTo(leader.Location) | Direction.Running);
            }
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 850));
        }

        private static bool TryGetFollowRouteTarget(PlayerBot bot, out Point3D target)
        {
            target = Point3D.Zero;
            if (bot == null || bot.RoutePoints == null) return false;
            while (bot.RouteIndex < bot.RoutePoints.Count && bot.InRange(bot.RoutePoints[bot.RouteIndex], 1))
                bot.RouteIndex++;
            if (bot.RouteIndex >= bot.RoutePoints.Count) return false;
            target = bot.RoutePoints[bot.RouteIndex];
            return true;
        }

        private static void ClearFollowRoute(PlayerBot bot)
        {
            if (bot == null || bot.RoutePoints == null) return;
            bot.RoutePoints.Clear();
            bot.RouteIndex = 0;
        }

        // A party medic only consumes a real Bandage already in its pack.
        // Healing resolution, timing, skill checks, and the actual hit change
        // stay entirely inside ServUO's native BandageContext.
        private static bool TryHealPartyMember(PlayerBot bot, Party party)
        {
            if (bot == null || party == null || bot.Backpack == null || BandageContext.GetContext(bot) != null) return false;
            var bandage = bot.Backpack.FindItemByType<Bandage>();
            if (bandage == null || bandage.Deleted) return false;

            Mobile patient = null;
            var lowestRatio = 1.0;
            foreach (var member in party.Members)
            {
                var candidate = member.Mobile;
                if (candidate == null || candidate.Deleted || !candidate.Alive || candidate.Map != bot.Map
                    || !bot.InRange(candidate, Bandage.Range) || candidate.Hits >= candidate.HitsMax) continue;
                var ratio = candidate.HitsMax <= 0 ? 1.0 : (double)candidate.Hits / candidate.HitsMax;
                if (ratio >= lowestRatio || !bot.CanBeBeneficial(candidate, true, true)) continue;
                patient = candidate;
                lowestRatio = ratio;
            }
            if (patient == null) return false;
            if (BandageContext.BeginHeal(bot, patient) == null) return false;
            NegativeAttributes.OnCombatAction(bot);
            bandage.Consume();
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            return true;
        }

        // Groups begin only when compatible idle travelers are already close
        // together. This is a local muster, not an invisible recall or map
        // teleport, and the ordinary native Party remains the source of truth.
        public static void ReconcileAutonomousParties()
        {
            var now = DateTime.UtcNow;
            if (!Core.UOR || now < NextAutonomousReconcile) return;
            NextAutonomousReconcile = now + AutonomousReconcileInterval;

            foreach (var serial in new List<int>(PlayerLedSeparationSince.Keys))
            {
                var bot = World.FindMobile((Serial)serial) as PlayerBot;
                if (bot == null || bot.Deleted || Party.Get(bot) == null)
                    PlayerLedSeparationSince.Remove(serial);
            }

            foreach (var bot in PlayerBotService.FindBots())
            {
                var existing = Party.Get(bot);
                if (existing == null || existing.Leader != bot || !ManagedLeaders.ContainsKey(bot.Serial.Value)) continue;
                DateTime expires;
                if (!ManagedLeaders.TryGetValue(bot.Serial.Value, out expires)) continue;
                if (!bot.Alive || bot.Deleted || expires <= now)
                {
                    ManagedLeaders.Remove(bot.Serial.Value);
                    existing.Disband();
                    continue;
                }
                foreach (var member in new List<PartyMemberInfo>(existing.Members))
                {
                    var memberBot = member.Mobile as PlayerBot;
                    if (memberBot != null && memberBot != bot && (!memberBot.Alive || memberBot.Deleted))
                        existing.Remove(memberBot);
                }
            }

            // One low-frequency formation attempt per reconciliation keeps the
            // population social without turning every town arrival into a raid.
            if (Utility.RandomDouble() >= 0.20) return;
            foreach (var leader in PlayerBotService.FindBots())
            {
                if (!PlayerBotService.IsEligibleForAutonomousParty(leader) || Party.Get(leader) != null) continue;
                var recruits = new List<PlayerBot> { leader };
                foreach (var candidate in PlayerBotService.FindBots())
                {
                    if (candidate == leader || !PlayerBotService.IsEligibleForAutonomousParty(candidate)
                        || Party.Get(candidate) != null || candidate.Map != leader.Map || !candidate.InRange(leader, 8)) continue;
                    // A native guild is a real social affiliation, so its
                    // members muster as a crew instead of mixing randomly.
                    if (candidate.Guild != leader.Guild) continue;
                    recruits.Add(candidate);
                    if (recruits.Count == 4) break;
                }
                if (recruits.Count < 2) continue;
                Form(recruits, "autonomous");
                return;
            }

            // Tavern visitors are already physically gathered at the same
            // social destination. Turn that visible LFG moment into a normal
            // native party, then clear the visit state so Party behavior can
            // lead the group out on an ordinary route.
            foreach (var leader in PlayerBotService.FindBots())
            {
                if (!IsTavernMusterCandidate(leader, null)) continue;
                var recruits = new List<PlayerBot> { leader };
                foreach (var candidate in PlayerBotService.FindBots())
                {
                    if (candidate == leader || Party.Get(candidate) != null || !IsTavernMusterCandidate(candidate, leader)) continue;
                    if (!String.Equals(candidate.BankVisitName, leader.BankVisitName, StringComparison.Ordinal)) continue;
                    if (candidate.Guild != leader.Guild) continue;
                    recruits.Add(candidate);
                    if (recruits.Count == 4) break;
                }
                if (recruits.Count < 2) continue;
                foreach (var member in recruits) PlayerBotService.ResumeTravelAfterPartyMuster(member);
                Form(recruits, "tavern LFG");
                return;
            }
        }

        private static bool IsTavernMusterCandidate(PlayerBot bot, PlayerBot leader)
        {
            if (bot == null || bot.Deleted || !bot.Alive || bot.Map == null || bot.Map == Map.Internal
                || Party.Get(bot) != null || (bot.BotRole != PlayerBotRole.Traveler && bot.BotRole != PlayerBotRole.Adventurer)
                || !String.Equals(bot.BankVisitKind, "Tavern", StringComparison.OrdinalIgnoreCase)
                || bot.BankVisitHome == Point3D.Zero || !bot.InRange(bot.BankVisitHome, 2)
                || bot.Combatant != null || bot.LaborKind != PlayerBotLaborKind.None || bot.CorpseRecoverySerial != 0)
                return false;
            return leader == null || (bot.Map == leader.Map && bot.InRange(leader, 8));
        }

        private static void Form(List<PlayerBot> members, string kind)
        {
            var leader = members[0];
            var party = new Party(leader);
            leader.Party = party;
            for (var i = 1; i < members.Count; i++) party.Add(members[i]);
            ManagedLeaders[leader.Serial.Value] = DateTime.UtcNow + Lifetime;
            PlayerBotService.RecordPartyEvent(leader.Name + " formed an " + kind + " party with " + members.Count + " bots.");
        }

        // ServUO's native Party leader is immutable. For a bot-only managed
        // party, a dead leader therefore gets a clean native disband/reform
        // around the surviving bots instead of leaving followers attached to
        // a corpse. Player-led parties are intentionally excluded: a player
        // chooses whether to rebuild their own party after a death.
        private static void PromoteLivingBotLeader(Party party, PlayerBot formerLeader, DateTime expires)
        {
            if (party == null || formerLeader == null) return;
            var survivors = new List<PlayerBot>();
            foreach (var member in party.Members)
            {
                var bot = member.Mobile as PlayerBot;
                if (bot != null && !bot.Deleted && bot.Alive) survivors.Add(bot);
            }

            ManagedLeaders.Remove(formerLeader.Serial.Value);
            party.Disband();
            if (survivors.Count < 2) return;

            var successor = survivors[0];
            var rebuilt = new Party(successor);
            successor.Party = rebuilt;
            for (var i = 1; i < survivors.Count; i++) rebuilt.Add(survivors[i]);
            ManagedLeaders[successor.Serial.Value] = expires;
            PlayerBotService.RecordPartyEvent(successor.Name + " took over a native PlayerBot party after " + formerLeader.Name + " fell.");
        }
    }
}
