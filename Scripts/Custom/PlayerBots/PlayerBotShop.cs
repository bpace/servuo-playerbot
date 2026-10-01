using System;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;

namespace Server.CustomBots
{
    public sealed class PlayerBotShopStockMarker : Item
    {
        public Item Stock { get; private set; }

        public PlayerBotShopStockMarker(Item stock) : base(1)
        {
            Stock = stock;
            Visible = false;
            Movable = false;
        }

        public PlayerBotShopStockMarker(Serial serial) : base(serial) { }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
            writer.Write(Stock);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
            Stock = reader.ReadItem();
        }
    }

    // Hawker stock is always a real item in the hawker's backpack. Labor
    // goods arrive through a completed labor route; ordinary fallback stock
    // remains separate so a delivery never destroys an existing item.
    public static class PlayerBotShop
    {
        public static bool TryOpenShop(PlayerBot bot, Mobile buyer, string speech)
        {
            if (bot == null || buyer == null || buyer.Deleted || !buyer.Alive || !buyer.Player
                || bot.Deleted || !bot.Alive || bot.Map != buyer.Map || !buyer.InRange(bot, 4)
                || bot.BankRole != PlayerBotBankRole.Hawker || String.IsNullOrEmpty(speech)
                || speech.IndexOf("buy", StringComparison.OrdinalIgnoreCase) < 0) return false;

            var stock = EnsureHawkerStock(bot);
            if (stock == null) return false;
            buyer.CloseGump(typeof(PlayerBotShopGump));
            buyer.SendGump(new PlayerBotShopGump(bot, stock));
            return true;
        }

        public static Item EnsureHawkerStock(PlayerBot bot)
        {
            if (bot == null || bot.Backpack == null || bot.Deleted) return null;
            Item fallback = null;
            foreach (Item item in bot.Backpack.Items)
            {
                var marker = item as PlayerBotShopStockMarker;
                if (marker != null && marker.Stock != null && !marker.Stock.Deleted
                    && marker.Stock.Parent == bot.Backpack)
                {
                    if (IsRetailLaborGood(marker.Stock)) return marker.Stock;
                    if (!IsTradeGood(marker.Stock) && fallback == null) fallback = marker.Stock;
                }
            }
            if (fallback != null) return fallback;

            Item stock;
            switch (Utility.Random(4))
            {
                case 0: stock = new Bandage(Utility.RandomMinMax(40, 120)); break;
                case 1: stock = new Garlic(Utility.RandomMinMax(30, 90)); break;
                case 2: stock = new MandrakeRoot(Utility.RandomMinMax(30, 90)); break;
                default: stock = new Arrow(Utility.RandomMinMax(80, 180)); break;
            }
            bot.Backpack.DropItem(stock);
            bot.Backpack.DropItem(new PlayerBotShopStockMarker(stock));
            return stock;
        }

        internal static bool HasWorkshopOre(PlayerBot hawker)
        {
            PlayerBotShopStockMarker marker;
            return FindWorkshopOre(hawker, out marker) != null;
        }

        internal static bool HasWorkshopLogs(PlayerBot hawker)
        {
            PlayerBotShopStockMarker marker;
            return FindWorkshopLogs(hawker, out marker) != null;
        }

        internal static bool HasWorkshopFish(PlayerBot hawker)
        {
            PlayerBotShopStockMarker marker;
            return FindWorkshopFish(hawker, out marker) != null;
        }

        // The transfer is deliberately local. The harvester was already paid
        // when its real stack reached this hawker; the smith only takes that
        // marked stock when both bots stand together at an authored workshop.
        internal static bool TryWithdrawWorkshopOre(PlayerBot smith)
        {
            if (smith == null || smith.Deleted || !smith.Alive || smith.Backpack == null || smith.Map == null) return false;
            IPooledEnumerable nearby = smith.GetMobilesInRange(4);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var hawker = mobile as PlayerBot;
                    if (hawker == null || hawker == smith || hawker.Deleted || !hawker.Alive
                        || hawker.BankRole != PlayerBotBankRole.Hawker || hawker.Backpack == null) continue;

                    PlayerBotShopStockMarker marker;
                    var ore = FindWorkshopOre(hawker, out marker);
                    if (ore == null || marker == null || !smith.Backpack.CheckHold(smith, ore, false, true)) continue;

                    hawker.Backpack.RemoveItem(ore);
                    smith.Backpack.DropItem(ore);
                    marker.Delete();
                    smith.Say("I'll put this ore to work.");
                    hawker.Say("The workshop stock is yours.");
                    PlayerBotService.RecordEvent(smith.Name + " withdrew " + ore.Amount + " ore from " + hawker.Name + " for smithing.");
                    return true;
                }
            }
            finally { nearby.Free(); }
            return false;
        }

        internal static bool TryWithdrawWorkshopLogs(PlayerBot carpenter)
        {
            if (carpenter == null || carpenter.Deleted || !carpenter.Alive || carpenter.Backpack == null || carpenter.Map == null) return false;
            IPooledEnumerable nearby = carpenter.GetMobilesInRange(4);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var hawker = mobile as PlayerBot;
                    if (hawker == null || hawker == carpenter || hawker.Deleted || !hawker.Alive
                        || hawker.BankRole != PlayerBotBankRole.Hawker || hawker.Backpack == null) continue;

                    PlayerBotShopStockMarker marker;
                    var logs = FindWorkshopLogs(hawker, out marker);
                    if (logs == null || marker == null || !carpenter.Backpack.CheckHold(carpenter, logs, false, true)) continue;

                    hawker.Backpack.RemoveItem(logs);
                    carpenter.Backpack.DropItem(logs);
                    marker.Delete();
                    carpenter.Say("I'll make something useful from these.");
                    hawker.Say("The workshop stock is yours.");
                    PlayerBotService.RecordEvent(carpenter.Name + " withdrew " + logs.Amount + " logs from " + hawker.Name + " for carpentry.");
                    return true;
                }
            }
            finally { nearby.Free(); }
            return false;
        }

        // A cook only takes actual caught Fish already delivered to the local
        // hawker. Raw steaks and cooked food stay player-facing retail stock.
        internal static bool TryWithdrawWorkshopFish(PlayerBot cooker)
        {
            if (cooker == null || cooker.Deleted || !cooker.Alive || cooker.Backpack == null || cooker.Map == null) return false;
            IPooledEnumerable nearby = cooker.GetMobilesInRange(4);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var hawker = mobile as PlayerBot;
                    if (hawker == null || hawker == cooker || hawker.Deleted || !hawker.Alive
                        || hawker.BankRole != PlayerBotBankRole.Hawker || hawker.Backpack == null) continue;

                    PlayerBotShopStockMarker marker;
                    var fish = FindWorkshopFish(hawker, out marker);
                    if (fish == null || marker == null || !cooker.Backpack.CheckHold(cooker, fish, false, true)) continue;

                    hawker.Backpack.RemoveItem(fish);
                    cooker.Backpack.DropItem(fish);
                    marker.Delete();
                    cooker.Say("I'll cook this catch.");
                    hawker.Say("The kitchen stock is yours.");
                    PlayerBotService.RecordEvent(cooker.Name + " withdrew " + fish.Amount + " fish from " + hawker.Name + " for cooking.");
                    return true;
                }
            }
            finally { nearby.Free(); }
            return false;
        }

        // ServUO's standard vendor lists sell daggers for 21 gold, wooden
        // shields for 30, fish for 6, and raw fish steaks for 3. Their
        // corresponding buyback values are 10, 15, 1, and 1. A hawker pays
        // that existing buyback value for a laborer's real good and advertises
        // the same item at the standard sale value.
        public static bool TrySellLaborGoods(PlayerBot worker)
        {
            if (worker == null || worker.Deleted || !worker.Alive || worker.Backpack == null || worker.Map == null) return false;
            var goods = FindLaborGoods(worker);
            if (goods == null || goods.Deleted || goods.Amount <= 0) return false;
            var paymentAmount = WholesalePrice(goods);
            if (paymentAmount <= 0) return false;

            IPooledEnumerable nearby = worker.GetMobilesInRange(12);
            try
            {
                foreach (Mobile mobile in nearby)
                {
                    var hawker = mobile as PlayerBot;
                    if (hawker == null || hawker == worker || hawker.Deleted || !hawker.Alive
                        || hawker.BankRole != PlayerBotBankRole.Hawker || hawker.Backpack == null
                        || !hawker.Backpack.CheckHold(hawker, goods, false, true)) continue;

                    var purse = hawker.Backpack.FindItemByType<Gold>();
                    if (purse == null || purse.Deleted || purse.Amount < paymentAmount
                        || !worker.Backpack.CheckHold(worker, purse, false, true)) continue;

                    var payment = TakeGold(hawker.Backpack, purse, paymentAmount);
                    if (payment == null) return false;
                    worker.Backpack.RemoveItem(goods);
                    hawker.Backpack.DropItem(goods);
                    worker.Backpack.DropItem(payment);
                    hawker.Backpack.DropItem(new PlayerBotShopStockMarker(goods));
                    worker.Say(IsRetailLaborGood(goods) ? "Sold my finished work." : "Delivered my harvest.");
                    hawker.Say(IsRetailLaborGood(goods) ? "I'll put it up for sale." : "I'll hold it for the workshop.");
                    PlayerBotService.RecordEvent(worker.Name + " sold " + goods.Amount + " " + StockName(goods) + " to " + hawker.Name + ".");
                    return true;
                }
            }
            finally { nearby.Free(); }
            return false;
        }

        public static string WtsLine(PlayerBot bot)
        {
            var stock = EnsureHawkerStock(bot);
            return stock == null ? "" : "WTS " + stock.Amount + " " + StockName(stock) + " for " + Price(stock) + " gold.";
        }

        internal static int Price(Item stock)
        {
            if (stock is Bandage) return stock.Amount * 2;
            if (stock is Garlic || stock is MandrakeRoot) return stock.Amount * 3;
            if (stock is Arrow) return stock.Amount;
            if (stock is Dagger) return stock.Amount * 21;
            if (stock is WoodenShield) return stock.Amount * 30;
            if (stock is Fish) return stock.Amount * 6;
            if (stock is RawFishSteak) return stock.Amount * 3;
            return 0;
        }

        internal static string StockName(Item stock)
        {
            if (!String.IsNullOrEmpty(stock.Name)) return stock.Name;
            if (stock is Bandage) return "bandages";
            if (stock is Garlic) return "garlic";
            if (stock is MandrakeRoot) return "mandrake root";
            if (stock is Arrow) return "arrows";
            if (stock is Dagger) return "daggers";
            if (stock is WoodenShield) return "wooden shields";
            if (stock is Fish) return "fish";
            if (stock is RawFishSteak) return "raw fish steaks";
            return "goods";
        }

        // Ore and logs use the existing one-gold local labor rate from the
        // miner/smith and lumberjack/carpenter exchanges. A hawker holds them
        // as private workshop stock, never player-facing store stock, because
        // ServUO's vendor lists do not provide a matching raw-ore retail value.
        private static bool IsTradeGood(Item item)
        {
            return item is BaseOre || item is BaseLog || IsRetailLaborGood(item);
        }

        private static BaseOre FindWorkshopOre(PlayerBot hawker, out PlayerBotShopStockMarker result)
        {
            result = null;
            if (hawker == null || hawker.Backpack == null) return null;
            foreach (Item item in hawker.Backpack.Items)
            {
                var marker = item as PlayerBotShopStockMarker;
                var ore = marker == null ? null : marker.Stock as BaseOre;
                if (ore != null && !ore.Deleted && ore.Parent == hawker.Backpack)
                {
                    result = marker;
                    return ore;
                }
            }
            return null;
        }

        private static BaseLog FindWorkshopLogs(PlayerBot hawker, out PlayerBotShopStockMarker result)
        {
            result = null;
            if (hawker == null || hawker.Backpack == null) return null;
            foreach (Item item in hawker.Backpack.Items)
            {
                var marker = item as PlayerBotShopStockMarker;
                var logs = marker == null ? null : marker.Stock as BaseLog;
                if (logs != null && !logs.Deleted && logs.Parent == hawker.Backpack)
                {
                    result = marker;
                    return logs;
                }
            }
            return null;
        }

        private static Fish FindWorkshopFish(PlayerBot hawker, out PlayerBotShopStockMarker result)
        {
            result = null;
            if (hawker == null || hawker.Backpack == null) return null;
            foreach (Item item in hawker.Backpack.Items)
            {
                var marker = item as PlayerBotShopStockMarker;
                var fish = marker == null ? null : marker.Stock as Fish;
                if (fish != null && !fish.Deleted && fish.Parent == hawker.Backpack)
                {
                    result = marker;
                    return fish;
                }
            }
            return null;
        }

        private static bool IsRetailLaborGood(Item item)
        {
            return item is Dagger || item is WoodenShield || item is Fish || item is RawFishSteak;
        }

        private static Item FindLaborGoods(PlayerBot worker)
        {
            if (worker.Backpack == null) return null;
            foreach (Item item in worker.Backpack.Items)
            {
                if (IsDeliveredTradeGood(worker.LaborKind, item)) return item;
            }
            return null;
        }

        private static bool IsDeliveredTradeGood(PlayerBotLaborKind laborKind, Item item)
        {
            if (laborKind == PlayerBotLaborKind.Miner) return item is BaseOre;
            if (laborKind == PlayerBotLaborKind.Lumberjack) return item is BaseLog;
            if (laborKind == PlayerBotLaborKind.Fisher) return item is Fish;
            if (laborKind == PlayerBotLaborKind.Cooker) return item is Fish || item is RawFishSteak;
            if (laborKind == PlayerBotLaborKind.Blacksmith) return item is Dagger;
            if (laborKind == PlayerBotLaborKind.Carpenter) return item is WoodenShield;
            return false;
        }

        private static int WholesalePrice(Item goods)
        {
            if (goods is Dagger) return goods.Amount * 10;
            if (goods is WoodenShield) return goods.Amount * 15;
            if (goods is Fish || goods is RawFishSteak) return goods.Amount;
            if (goods is BaseOre || goods is BaseLog) return goods.Amount;
            return 0;
        }

        private static Item TakeGold(Container pack, Gold gold, int amount)
        {
            if (pack == null || gold == null || gold.Deleted || amount <= 0 || amount > gold.Amount) return null;
            if (amount == gold.Amount)
            {
                pack.RemoveItem(gold);
                return gold;
            }
            var payment = Mobile.LiftItemDupe(gold, amount);
            if (payment != null) pack.RemoveItem(payment);
            return payment;
        }

        internal static bool TryPurchase(PlayerBot seller, Mobile buyer, Item stock)
        {
            var price = Price(stock);
            if (seller == null || buyer == null || stock == null || price <= 0 || seller.Deleted || buyer.Deleted
                || !seller.Alive || !buyer.Alive || !buyer.Player || seller.Map != buyer.Map || !buyer.InRange(seller, 4)
                || seller.BankRole != PlayerBotBankRole.Hawker || seller.Backpack == null || stock.Deleted
                || stock.Parent != seller.Backpack || buyer.Backpack == null || !buyer.Backpack.CheckHold(buyer, stock, false, true)) return false;

            if (!Banker.Withdraw(buyer, price, false)) return false;
            try
            {
                seller.Backpack.RemoveItem(stock);
                buyer.Backpack.DropItem(stock);
                foreach (Item item in seller.Backpack.Items)
                {
                    var marker = item as PlayerBotShopStockMarker;
                    if (marker != null && marker.Stock == stock) marker.Delete();
                }
                seller.Backpack.DropItem(new Gold(price));
                seller.Say("A fair trade.");
                buyer.SendMessage("You bought {0} {1} for {2} gold.", stock.Amount, StockName(stock), price);
                return true;
            }
            catch
            {
                if (!stock.Deleted && stock.Parent != seller.Backpack) seller.Backpack.DropItem(stock);
                Banker.Deposit(buyer, price, false);
                return false;
            }
        }
    }

    public sealed class PlayerBotShopGump : Gump
    {
        private readonly PlayerBot _seller;
        private readonly Item _stock;

        public PlayerBotShopGump(PlayerBot seller, Item stock) : base(100, 100)
        {
            _seller = seller;
            _stock = stock;
            var price = PlayerBotShop.Price(stock);
            AddBackground(0, 0, 300, 150, 0x13BE);
            AddLabel(20, 18, 0x34, seller.Name + "'s goods");
            AddLabel(20, 52, 0x480, stock.Amount + " " + PlayerBotShop.StockName(stock));
            AddLabel(20, 76, 0x480, price + " gold from bank/account");
            AddButton(25, 108, 0xF7, 0xF8, 1, GumpButtonType.Reply, 0);
            AddLabel(60, 108, 0x34, "Buy");
            AddButton(170, 108, 0xF2, 0xF1, 0, GumpButtonType.Reply, 0);
            AddLabel(205, 108, 0x34, "Cancel");
        }

        public override void OnResponse(NetState sender, RelayInfo info)
        {
            if (info.ButtonID != 1) return;
            if (!PlayerBotShop.TryPurchase(_seller, sender.Mobile, _stock))
                sender.Mobile.SendMessage("That trade cannot be completed.");
        }
    }
}
