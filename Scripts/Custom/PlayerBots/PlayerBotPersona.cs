using System;
using Server.Items;
using Server.Mobiles;

namespace Server.CustomBots
{
    // Keeps presentation separate from movement and combat. A bot only needs
    // a role; this module owns its identity, outfit, and mount.
    internal static class PlayerBotPersonas
    {
        private static readonly string[] MaleNames =
        {
            "Alden", "Aric", "Bram", "Corwin", "Darian", "Gareth", "Ivor", "Loric",
            "Nolan", "Orin", "Quinn", "Silas", "Ulric", "Vaughn", "Wystan", "Yorick"
        };

        private static readonly string[] FemaleNames =
        {
            "Brenna", "Celia", "Elise", "Fiona", "Hilda", "Jessa", "Kara", "Mira",
            "Petra", "Rhea", "Tessa", "Vera", "Wynne", "Ysabel", "Aveline", "Maris"
        };

        private static readonly string[] Surnames =
        {
            "Ashford", "Blackwater", "Briar", "Crowe", "Dale", "Ember", "Fairwind", "Grove",
            "Hawke", "Ironwood", "Keel", "Lark", "Marlowe", "North", "Reed", "Vale"
        };

        private static readonly int[] CivilianHues = { 0x455, 0x47E, 0x489, 0x4A5, 0x58B, 0x59A, 0x5A4 };
        private static readonly int[] TravelHues = { 0x48F, 0x4AC, 0x59B, 0x5A8, 0x6D3 };
        private static readonly int[] DarkHues = { 0x455, 0x497, 0x4E9, 0x59B };

        public static void ApplyNew(PlayerBot bot)
        {
            if (bot == null || bot.Deleted)
                return;

            bot.Female = Utility.RandomBool();
            bot.Body = bot.Female ? 0x191 : 0x190;
            bot.Hue = Utility.RandomSkinHue();
            bot.SpeechHue = Utility.RandomMinMax(0x3B2, 0x59);
            bot.Name = NextName(bot.Female);
            Equip(bot);
        }

        // Existing v1 bots were created naked. This is idempotent so a
        // restart upgrades them without replacing GM-added equipment later.
        public static void EnsureAppearance(PlayerBot bot)
        {
            if (bot == null || bot.Deleted)
                return;

            RepairLegLayer(bot);
            if (HasOutfit(bot))
                return;

            if (HasLegacyNumberedName(bot.Name))
                bot.Name = NextName(bot.Female);

            Equip(bot);
        }

        private static void Equip(PlayerBot bot)
        {
            switch (bot.BotRole)
            {
                case PlayerBotRole.Banker:
                    EquipBanker(bot);
                    break;
                case PlayerBotRole.Adventurer:
                    EquipAdventurer(bot);
                    break;
                case PlayerBotRole.Townie:
                    EquipTownie(bot);
                    break;
                case PlayerBotRole.PlayerKiller:
                    EquipPlayerKiller(bot);
                    break;
                default:
                    EquipTraveler(bot);
                    break;
            }
        }

        private static void EquipTraveler(PlayerBot bot)
        {
            var hue = RandomHue(TravelHues);
            WearIfEmpty(bot, Layer.Shirt, new Shirt(hue));
            WearIfEmpty(bot, Layer.Pants, new LongPants(RandomHue(CivilianHues)));
            WearIfEmpty(bot, Layer.Shoes, new Boots());
            WearIfEmpty(bot, Layer.Cloak, new Cloak(hue));
            EquipWeapon(bot, new Longsword());
            MaybeMount(bot);
        }

        private static void EquipBanker(PlayerBot bot)
        {
            WearIfEmpty(bot, Layer.Shirt, new Shirt(RandomHue(CivilianHues)));
            WearIfEmpty(bot, Layer.Pants, new LongPants(RandomHue(CivilianHues)));
            WearIfEmpty(bot, Layer.Shoes, new Shoes());
            WearIfEmpty(bot, Layer.OuterTorso, new Robe(RandomHue(CivilianHues)));
        }

        private static void EquipTownie(PlayerBot bot)
        {
            var hue = RandomHue(CivilianHues);
            WearIfEmpty(bot, Layer.Shirt, new Shirt(hue));
            WearIfEmpty(bot, Layer.Pants, new LongPants(RandomHue(CivilianHues)));
            WearIfEmpty(bot, Layer.Shoes, new Shoes());
            if (Utility.RandomBool())
                WearIfEmpty(bot, Layer.Cloak, new Cloak(RandomHue(CivilianHues)));
        }

        private static void EquipAdventurer(PlayerBot bot)
        {
            var hue = RandomHue(TravelHues);
            WearIfEmpty(bot, Layer.Shirt, new Shirt(hue));
            WearIfEmpty(bot, Layer.Shoes, new Boots());
            WearIfEmpty(bot, Layer.InnerTorso, new LeatherChest());
            WearIfEmpty(bot, Layer.Pants, new LeatherLegs());
            WearIfEmpty(bot, Layer.Cloak, new Cloak(hue));
            EquipWeapon(bot, Utility.RandomBool() ? (Item)new Katana() : new Longsword());
            MaybeMount(bot);
        }

        private static void EquipPlayerKiller(PlayerBot bot)
        {
            var hue = RandomHue(DarkHues);
            WearIfEmpty(bot, Layer.Shirt, new Shirt(hue));
            WearIfEmpty(bot, Layer.Shoes, new Boots());
            WearIfEmpty(bot, Layer.InnerTorso, new LeatherChest());
            WearIfEmpty(bot, Layer.Pants, new LeatherLegs());
            WearIfEmpty(bot, Layer.Cloak, new Cloak(0x455));
            EquipWeapon(bot, new Katana());
        }

        private static void EquipWeapon(PlayerBot bot, Item weapon)
        {
            WearIfEmpty(bot, Layer.OneHanded, weapon);
        }

        private static void MaybeMount(PlayerBot bot)
        {
            if (!bot.Mounted && Utility.Random(5) == 0)
                new Horse().Rider = bot;
        }

        private static void WearIfEmpty(PlayerBot bot, Layer layer, Item item)
        {
            if (bot.FindItemOnLayer(layer) != null)
            {
                item.Delete();
                return;
            }

            item.LootType = LootType.Blessed;
            bot.AddItem(item);
        }

        private static bool HasOutfit(PlayerBot bot)
        {
            return bot.FindItemOnLayer(Layer.Shirt) != null
                || bot.FindItemOnLayer(Layer.Pants) != null
                || bot.FindItemOnLayer(Layer.InnerTorso) != null
                || bot.FindItemOnLayer(Layer.OuterTorso) != null;
        }

        private static void RepairLegLayer(PlayerBot bot)
        {
            if (bot.BotRole != PlayerBotRole.Adventurer && bot.BotRole != PlayerBotRole.PlayerKiller)
                return;

            var hasLeatherLegs = false;
            foreach (Item item in bot.Items)
            {
                if (item is LeatherLegs)
                {
                    hasLeatherLegs = true;
                    break;
                }
            }

            if (!hasLeatherLegs)
                return;

            Item longPants = null;
            foreach (Item item in bot.Items)
            {
                if (item is LongPants)
                {
                    longPants = item;
                    break;
                }
            }

            if (longPants != null)
                longPants.Delete();
        }

        private static string NextName(bool female)
        {
            var names = female ? FemaleNames : MaleNames;
            return names[Utility.Random(names.Length)] + " " + Surnames[Utility.Random(Surnames.Length)];
        }

        private static int RandomHue(int[] hues)
        {
            return hues[Utility.Random(hues.Length)];
        }

        private static bool HasLegacyNumberedName(string name)
        {
            if (String.IsNullOrEmpty(name))
                return true;

            var space = name.LastIndexOf(' ');
            if (space < 1 || space == name.Length - 1)
                return false;

            for (var i = space + 1; i < name.Length; i++)
            {
                if (!Char.IsDigit(name[i]))
                    return false;
            }

            return true;
        }
    }
}
