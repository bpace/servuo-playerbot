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

    // Real passive stock for a Hawker. This is deliberately not a trade
    // system yet: an advertised item must exist, but no buyer can settle
    // until native item/gold transfer is implemented and verified.
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
            foreach (Item item in bot.Backpack.Items)
            {
                var marker = item as PlayerBotShopStockMarker;
                if (marker != null && marker.Stock != null && !marker.Stock.Deleted
                    && marker.Stock.Parent == bot.Backpack) return marker.Stock;
            }

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
            return 0;
        }

        internal static string StockName(Item stock)
        {
            if (!String.IsNullOrEmpty(stock.Name)) return stock.Name;
            if (stock is Bandage) return "bandages";
            if (stock is Garlic) return "garlic";
            if (stock is MandrakeRoot) return "mandrake root";
            if (stock is Arrow) return "arrows";
            return "goods";
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
