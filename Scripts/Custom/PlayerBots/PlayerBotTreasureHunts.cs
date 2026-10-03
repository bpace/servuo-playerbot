using System;
using Server.Items;

namespace Server.CustomBots
{
    // A narrow adapter for UO Offline's treasure-hunt scene. The map, dig
    // timer, chest, guardians, and rewards all remain ServUO-owned.
    public static class PlayerBotTreasureHunts
    {
        private const int DigRange = 4;

        internal static bool IsActive(PlayerBot bot)
        {
            return bot != null && !bot.Deleted && bot.Alive
                && bot.BotRole == PlayerBotRole.TreasureHunter
                && bot.HuntMap != null && !bot.HuntMap.Deleted;
        }

        internal static void Tick(PlayerBot bot)
        {
            var map = bot.HuntMap;
            if (map == null || map.Deleted || bot.Backpack == null || map.RootParent != bot)
            {
                Clear(bot, "lost its treasure map");
                return;
            }

            if (map.Completed)
            {
                TickChest(bot, map);
                return;
            }

            if (map.Facet != bot.Map)
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                return;
            }

            if (map.Decoder == null)
            {
                map.Decode(bot);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                return;
            }

            var chest = new Point3D(map.ChestLocation, map.Facet.GetAverageZ(map.ChestLocation.X, map.ChestLocation.Y));
            if (!bot.InRange(chest, DigRange))
            {
                MoveToDigRange(bot, chest);
                return;
            }

            if (bot.Location.X == chest.X && bot.Location.Y == chest.Y)
            {
                MoveToDigRange(bot, chest);
                return;
            }

            if (!TreasureMap.HasDiggingTool(bot))
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                return;
            }

            if (!bot.CanBeginAction(typeof(TreasureMap)))
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                return;
            }

            map.OnBeginDig(bot);
            if (bot.Target != null)
            {
                bot.Target.Invoke(bot, chest);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            }
            else bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        }

        internal static bool Assign(PlayerBot bot, TreasureMap map)
        {
            if (bot == null || bot.Deleted || map == null || map.Deleted || map.Completed || bot.Backpack == null) return false;
            if (map.RootParent != bot)
            {
                bot.Backpack.DropItem(map);
            }
            if (map.RootParent != bot) return false;
            bot.HuntMap = map;
            bot.HuntChest = null;
            bot.Destination = Point3D.Zero;
            bot.DestinationName = "";
            if (bot.RoutePoints != null) bot.RoutePoints.Clear();
            bot.RouteIndex = 0;
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            return true;
        }

        private static void MoveToDigRange(PlayerBot bot, Point3D chest)
        {
            var map = bot.Map;
            var offsets = new[] { new Point2D(2, 0), new Point2D(-2, 0), new Point2D(0, 2), new Point2D(0, -2), new Point2D(2, 2), new Point2D(-2, 2), new Point2D(2, -2), new Point2D(-2, -2) };
            foreach (var offset in offsets)
            {
                var x = chest.X + offset.X;
                var y = chest.Y + offset.Y;
                var z = map.GetAverageZ(x, y);
                if (!map.CanFit(x, y, z, 16, false, false)) continue;
                bot.Destination = new Point3D(x, y, z);
                bot.DestinationName = "Treasure map";
                if (bot.RoutePoints != null) bot.RoutePoints.Clear();
                bot.RouteIndex = 0;
                PlayerBotService.TickTravelBehavior(bot);
                return;
            }
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        }

        private static void Clear(PlayerBot bot, string outcome)
        {
            bot.HuntMap = null;
            bot.HuntChest = null;
            bot.Destination = Point3D.Zero;
            bot.DestinationName = "";
            if (bot.RoutePoints != null) bot.RoutePoints.Clear();
            bot.RouteIndex = 0;
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            PlayerBotService.RecordEvent(bot.Name + " " + outcome + ".");
        }

        private static void TickChest(PlayerBot bot, TreasureMap map)
        {
            var chest = bot.HuntChest;
            if (chest == null || chest.Deleted || chest.TreasureMap != map)
            {
                chest = FindChest(map);
                bot.HuntChest = chest;
            }
            if (chest == null)
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                return;
            }

            if (HasLivingGuardians(chest))
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                return;
            }

            if (!bot.InRange(chest.GetWorldLocation(), 1))
            {
                bot.Destination = chest.GetWorldLocation();
                bot.DestinationName = "Treasure chest";
                if (bot.RoutePoints != null) bot.RoutePoints.Clear();
                bot.RouteIndex = 0;
                PlayerBotService.TickTravelBehavior(bot);
                return;
            }

            if (chest.Locked)
            {
                var pick = bot.Backpack.FindItemByType<Lockpick>();
                if (pick == null || pick.Deleted) { bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(20); return; }
                pick.OnDoubleClick(bot);
                if (bot.Target != null) bot.Target.Invoke(bot, chest);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                return;
            }

            // Opening remains ServUO's own access gate. Item lifting and
            // subsequent guardian spawns stay entirely under the chest.
            chest.OnDoubleClick(bot);
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        }

        private static TreasureMapChest FindChest(TreasureMap map)
        {
            foreach (Item item in World.Items.Values)
            {
                var chest = item as TreasureMapChest;
                if (chest != null && !chest.Deleted && chest.TreasureMap == map) return chest;
            }
            return null;
        }

        private static bool HasLivingGuardians(TreasureMapChest chest)
        {
            foreach (Mobile guardian in chest.Guardians)
                if (guardian != null && !guardian.Deleted && guardian.Alive) return true;
            foreach (Mobile guardian in chest.AncientGuardians)
                if (guardian != null && !guardian.Deleted && guardian.Alive) return true;
            return false;
        }
    }
}
