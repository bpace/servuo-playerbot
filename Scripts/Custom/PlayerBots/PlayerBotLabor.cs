using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Engines.Harvest;
using Server.Items;
using Server.Mobiles;
using Server.Targeting;

namespace Server.CustomBots
{
    // Manual labor actors are opt-in. They exercise ServUO's real harvest and
    // craft pipelines, so their resources, tool wear, skill checks, and
    // finished goods stay subject to the shard's own rules.
    public static class PlayerBotLabor
    {
        private static readonly TimeSpan ShiftLength = TimeSpan.FromMinutes(10);
        private static readonly HashSet<int> AutonomousWorkers = new HashSet<int>();

        public static bool IsActive(PlayerBot bot)
        {
            return bot != null && bot.LaborKind != PlayerBotLaborKind.None && DateTime.UtcNow < bot.LaborUntil;
        }

        internal static bool StartAutonomousShift(PlayerBot bot, PlayerBotLaborKind kind)
        {
            if (bot == null || bot.LaborKind != PlayerBotLaborKind.None || bot.LaborReturning) return false;
            if (kind == PlayerBotLaborKind.Blacksmith && !PlayerBotShop.TryWithdrawWorkshopOre(bot)) return false;
            if (kind == PlayerBotLaborKind.Carpenter && !PlayerBotShop.TryWithdrawWorkshopLogs(bot)) return false;
            if (kind == PlayerBotLaborKind.Cooker && !PlayerBotShop.TryWithdrawWorkshopFish(bot)) return false;
            bot.LaborReturnName = "";
            bot.LaborKind = kind;
            bot.LaborUntil = DateTime.UtcNow + ShiftLength;
            bot.NextLaborAction = DateTime.UtcNow;
            AutonomousWorkers.Add(bot.Serial.Value);
            Prepare(bot);
            return true;
        }

        public static string StartNear(Mobile gm, string requestedKind)
        {
            if (gm == null || gm.Map == null || gm.Map == Map.Internal) return "Stand in the world before starting a labor bot.";
            PlayerBotLaborKind kind;
            if (!TryParse(requestedKind, out kind)) return "Usage: [PlayerBots labor miner|lumberjack|blacksmith|carpenter|fisher|cooker]";

            var bot = new PlayerBot(PlayerBotRole.Adventurer);
            bot.MoveToWorld(gm.Location, gm.Map);
            bot.LaborKind = kind;
            bot.LaborUntil = DateTime.UtcNow + ShiftLength;
            bot.NextLaborAction = DateTime.MinValue;
            Prepare(bot);
            return bot.Name + " started a ten-minute " + kind.ToString().ToLowerInvariant() + " shift at your location.";
        }

        public static void Tick(PlayerBot bot)
        {
            if (bot != null && bot.LaborReturning)
            {
                TickReturn(bot);
                return;
            }
            if (!IsActive(bot))
            {
                if (bot != null && bot.LaborKind != PlayerBotLaborKind.None)
                {
                    if (TryBeginReturn(bot)) return;
                    bot.LaborKind = PlayerBotLaborKind.None;
                    bot.LaborUntil = DateTime.MinValue;
                    bot.NextLaborAction = DateTime.MinValue;
                    PlayerBotService.RecordEvent(bot.Name + " finished a labor shift.");
                    PlayerBotService.ResumeTravelAfterLabor(bot);
                }
                return;
            }
            if (bot.Map == null || bot.Map == Map.Internal || !bot.Alive) return;
            if (AutonomousWorkers.Contains(bot.Serial.Value) && !bot.InRange(bot.Destination, 0))
            {
                if (!bot.Move(bot.GetDirectionTo(bot.Destination) | Direction.Running))
                {
                    ClearLabor(bot);
                    return;
                }
                bot.NextLaborAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 850));
                return;
            }
            if (DateTime.UtcNow < bot.NextLaborAction) return;

            Prepare(bot);
            switch (bot.LaborKind)
            {
                case PlayerBotLaborKind.Miner:
                    HarvestSystem.TargetByResource(new TargetByResourceMacroEventArgs(bot, FindOrCreatePickaxe(bot), 0));
                    bot.NextLaborAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(9, 15));
                    break;
                case PlayerBotLaborKind.Lumberjack:
                    HarvestSystem.TargetByResource(new TargetByResourceMacroEventArgs(bot, FindOrCreateHatchet(bot), 2));
                    bot.NextLaborAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(9, 15));
                    break;
                case PlayerBotLaborKind.Blacksmith:
                    TryBuyNearbyOre(bot);
                    TrySmeltOre(bot);
                    TryCraftBlacksmithProduct(bot);
                    bot.NextLaborAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 18));
                    break;
                case PlayerBotLaborKind.Carpenter:
                    TryBuyNearbyLogs(bot);
                    TryMakeBoards(bot);
                    TryCraftWoodenShield(bot);
                    bot.NextLaborAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 18));
                    break;
                case PlayerBotLaborKind.Fisher:
                    TryFish(bot);
                    bot.NextLaborAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 16));
                    break;
                case PlayerBotLaborKind.Cooker:
                    TryBuyNearbyFish(bot);
                    TryCarveFish(bot);
                    TryCookFish(bot);
                    bot.NextLaborAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 18));
                    break;
            }
        }

        private static bool TryParse(string requestedKind, out PlayerBotLaborKind kind)
        {
            kind = PlayerBotLaborKind.None;
            if (String.Equals(requestedKind, "miner", StringComparison.OrdinalIgnoreCase)) kind = PlayerBotLaborKind.Miner;
            else if (String.Equals(requestedKind, "lumberjack", StringComparison.OrdinalIgnoreCase)) kind = PlayerBotLaborKind.Lumberjack;
            else if (String.Equals(requestedKind, "blacksmith", StringComparison.OrdinalIgnoreCase)) kind = PlayerBotLaborKind.Blacksmith;
            else if (String.Equals(requestedKind, "carpenter", StringComparison.OrdinalIgnoreCase)) kind = PlayerBotLaborKind.Carpenter;
            else if (String.Equals(requestedKind, "fisher", StringComparison.OrdinalIgnoreCase)) kind = PlayerBotLaborKind.Fisher;
            else if (String.Equals(requestedKind, "cooker", StringComparison.OrdinalIgnoreCase)) kind = PlayerBotLaborKind.Cooker;
            return kind != PlayerBotLaborKind.None;
        }

        private static void Prepare(PlayerBot bot)
        {
            switch (bot.LaborKind)
            {
                case PlayerBotLaborKind.Miner:
                    bot.Skills[SkillName.Mining].Base = Math.Max(bot.Skills[SkillName.Mining].Base, 75.0);
                    FindOrCreatePickaxe(bot);
                    break;
                case PlayerBotLaborKind.Lumberjack:
                    bot.Skills[SkillName.Lumberjacking].Base = Math.Max(bot.Skills[SkillName.Lumberjacking].Base, 75.0);
                    FindOrCreateHatchet(bot);
                    break;
                case PlayerBotLaborKind.Blacksmith:
                    bot.Skills[SkillName.Blacksmith].Base = Math.Max(bot.Skills[SkillName.Blacksmith].Base, 75.0);
                    FindOrCreateTongs(bot);
                    break;
                case PlayerBotLaborKind.Carpenter:
                    bot.Skills[SkillName.Carpentry].Base = Math.Max(bot.Skills[SkillName.Carpentry].Base, 75.0);
                    bot.Skills[SkillName.Lumberjacking].Base = Math.Max(bot.Skills[SkillName.Lumberjacking].Base, 75.0);
                    FindOrCreateSaw(bot);
                    FindOrCreateHatchet(bot);
                    break;
                case PlayerBotLaborKind.Fisher:
                    bot.Skills[SkillName.Fishing].Base = Math.Max(bot.Skills[SkillName.Fishing].Base, 75.0);
                    FindOrCreateFishingPole(bot);
                    break;
                case PlayerBotLaborKind.Cooker:
                    bot.Skills[SkillName.Cooking].Base = Math.Max(bot.Skills[SkillName.Cooking].Base, 75.0);
                    FindOrCreateButcherKnife(bot);
                    FindOrCreateSkillet(bot);
                    break;
            }
        }

        private static Pickaxe FindOrCreatePickaxe(PlayerBot bot)
        {
            var tool = bot.Backpack == null ? null : bot.Backpack.FindItemByType<Pickaxe>();
            if (tool != null && !tool.Deleted) return tool;
            tool = new Pickaxe();
            bot.AddToBackpack(tool);
            return tool;
        }

        private static Hatchet FindOrCreateHatchet(PlayerBot bot)
        {
            var tool = bot.FindItemOnLayer(Layer.OneHanded) as Hatchet;
            if (tool != null && !tool.Deleted) return tool;
            tool = bot.Backpack == null ? null : bot.Backpack.FindItemByType<Hatchet>();
            if (tool == null || tool.Deleted)
            {
                tool = new Hatchet();
                bot.AddToBackpack(tool);
            }
            if (tool.Parent != bot) bot.EquipItem(tool);
            return tool;
        }

        private static Tongs FindOrCreateTongs(PlayerBot bot)
        {
            var tool = bot.Backpack == null ? null : bot.Backpack.FindItemByType<Tongs>();
            if (tool != null && !tool.Deleted) return tool;
            tool = new Tongs();
            bot.AddToBackpack(tool);
            return tool;
        }

        private static Saw FindOrCreateSaw(PlayerBot bot)
        {
            var tool = bot.Backpack == null ? null : bot.Backpack.FindItemByType<Saw>();
            if (tool != null && !tool.Deleted) return tool;
            tool = new Saw();
            bot.AddToBackpack(tool);
            return tool;
        }

        private static FishingPole FindOrCreateFishingPole(PlayerBot bot)
        {
            var tool = bot.Backpack == null ? null : bot.Backpack.FindItemByType<FishingPole>();
            if (tool != null && !tool.Deleted) return tool;
            tool = new FishingPole();
            bot.AddToBackpack(tool);
            return tool;
        }

        private static ButcherKnife FindOrCreateButcherKnife(PlayerBot bot)
        {
            var tool = bot.Backpack == null ? null : bot.Backpack.FindItemByType<ButcherKnife>();
            if (tool != null && !tool.Deleted) return tool;
            tool = new ButcherKnife();
            bot.AddToBackpack(tool);
            return tool;
        }

        private static Skillet FindOrCreateSkillet(PlayerBot bot)
        {
            var tool = bot.Backpack == null ? null : bot.Backpack.FindItemByType<Skillet>();
            if (tool != null && !tool.Deleted) return tool;
            tool = new Skillet();
            bot.AddToBackpack(tool);
            return tool;
        }

        // Fishing.System owns range, water-tile validation, resource banks,
        // skill checks, catch selection, pack delivery, and pole wear. The
        // labor adapter only selects a nearby land-water tile and invokes the
        // normal targeting path a player uses after double-clicking a pole.
        private static void TryFish(PlayerBot bot)
        {
            if (bot == null || bot.Map == null || bot.Map == Map.Internal) return;
            LandTarget water = null;
            for (var radius = 1; radius <= 4 && water == null; radius++)
            {
                for (var x = bot.X - radius; x <= bot.X + radius && water == null; x++)
                for (var y = bot.Y - radius; y <= bot.Y + radius; y++)
                {
                    if (x < 0 || y < 0 || Math.Max(Math.Abs(x - bot.X), Math.Abs(y - bot.Y)) != radius) continue;
                    var tile = bot.Map.Tiles.GetLandTile(x, y).ID & TileData.MaxLandValue;
                    if (Fishing.System.Definition.Validate(tile)) water = new LandTarget(new Point3D(x, y, 0), bot.Map);
                }
            }
            if (water == null) return;

            var pole = FindOrCreateFishingPole(bot);
            if (Fishing.System.BeginHarvesting(bot, pole) && bot.Target != null)
                bot.Target.Invoke(bot, water);
        }

        // A cooker buys actual caught Fish from a nearby active fisher, then
        // keeps ServUO in charge of both carving and the DefCooking craft
        // recipe. A missing heat source or failed recipe leaves the real
        // input in the pack for the normal bank-return path.
        private static void TryBuyNearbyFish(PlayerBot cooker)
        {
            if (cooker == null || cooker.Backpack == null || cooker.Map == null) return;
            foreach (var fisher in PlayerBotService.FindBots())
            {
                if (fisher == null || fisher == cooker || fisher.Deleted || !fisher.Alive || fisher.Map != cooker.Map
                    || fisher.LaborKind != PlayerBotLaborKind.Fisher || !IsActive(fisher) || !fisher.InRange(cooker, 4)
                    || fisher.Backpack == null) continue;

                var fish = fisher.Backpack.FindItemByType<Fish>();
                var purse = cooker.Backpack.FindItemByType<Gold>();
                if (fish == null || fish.Deleted || fish.Amount <= 0 || purse == null || purse.Deleted || purse.Amount < fish.Amount
                    || !cooker.Backpack.CheckHold(cooker, fish, false, true)) continue;

                var price = fish.Amount;
                var payment = TakeFromPack(cooker.Backpack, purse, price);
                if (payment == null) return;
                fisher.Backpack.DropItem(payment);
                fisher.Backpack.RemoveItem(fish);
                cooker.Backpack.DropItem(fish);
                cooker.Say("I'll cook that catch.");
                fisher.Say("A fair price.");
                PlayerBotService.RecordEvent(cooker.Name + " bought " + price + " fish from " + fisher.Name + ".");
                return;
            }
        }

        private static void TryCarveFish(PlayerBot bot)
        {
            if (bot == null || bot.Backpack == null) return;
            var fish = bot.Backpack.FindItemByType<Fish>();
            if (fish != null && !fish.Deleted) fish.Carve(bot, FindOrCreateButcherKnife(bot));
        }

        private static void TryCookFish(PlayerBot bot)
        {
            if (bot == null || bot.Backpack == null) return;
            var raw = bot.Backpack.FindItemByType<RawFishSteak>();
            if (raw == null || raw.Deleted) return;
            var system = DefCooking.CraftSystem;
            var item = system.CraftItems.SearchFor(typeof(FishSteak));
            if (item != null) item.Craft(bot, system, typeof(RawFishSteak), FindOrCreateSkillet(bot));
        }

        // Keep the profile small and backed by the ordinary blacksmith vendor
        // list. A smith chooses only native three-, eight-, ten-, twelve-, eighteen-, or twenty-ingot
        // recipes from its actual iron stack. CraftItem retains all skill,
        // station, resource, and failure behavior.
        private static void TryCraftBlacksmithProduct(PlayerBot bot)
        {
            var ingots = bot == null || bot.Backpack == null ? null : bot.Backpack.FindItemByType<IronIngot>();
            var tool = FindOrCreateTongs(bot);
            var system = DefBlacksmithy.CraftSystem;
            var type = typeof(Dagger);
            if (ingots != null && !ingots.Deleted)
            {
                if (ingots.Amount >= 20)
                {
                    var choice = Utility.Random(7);
                    type = choice == 0 ? typeof(Broadsword) : choice == 1 ? typeof(Scimitar)
                        : choice == 2 ? typeof(PlateGorget) : choice == 3 ? typeof(PlateGloves)
                        : choice == 4 ? typeof(PlateArms) : choice == 5 ? typeof(PlateLegs) : typeof(Dagger);
                }
                else if (ingots.Amount >= 18)
                {
                    var choice = Utility.Random(6);
                    type = choice == 0 ? typeof(Broadsword) : choice == 1 ? typeof(Scimitar)
                        : choice == 2 ? typeof(PlateGorget) : choice == 3 ? typeof(PlateGloves)
                        : choice == 4 ? typeof(PlateArms) : typeof(Dagger);
                }
                else if (ingots.Amount >= 12)
                {
                    var choice = Utility.Random(5);
                    type = choice == 0 ? typeof(Broadsword) : choice == 1 ? typeof(Scimitar)
                        : choice == 2 ? typeof(PlateGorget) : choice == 3 ? typeof(PlateGloves) : typeof(Dagger);
                }
                else if (ingots.Amount >= 10)
                {
                    var choice = Utility.Random(4);
                    type = choice == 0 ? typeof(Broadsword) : choice == 1 ? typeof(Scimitar)
                        : choice == 2 ? typeof(PlateGorget) : typeof(Dagger);
                }
                else if (ingots.Amount >= 8 && Utility.RandomBool()) type = typeof(Cutlass);
            }
            var item = system.CraftItems.SearchFor(type);
            if (item != null) item.Craft(bot, system, typeof(IronIngot), tool);
        }

        private static void TryCraftWoodenShield(PlayerBot bot)
        {
            var tool = FindOrCreateSaw(bot);
            var system = DefCarpentry.CraftSystem;
            var item = system.CraftItems.SearchFor(typeof(WoodenShield));
            if (item != null) item.Craft(bot, system, typeof(Board), tool);
        }

        // Lumberjacks sell their actual logs to a nearby carpenter. The
        // carpenter uses BaseLog.Axe, ServUO's normal log-to-board path,
        // before the ordinary carpentry CraftItem pipeline consumes boards.
        private static void TryBuyNearbyLogs(PlayerBot carpenter)
        {
            if (carpenter == null || carpenter.Backpack == null || carpenter.Map == null) return;
            foreach (var lumberjack in PlayerBotService.FindBots())
            {
                if (lumberjack == null || lumberjack == carpenter || lumberjack.Deleted || !lumberjack.Alive
                    || lumberjack.Map != carpenter.Map || lumberjack.LaborKind != PlayerBotLaborKind.Lumberjack
                    || !IsActive(lumberjack) || !lumberjack.InRange(carpenter, 4) || lumberjack.Backpack == null) continue;

                var logs = lumberjack.Backpack.FindItemByType<BaseLog>();
                var purse = carpenter.Backpack.FindItemByType<Gold>();
                if (logs == null || logs.Deleted || logs.Amount <= 0 || purse == null || purse.Deleted || purse.Amount < logs.Amount
                    || !carpenter.Backpack.CheckHold(carpenter, logs, false, true)) continue;

                var price = logs.Amount;
                var payment = TakeFromPack(carpenter.Backpack, purse, price);
                if (payment == null) return;
                lumberjack.Backpack.DropItem(payment);

                lumberjack.Backpack.RemoveItem(logs);
                carpenter.Backpack.DropItem(logs);
                carpenter.Say("I'll take those logs.");
                lumberjack.Say("A fair price.");
                PlayerBotService.RecordEvent(carpenter.Name + " bought " + price + " logs from " + lumberjack.Name + ".");
                return;
            }
        }

        private static void TryMakeBoards(PlayerBot bot)
        {
            if (bot == null || bot.Backpack == null) return;
            var logs = bot.Backpack.FindItemByType<BaseLog>();
            if (logs != null && !logs.Deleted) logs.Axe(bot, FindOrCreateHatchet(bot));
        }

        // Smelting must remain the native BaseOre interaction. It keeps the
        // existing forge-range, mining-skill, ore-size, loss, and ingot rules
        // instead of exchanging an ore item for a hand-created ingot.
        private static void TrySmeltOre(PlayerBot bot)
        {
            if (bot == null || bot.Backpack == null || bot.Map == null) return;
            var ore = bot.Backpack.FindItemByType<BaseOre>();
            var forge = FindForge(bot);
            if (ore == null || ore.Deleted || forge == null) return;

            ore.OnDoubleClick(bot);
            if (bot.Target != null) bot.Target.Invoke(bot, forge);
        }

        // A short local labor contract is the first economy seam: a smith
        // pays a nearby active miner for ore the miner actually harvested.
        // Stack splitting uses ServUO's own lift helper, so the transaction
        // conserves both ore and gold instead of manufacturing either.
        private static void TryBuyNearbyOre(PlayerBot smith)
        {
            if (smith == null || smith.Backpack == null || smith.Map == null) return;
            foreach (var miner in PlayerBotService.FindBots())
            {
                if (miner == null || miner == smith || miner.Deleted || !miner.Alive || miner.Map != smith.Map
                    || miner.LaborKind != PlayerBotLaborKind.Miner || !IsActive(miner) || !miner.InRange(smith, 4)
                    || miner.Backpack == null) continue;

                var ore = miner.Backpack.FindItemByType<BaseOre>();
                var purse = smith.Backpack.FindItemByType<Gold>();
                if (ore == null || ore.Deleted || ore.Amount <= 0 || purse == null || purse.Deleted || purse.Amount < ore.Amount
                    || !smith.Backpack.CheckHold(smith, ore, false, true)) continue;

                var price = ore.Amount;
                var payment = TakeFromPack(smith.Backpack, purse, price);
                if (payment == null) return;
                miner.Backpack.DropItem(payment);

                miner.Backpack.RemoveItem(ore);
                smith.Backpack.DropItem(ore);
                smith.Say("I'll take that ore.");
                miner.Say("A fair price.");
                PlayerBotService.RecordEvent(smith.Name + " bought " + price + " ore from " + miner.Name + ".");
                return;
            }
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

        // A shift only returns goods when the already-audited route graph can
        // reach an imported bank destination. Arrival also requires a live
        // Banker, so a location label can never silently become a delivery.
        private static bool TryBeginReturn(PlayerBot bot)
        {
            if (!HasLaborGoods(bot) || bot.Map == null || bot.Map == Map.Internal) return false;
            var banks = PlayerBotWorldData.GetDestinations(bot.Map, "Bank");
            banks.Sort(delegate(PlayerBotDestination left, PlayerBotDestination right)
            {
                var leftDistance = Math.Max(Math.Abs(bot.X - left.X), Math.Abs(bot.Y - left.Y));
                var rightDistance = Math.Max(Math.Abs(bot.X - right.X), Math.Abs(bot.Y - right.Y));
                return leftDistance.CompareTo(rightDistance);
            });
            foreach (var bank in banks)
            {
                if (!PlayerBotWorldData.TryPlanRoute(bot, bank)) continue;
                bot.Destination = new Point3D(bank.X, bank.Y, bank.Z);
                bot.DestinationName = "Labor delivery: " + bank.Name;
                bot.LaborReturnName = bank.Name;
                bot.LaborReturning = true;
                PlayerBotService.RecordEvent(bot.Name + " is hauling labor goods to " + bank.Name + ".");
                return true;
            }
            return false;
        }

        private static void TickReturn(PlayerBot bot)
        {
            if (bot == null || bot.Deleted || !bot.Alive || bot.Map == null || bot.Map == Map.Internal)
            {
                ClearLabor(bot);
                return;
            }
            if (!HasLaborGoods(bot))
            {
                ClearLabor(bot);
                return;
            }
            if (bot.InRange(bot.Destination, 2))
            {
                if (FindBanker(bot) != null && PlayerBotShop.TrySellLaborGoods(bot))
                {
                    PlayerBotService.RecordEvent(bot.Name + " delivered a labor good to a bank hawker at " + bot.LaborReturnName + ".");
                    ClearLabor(bot);
                    return;
                }
                if (FindBanker(bot) != null && DepositLaborGoods(bot) > 0)
                {
                    PlayerBotService.RecordEvent(bot.Name + " banked a labor haul at " + bot.LaborReturnName + ".");
                    ClearLabor(bot);
                    return;
                }
                // A changed shard vendor layout is not a reason to deposit
                // into an arbitrary container. Keep the haul and return the
                // bot to ordinary travel instead.
                ClearLabor(bot);
                return;
            }
            PlayerBotService.TickTravelBehavior(bot);
        }

        private static Banker FindBanker(PlayerBot bot)
        {
            IPooledEnumerable nearby = bot.GetMobilesInRange(12);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var banker = mobile as Banker;
                    if (banker != null && !banker.Deleted && banker.Alive) return banker;
                }
            }
            finally { nearby.Free(); }
            return null;
        }

        private static bool HasLaborGoods(PlayerBot bot)
        {
            if (bot == null || bot.Backpack == null) return false;
            switch (bot.LaborKind)
            {
                case PlayerBotLaborKind.Miner: return bot.Backpack.FindItemByType<BaseOre>() != null;
                case PlayerBotLaborKind.Lumberjack: return bot.Backpack.FindItemByType<BaseLog>() != null;
                case PlayerBotLaborKind.Blacksmith: return bot.Backpack.FindItemByType<BaseIngot>() != null || HasBlacksmithGoods(bot.Backpack);
                case PlayerBotLaborKind.Carpenter: return bot.Backpack.FindItemByType<BaseWoodBoard>() != null || bot.Backpack.FindItemByType<WoodenShield>() != null;
                case PlayerBotLaborKind.Fisher: return bot.Backpack.FindItemByType<Fish>() != null;
                case PlayerBotLaborKind.Cooker: return bot.Backpack.FindItemByType<Fish>() != null || bot.Backpack.FindItemByType<RawFishSteak>() != null || bot.Backpack.FindItemByType<FishSteak>() != null;
                default: return false;
            }
        }

        private static int DepositLaborGoods(PlayerBot bot)
        {
            if (bot == null || bot.Backpack == null || bot.BankBox == null) return 0;
            var goods = new List<Item>();
            foreach (Item item in bot.Backpack.Items)
            {
                if ((bot.LaborKind == PlayerBotLaborKind.Miner && item is BaseOre)
                    || (bot.LaborKind == PlayerBotLaborKind.Lumberjack && item is BaseLog)
                    || (bot.LaborKind == PlayerBotLaborKind.Blacksmith && (item is BaseIngot || IsBlacksmithGood(item)))
                    || (bot.LaborKind == PlayerBotLaborKind.Carpenter && (item is BaseWoodBoard || item is WoodenShield))
                    || (bot.LaborKind == PlayerBotLaborKind.Fisher && item is Fish)
                    || (bot.LaborKind == PlayerBotLaborKind.Cooker && (item is Fish || item is RawFishSteak || item is FishSteak)))
                    goods.Add(item);
            }
            foreach (var item in goods)
            {
                bot.Backpack.RemoveItem(item);
                bot.BankBox.DropItem(item);
            }
            return goods.Count;
        }

        private static bool HasBlacksmithGoods(Container pack)
        {
            if (pack == null) return false;
            foreach (Item item in pack.Items)
            {
                if (IsBlacksmithGood(item)) return true;
            }
            return false;
        }

        private static bool IsBlacksmithGood(Item item)
        {
            return item is Dagger || item is Broadsword || item is Cutlass || item is Scimitar || item is PlateGorget || item is PlateGloves || item is PlateArms || item is PlateLegs;
        }

        private static void ClearLabor(PlayerBot bot)
        {
            if (bot == null) return;
            bot.LaborKind = PlayerBotLaborKind.None;
            bot.LaborUntil = DateTime.MinValue;
            bot.NextLaborAction = DateTime.MinValue;
            bot.LaborReturning = false;
            bot.LaborReturnName = "";
            AutonomousWorkers.Remove(bot.Serial.Value);
            PlayerBotService.ResumeTravelAfterLabor(bot);
        }

        private static Item FindForge(PlayerBot bot)
        {
            IPooledEnumerable nearby = bot.Map.GetItemsInRange(bot.Location, 2);
            try
            {
                foreach (Item item in nearby)
                {
                    if (item == null || item.Deleted) continue;
                    if (item.GetType().IsDefined(typeof(ForgeAttribute), false)
                        || item.ItemID == 4017 || (item.ItemID >= 6522 && item.ItemID <= 6569))
                        return item;
                }
            }
            finally { nearby.Free(); }
            return null;
        }
    }
}
