using System;
using System.Collections.Generic;

namespace Server.CustomBots
{
    // Bot-only stories. Callers must supply an already-sanitized event.
    public static class PlayerBotJournal
    {
        private const int Capacity = 100;
        private static readonly Queue<string> Entries = new Queue<string>();

        internal static void Record(string message)
        {
            if (String.IsNullOrEmpty(message)) return;
            Entries.Enqueue(DateTime.UtcNow.ToString("HH:mm") + " " + message);
            while (Entries.Count > Capacity) Entries.Dequeue();
        }

        internal static string PickRecent()
        {
            if (Entries.Count == 0) return null;
            var entries = Entries.ToArray();
            return entries[Utility.Random(entries.Length)];
        }
    }
}
