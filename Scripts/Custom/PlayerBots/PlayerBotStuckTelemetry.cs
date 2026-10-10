using System;
using System.Collections.Generic;

namespace Server.CustomBots
{
    // Bounded diagnostic equivalent of UO Offline's stuck ledger. It records
    // failed native moves but never teleports a bot or changes route geometry.
    internal static class PlayerBotStuckTelemetry
    {
        private static readonly Queue<string> Recent = new Queue<string>();

        internal static void RecordMoveFailure(PlayerBot bot, Point3D target)
        {
            if (bot == null || bot.Map == null || bot.Map == Map.Internal) return;
            Recent.Enqueue(DateTime.UtcNow.ToString("HH:mm:ss") + " " + bot.Map.Name + " " + bot.Name + " blocked near " + target.X + "," + target.Y);
            while (Recent.Count > 100) Recent.Dequeue();
            PlayerBotService.RecordEvent("Route move blocked for " + bot.Name + " near " + target.X + "," + target.Y + ".");
        }
    }
}
