using System.Collections.Generic;
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

        // World saves can preserve a creature after its owner reference was
        // removed or manually edited. Keep only the dedicated animal that a
        // live PlayerBot explicitly still owns; no ordinary player pet is in
        // scope because the types are private to this system.
        internal static int SweepStrays()
        {
            var strays = new List<BaseCreature>();
            foreach (Mobile mobile in World.Mobiles.Values)
            {
                var animal = mobile as BaseCreature;
                if (!(animal is PlayerBotPackHorse) && !(animal is PlayerBotPackLlama)) continue;
                var bot = animal.ControlMaster as PlayerBot;
                if (bot == null || bot.Deleted || bot.PackAnimal != animal) strays.Add(animal);
            }

            foreach (BaseCreature animal in strays) animal.Delete();
            return strays.Count;
        }
    }
}
