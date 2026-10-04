using System;
using Server.Items;
using Server.Network;

namespace Server.CustomBots
{
    // A narrow adapter for UO Offline's treasure-hunt scene. The map, dig
    // timer, chest, guardians, and rewards all remain ServUO-owned.
    public static class PlayerBotTreasureHunts
    {
        private const int DigRange = 4;
        private static readonly TimeSpan AcquisitionReconcileInterval = TimeSpan.FromMinutes(2);
        private static DateTime _nextAcquisitionReconcile;

        // Fishing is one of ServUO's native TreasureMap sources. Keep the
        // UO Offline map-sale scene honest: a hunter can acquire only an
        // actual unfinished map already caught by a nearby PlayerBot fisher,
        // and its existing gold physically changes hands with that map.
        internal static void ReconcileAutonomousAcquisition()
        {
            var now = DateTime.UtcNow;
            if (now < _nextAcquisitionReconcile) return;
            _nextAcquisitionReconcile = now + AcquisitionReconcileInterval;
            if (Utility.RandomDouble() >= 0.25) return;

            foreach (var hunter in PlayerBotService.FindBots())
            {
                if (!IsEligibleBuyer(hunter)) continue;
                foreach (var fisher in PlayerBotService.FindBots())
                {
                    if (!IsEligibleSeller(hunter, fisher)) continue;
                    var map = fisher.Backpack.FindItemByType<TreasureMap>();
                    if (map == null || map.Deleted || map.Completed || map.RootParent != fisher || map.Facet != hunter.Map) continue;
                    if (TryPurchaseMap(hunter, fisher, map)) return;
                }
            }
        }

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

        private static bool IsEligibleBuyer(PlayerBot hunter)
        {
            return hunter != null && !hunter.Deleted && hunter.Alive && hunter.BotRole == PlayerBotRole.TreasureHunter
                && hunter.HuntMap == null && hunter.HuntChest == null && hunter.Backpack != null
                && hunter.Map != null && hunter.Map != Map.Internal && hunter.Combatant == null
                && hunter.LaborKind == PlayerBotLaborKind.None && hunter.Destination == Point3D.Zero;
        }

        private static bool IsEligibleSeller(PlayerBot hunter, PlayerBot fisher)
        {
            return fisher != null && fisher != hunter && !fisher.Deleted && fisher.Alive
                && fisher.LaborKind == PlayerBotLaborKind.Fisher && fisher.Backpack != null
                && fisher.Map == hunter.Map && fisher.Combatant == null && fisher.InRange(hunter, 8);
        }

        private static bool TryPurchaseMap(PlayerBot hunter, PlayerBot fisher, TreasureMap map)
        {
            var price = Utility.RandomMinMax(150, 400);
            var purse = hunter.Backpack.FindItemByType<Gold>();
            if (purse == null || purse.Deleted || purse.Amount < price
                || !hunter.Backpack.CheckHold(hunter, map, false, true)) return false;

            var payment = TakeFromPack(hunter.Backpack, purse, price);
            if (payment == null || !fisher.Backpack.CheckHold(fisher, payment, false, true))
            {
                if (payment != null) hunter.Backpack.DropItem(payment);
                return false;
            }

            fisher.Backpack.RemoveItem(map);
            hunter.Backpack.DropItem(map);
            if (map.RootParent != hunter)
            {
                fisher.Backpack.DropItem(map);
                hunter.Backpack.DropItem(payment);
                return false;
            }

            fisher.Backpack.DropItem(payment);
            if (!Assign(hunter, map)) return false;
            hunter.Say("I'll take that treasure map.");
            fisher.Say("A fair trade.");
            PlayerBotService.RecordEvent(hunter.Name + " bought a native treasure map from " + fisher.Name + " for " + price + " gold.");
            return true;
        }

        private static Item TakeFromPack(Container pack, Item item, int amount)
        {
            if (pack == null || item == null || item.Deleted || amount <= 0 || amount > item.Amount) return null;
            if (amount == item.Amount)
            {
                pack.RemoveItem(item);
                return item;
            }
            var moved = Mobile.LiftItemDupe(item, amount);
            if (moved != null) pack.RemoveItem(moved);
            return moved;
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

            if (chest.TrapType != TrapType.None)
            {
                if (bot.UseSkill(SkillName.RemoveTrap) && bot.Target != null) bot.Target.Invoke(bot, chest);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(12);
                return;
            }

            // Opening remains ServUO's own access gate. Item lifting and
            // subsequent guardian spawns stay entirely under the chest.
            chest.OnDoubleClick(bot);
            TryLootOne(bot, chest);
            if (chest.Items.Count == 0)
            {
                Clear(bot, "looted a native treasure chest");
                return;
            }
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(3);
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

        private static void TryLootOne(PlayerBot bot, TreasureMapChest chest)
        {
            if (bot.Backpack == null || chest.Items.Count == 0) return;
            var item = chest.Items[0];
            LRReason reject = LRReason.CannotLift;
            if (item == null || item.Deleted || !chest.CheckLift(bot, item, ref reject)) return;
            chest.OnItemLifted(bot, item);
            bot.Backpack.TryDropItem(bot, item, false);
        }
    }
}
