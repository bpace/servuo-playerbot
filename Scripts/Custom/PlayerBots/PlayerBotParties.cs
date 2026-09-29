using System;
using System.Collections.Generic;
using Server.Engines.PartySystem;

namespace Server.CustomBots
{
    // A deliberately small adapter over ServUO's own transient Party object.
    // It never serializes a party, fakes a packet, or changes party loot and
    // chat rules. The registry only owns expiry and follower movement.
    public static class PlayerBotParties
    {
        private static readonly Dictionary<int, DateTime> ManagedLeaders = new Dictionary<int, DateTime>();
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
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
            return ManagedLeaders.ContainsKey(party.Leader.Serial.Value);
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

        public static void Tick(PlayerBot bot)
        {
            var party = Party.Get(bot);
            if (party == null || party.Leader == null) return;
            DateTime expires;
            if (!ManagedLeaders.TryGetValue(party.Leader.Serial.Value, out expires)) return;

            var leader = party.Leader as PlayerBot;
            if (leader == null || leader.Deleted || expires <= DateTime.UtcNow)
            {
                ManagedLeaders.Remove(party.Leader.Serial.Value);
                party.Disband();
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

            // Every member still uses the existing native combat adapter;
            // groups do not gain fabricated damage, loot, healing, or travel.
            var leaderTarget = leader.Combatant as Mobile;
            if (bot != leader && leaderTarget != null && !leaderTarget.Deleted && leaderTarget.Alive
                && bot.Map == leaderTarget.Map && bot.InRange(leaderTarget, 12) && bot.CanBeHarmful(leaderTarget, false))
            {
                bot.Combatant = leaderTarget;
                bot.Warmode = true;
            }
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

            if (bot.Map != leader.Map || !bot.InRange(leader.Location, 2))
            {
                if (bot.Map == leader.Map)
                    bot.Move(bot.GetDirectionTo(leader.Location) | Direction.Running);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 850));
                return;
            }
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(650, 1000));
        }

        // Groups begin only when compatible idle travelers are already close
        // together. This is a local muster, not an invisible recall or map
        // teleport, and the ordinary native Party remains the source of truth.
        public static void ReconcileAutonomousParties()
        {
            var now = DateTime.UtcNow;
            if (!Core.UOR || now < NextAutonomousReconcile) return;
            NextAutonomousReconcile = now + AutonomousReconcileInterval;

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
                    recruits.Add(candidate);
                    if (recruits.Count == 4) break;
                }
                if (recruits.Count < 2) continue;
                Form(recruits, "autonomous");
                return;
            }
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
    }
}
