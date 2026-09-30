using System;
using System.Collections.Generic;
using Server.Guilds;

namespace Server.CustomBots
{
    // Native guilds are persistent ServUO world entities. This adapter only
    // creates or extends its explicitly named bot guild; it never changes a
    // player guild or adds a real player without that player's own consent.
    public static class PlayerBotGuilds
    {
        private const string GuildName = "PlayerBot Fellowship";
        private const string GuildAbbreviation = "PBF";
        private static readonly Dictionary<int, DateTime> NextGuildChat = new Dictionary<int, DateTime>();

        public static string FormNear(Mobile caller, int requestedCount)
        {
            if (caller == null || caller.Map == null || caller.Map == Map.Internal)
                return "Stand on a normal game facet before creating the PlayerBot guild.";

            var count = Math.Max(2, Math.Min(10, requestedCount));
            var candidates = new List<PlayerBot>();
            foreach (var bot in PlayerBotService.FindBots())
            {
                if (bot == null || bot.Deleted || !bot.Alive || bot.Map != caller.Map || bot.Guild != null)
                    continue;
                if (bot.InRange(caller.Location, 18)) candidates.Add(bot);
            }
            candidates.Sort(delegate(PlayerBot left, PlayerBot right)
            {
                return left.GetDistanceToSqrt(caller).CompareTo(right.GetDistanceToSqrt(caller));
            });
            if (candidates.Count < 2)
                return "Need at least two living, unguilded PlayerBots within 18 tiles to form the native bot guild.";

            var guild = FindBotGuild();
            if (guild == null)
                guild = new Guild(candidates[0], GuildName, GuildAbbreviation);

            var added = guild.Members.Count == 0 ? 0 : 1;
            foreach (var bot in candidates)
            {
                if (added >= count) break;
                if (bot.Guild == null)
                {
                    guild.AddMember(bot);
                    added++;
                }
            }
            return "Native guild [" + GuildAbbreviation + "] now has " + guild.Members.Count + " PlayerBot member(s).";
        }

        // Membership is player-initiated. A player must deliberately run the
        // command beside a live Fellowship bot; bots never recruit, target, or
        // alter a real player's guild on their own.
        public static string JoinPlayer(Mobile player)
        {
            if (player == null || player.Deleted || !player.Alive || player.Map == null || player.Map == Map.Internal)
                return "You must be alive on a normal game facet to join the PlayerBot Fellowship.";
            if (player.Guild != null)
                return "Leave your current guild before joining the PlayerBot Fellowship.";

            var guild = FindBotGuild();
            if (guild == null)
                return "The PlayerBot Fellowship does not exist yet. Ask a GM to form it with nearby bots first.";
            var nearbyMember = false;
            foreach (var member in guild.Members)
            {
                var bot = member as PlayerBot;
                if (bot != null && !bot.Deleted && bot.Alive && bot.Map == player.Map && bot.InRange(player, 18))
                {
                    nearbyMember = true;
                    break;
                }
            }
            if (!nearbyMember)
                return "Stand within 18 tiles of a live PlayerBot Fellowship member to join.";

            guild.AddMember(player);
            return "You joined the PlayerBot Fellowship [" + GuildAbbreviation + "].";
        }

        // Guild.GuildChat is the shard's own recipient and packet path. A
        // headless bot has no NetState, but any online player who deliberately
        // joined the Fellowship receives the normal guild message. The short
        // in-memory cooldown prevents bank sitters from turning it into spam.
        public static bool TryGuildChat(PlayerBot bot)
        {
            if (bot == null || bot.Deleted || !bot.Alive || Utility.RandomDouble() >= 0.18) return false;
            var guild = FindBotGuild();
            if (guild == null || bot.Guild != guild || guild.Members.Count < 2) return false;

            DateTime next;
            var now = DateTime.UtcNow;
            if (NextGuildChat.TryGetValue(bot.Serial.Value, out next) && now < next) return false;

            var lines = new[]
            {
                "Fellowship check-in. Anyone need a hand?",
                "Holding the bank. Roads look clear from here.",
                "LFG for a safe road run when the crew gathers.",
                "Good hunting, Fellowship. Meet at the bank when ready."
            };
            guild.GuildChat(bot, lines[Utility.Random(lines.Length)]);
            NextGuildChat[bot.Serial.Value] = now + TimeSpan.FromMinutes(Utility.RandomMinMax(2, 5));
            return true;
        }

        private static Guild FindBotGuild()
        {
            var guild = BaseGuild.FindByAbbrev(GuildAbbreviation) as Guild;
            if (guild == null || guild.Disbanded || !String.Equals(guild.Name, GuildName, StringComparison.Ordinal))
                return null;
            return guild;
        }
    }
}
