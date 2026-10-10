using System;
using System.Collections.Generic;

namespace Server.CustomBots
{
    // A bounded adaptation of UO Offline's destination danger map.  Heat is
    // derived only from a murder report the ServUO engine has already accepted;
    // it never invents red sightings, guard calls, teleports, or combat.
    internal static class PlayerBotDangerMap
    {
        private const double HalfLifeMinutes = 45.0;
        private const double WarmHeat = 1.5;
        private const double HotHeat = 3.0;
        private const int EventDestinationRange = 24;

        private sealed class HeatEntry
        {
            public double Value;
            public DateTime UpdatedAt;
        }

        private static readonly Dictionary<string, HeatEntry> Heat =
            new Dictionary<string, HeatEntry>(StringComparer.OrdinalIgnoreCase);

        internal static void NoteReportedMurder(PlayerBot victim)
        {
            if (victim == null || victim.Deleted || victim.Map == null || victim.Map == Map.Internal)
                return;

            var destination = PlayerBotWorldData.FindNearestDestination(
                victim.Map, victim.Location, EventDestinationRange);
            if (destination == null)
                return;

            AddHeat(Key(victim.Map, destination.Name), 1.0);
        }

        internal static double DestinationWeight(Map map, PlayerBotDestination destination)
        {
            if (map == null || destination == null || String.IsNullOrEmpty(destination.Name))
                return 1.0;

            var heat = CurrentHeat(Key(map, destination.Name));
            return heat >= HotHeat ? 0.25 : heat >= WarmHeat ? 0.5 : 1.0;
        }

        private static void AddHeat(string key, double amount)
        {
            HeatEntry entry;
            if (!Heat.TryGetValue(key, out entry))
            {
                entry = new HeatEntry { UpdatedAt = DateTime.UtcNow };
                Heat[key] = entry;
            }

            entry.Value = CurrentHeat(entry) + amount;
            entry.UpdatedAt = DateTime.UtcNow;
            if (Heat.Count > 500)
                Prune();
        }

        private static double CurrentHeat(string key)
        {
            HeatEntry entry;
            return Heat.TryGetValue(key, out entry) ? CurrentHeat(entry) : 0.0;
        }

        private static double CurrentHeat(HeatEntry entry)
        {
            var elapsed = Math.Max(0.0, (DateTime.UtcNow - entry.UpdatedAt).TotalMinutes);
            return entry.Value * Math.Pow(0.5, elapsed / HalfLifeMinutes);
        }

        private static void Prune()
        {
            var stale = new List<string>();
            foreach (var pair in Heat)
                if (CurrentHeat(pair.Value) < 0.1)
                    stale.Add(pair.Key);
            foreach (var key in stale)
                Heat.Remove(key);
        }

        private static string Key(Map map, string destinationName)
        {
            return map.Name + ":" + destinationName;
        }
    }
}
