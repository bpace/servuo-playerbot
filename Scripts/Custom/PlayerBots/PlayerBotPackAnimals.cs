using Server.Mobiles;

namespace Server.CustomBots
{
    // Dedicated classes make it possible to clean bot-owned working animals
    // without ever touching a player's ordinary pack animal.
    public sealed class PlayerBotPackHorse : PackHorse
    {
        [Constructable]
        public PlayerBotPackHorse()
        {
        }

        public PlayerBotPackHorse(Serial serial) : base(serial)
        {
        }

        public override void OnThink()
        {
            base.OnThink();
            if (ControlMaster == null || ControlMaster.Deleted || ControlMaster.Map == Map.Internal) Delete();
        }
    }

    public sealed class PlayerBotPackLlama : PackLlama
    {
        [Constructable]
        public PlayerBotPackLlama()
        {
        }

        public PlayerBotPackLlama(Serial serial) : base(serial)
        {
        }

        public override void OnThink()
        {
            base.OnThink();
            if (ControlMaster == null || ControlMaster.Deleted || ControlMaster.Map == Map.Internal) Delete();
        }
    }

    public static class PlayerBotPackAnimals
    {
        internal static BaseCreature SpawnFor(PlayerBot bot, bool llama)
        {
            if (bot == null || bot.Deleted || !bot.Alive || bot.Map == null || bot.Map == Map.Internal) return null;
            if (bot.PackAnimal != null && !bot.PackAnimal.Deleted && bot.PackAnimal.ControlMaster == bot)
                return bot.PackAnimal;

            var animal = llama ? (BaseCreature)new PlayerBotPackLlama() : new PlayerBotPackHorse();
            animal.Name = llama ? "a miner's pack llama" : "a lumberjack's pack horse";
            animal.MoveToWorld(new Point3D(bot.X + 1, bot.Y + 1, bot.Z), bot.Map);
            if (!animal.SetControlMaster(bot))
            {
                animal.Delete();
                return null;
            }
            animal.ControlTarget = bot;
            animal.ControlOrder = OrderType.Follow;
            bot.PackAnimal = animal;
            return animal;
        }

        internal static void Release(PlayerBot bot)
        {
            if (bot == null) return;
            var animal = bot.PackAnimal;
            bot.PackAnimal = null;
            if (animal != null && !animal.Deleted) animal.Delete();
        }
    }
}
