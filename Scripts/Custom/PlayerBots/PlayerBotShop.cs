using Server.Items;

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
            return stock == null ? "" : "WTS " + stock.Amount + " " + stock.Name + ".";
        }
    }
}
