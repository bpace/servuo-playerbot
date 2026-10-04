using System;
using System.Collections.Generic;

namespace Server.CustomBots
{
    // Bot-only stories. Callers must supply an already-sanitized event.
    public static class PlayerBotJournal
    {
        private const int Capacity = 100;
        private static readonly TimeSpan StoryMinAge = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan StoryMaxAge = TimeSpan.FromHours(3);
        private static readonly TimeSpan RetellCooldown = TimeSpan.FromMinutes(15);
        private static readonly Queue<Story> Entries = new Queue<Story>();
        private static readonly Dictionary<int, DateTime> NextTell = new Dictionary<int, DateTime>();

        private sealed class Story
        {
            public int ActorSerial;
            public DateTime At;
            public string Text;
        }

        internal static void RecordParty(PlayerBot leader, int members)
        {
            if (leader == null || leader.Deleted || String.IsNullOrEmpty(leader.Name) || members < 2) return;
            Record(leader, leader.Name + " formed a party with " + members + " companions.");
        }

        internal static void RecordGuildRecruit(PlayerBot recruit, string guildName)
        {
            if (recruit == null || recruit.Deleted || String.IsNullOrEmpty(recruit.Name) || String.IsNullOrEmpty(guildName)) return;
            Record(recruit, recruit.Name + " joined " + guildName + ".");
        }

        internal static void RecordTreasureCompletion(PlayerBot hunter)
        {
            if (hunter == null || hunter.Deleted || String.IsNullOrEmpty(hunter.Name)) return;
            Record(hunter, hunter.Name + " returned from a treasure hunt.");
        }

        private static void Record(PlayerBot actor, string text)
        {
            if (actor == null || actor.Deleted || String.IsNullOrEmpty(text)) return;
            Entries.Enqueue(new Story
            {
                ActorSerial = actor.Serial.Value,
                At = DateTime.UtcNow,
                Text = text
            });
            while (Entries.Count > Capacity) Entries.Dequeue();
        }

        // UO Offline's event journal only repeats real events. This smaller
        // ServUO slice intentionally carries only PlayerBot party formation,
        // excludes the speaker's own story, and is never a dashboard feed.
        internal static string PickRecentFor(PlayerBot speaker)
        {
            if (speaker == null || speaker.Deleted || Entries.Count == 0) return null;

            var now = DateTime.UtcNow;
            DateTime allowedAt;
            if (NextTell.TryGetValue(speaker.Serial.Value, out allowedAt) && now < allowedAt) return null;

            var candidates = new List<Story>();
            foreach (var story in Entries)
            {
                var age = now - story.At;
                if (story.ActorSerial == speaker.Serial.Value || age < StoryMinAge || age > StoryMaxAge) continue;
                candidates.Add(story);
            }
            if (candidates.Count == 0) return null;

            NextTell[speaker.Serial.Value] = now + RetellCooldown;
            if (NextTell.Count > 512)
            {
                var stale = new List<int>();
                foreach (var entry in NextTell)
                    if (entry.Value <= now) stale.Add(entry.Key);
                foreach (var serial in stale) NextTell.Remove(serial);
            }
            return "I heard " + candidates[Utility.Random(candidates.Count)].Text;
        }
    }
}
