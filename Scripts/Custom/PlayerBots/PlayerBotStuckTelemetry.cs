using System;
using System.Collections.Generic;

namespace Server.CustomBots
{
    // Bounded diagnostic equivalent of UO Offline's stuck ledger. It records
    // failed native moves but never teleports a bot or changes route geometry.
    internal static class PlayerBotStuckTelemetry
    {
        private static readonly Queue<string> Recent = new Queue<string>();
        private static readonly Dictionary<int, FailureTrack> Failures = new Dictionary<int, FailureTrack>();
        private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(3);
        private const int SameSpotRange = 3;

        private sealed class FailureTrack
        {
            public string MapName;
            public Point3D Location;
            public DateTime StartedAt;
            public DateTime LastAt;
            public int Count;
        }

        internal static void RecordMoveFailure(PlayerBot bot, Point3D target)
        {
            if (bot == null || bot.Map == null || bot.Map == Map.Internal) return;
            var now = DateTime.UtcNow;
            FailureTrack track;
            if (!Failures.TryGetValue(bot.Serial.Value, out track)
                || !String.Equals(track.MapName, bot.Map.Name, StringComparison.OrdinalIgnoreCase)
                || now - track.LastAt > FailureWindow
                || ChebyshevDistance(track.Location, bot.Location) > SameSpotRange)
            {
                track = new FailureTrack
                {
                    MapName = bot.Map.Name,
                    Location = bot.Location,
                    StartedAt = now,
                    Count = 0
                };
                Failures[bot.Serial.Value] = track;
            }

            track.LastAt = now;
            track.Count++;

            if (track.Count == 1 || track.Count == 3 || track.Count % 10 == 0)
            {
                var detail = bot.Map.Name + " " + bot.Name + " blocked near " + target.X + "," + target.Y
                    + " (" + track.Count + " failed move" + (track.Count == 1 ? "" : "s") + " in "
                    + Math.Max(1, (int)(now - track.StartedAt).TotalMinutes + 1) + " min)";
                Recent.Enqueue(now.ToString("HH:mm:ss") + " " + detail);
                while (Recent.Count > 100) Recent.Dequeue();
                PlayerBotService.RecordEvent((track.Count == 1 ? "Route move blocked for " : "Navigation stall detected for ") + detail + ".");
            }
        }

        internal static void RecordMoveSuccess(PlayerBot bot)
        {
            if (bot != null) Failures.Remove(bot.Serial.Value);
        }

        internal static List<string> GetRecent(int maximum)
        {
            var entries = new List<string>(Recent);
            if (maximum <= 0 || entries.Count <= maximum) return entries;
            return entries.GetRange(entries.Count - maximum, maximum);
        }

        private static int ChebyshevDistance(Point3D first, Point3D second)
        {
            return Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));
        }
    }
}
