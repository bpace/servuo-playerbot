using System;
using Server.Engines.Craft;
using Server.Engines.Harvest;
using Server.Items;
using Server.Mobiles;

namespace Server.CustomBots
{
    // Manual labor actors are opt-in. They exercise ServUO's real harvest and
    // craft pipelines, so their resources, tool wear, skill checks, and
    // finished goods stay subject to the shard's own rules.
    public static class PlayerBotLabor
    {
        private static readonly TimeSpan ShiftLength = TimeSpan.FromMinutes(10);

        public static bool IsActive(PlayerBot bot)
        {
            return bot != null && bot.LaborKind != PlayerBotLaborKind.None && DateTime.UtcNow < bot.LaborUntil;
        }

        public static string StartNear(Mobile gm, string requestedKind)
        {
            if (gm == null || gm.Map == null || gm.Map == Map.Internal) return "Stand in the world before starting a labor bot.";
            PlayerBotLaborKind kind;
            if (!TryParse(requestedKind, out kind)) return "Usage: [PlayerBots labor miner|lumberjack|blacksmith]";

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
            if (!IsActive(bot))
            {
                if (bot != null && bot.LaborKind != PlayerBotLaborKind.None)
                {
                    bot.LaborKind = PlayerBotLaborKind.None;
                    bot.LaborUntil = DateTime.MinValue;
                    bot.NextLaborAction = DateTime.MinValue;
                    PlayerBotService.RecordEvent(bot.Name + " finished a labor shift.");
                }
                return;
            }
            if (bot.Map == null || bot.Map == Map.Internal || !bot.Alive) return;
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
                    TryCraftDagger(bot);
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

        private static void TryCraftDagger(PlayerBot bot)
        {
            var tool = FindOrCreateTongs(bot);
            var system = DefBlacksmithy.CraftSystem;
            var item = system.CraftItems.SearchFor(typeof(Dagger));
            if (item != null) item.Craft(bot, system, typeof(IronIngot), tool);
        }
    }
}
