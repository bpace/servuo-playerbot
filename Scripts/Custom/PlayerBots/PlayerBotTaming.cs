using System;
using System.Collections.Generic;
using Server.Mobiles;

namespace Server.CustomBots
{
    // A narrow ServUO-native counterpart to UO Offline's TamerBehavior. It
    // only selects a wild, eligible creature then lets AnimalTaming own the
    // actual chance, follower limit, targeting, and control-master result.
    public static class PlayerBotTaming
    {
        private const int SearchRange = 12;
        private const int TameRange = 3;
        private static readonly Dictionary<int, BaseCreature> PendingTargets = new Dictionary<int, BaseCreature>();

        internal static bool IsActive(PlayerBot bot)
        {
            return bot != null && !bot.Deleted && bot.Alive && bot.BotRole == PlayerBotRole.Tamer
                && bot.LaborKind == PlayerBotLaborKind.None && !bot.LaborReturning
                && bot.Combatant == null && bot.Map != null && bot.Map != Map.Internal;
        }

        internal static void Tick(PlayerBot bot)
        {
            BaseCreature quarry;
            if (PendingTargets.TryGetValue(bot.Serial.Value, out quarry))
            {
                FinishAttempt(bot, quarry);
                return;
            }

            quarry = FindQuarry(bot);
            if (quarry == null)
            {
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 24));
                return;
            }

            if (!bot.InRange(quarry, TameRange))
            {
                var direction = bot.GetDirectionTo(quarry) | Direction.Running;
                bot.Move(direction);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromMilliseconds(Utility.RandomMinMax(450, 850));
                return;
            }

            if (bot.UseSkill(SkillName.AnimalTaming))
            {
                PendingTargets[bot.Serial.Value] = quarry;
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            }
            else bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        }

        internal static void ReleasePets(PlayerBot bot)
        {
            if (bot == null) return;
            PendingTargets.Remove(bot.Serial.Value);
            var pets = new List<BaseCreature>();
            foreach (Mobile mobile in World.Mobiles.Values)
            {
                var pet = mobile as BaseCreature;
                if (pet != null && !pet.Deleted && pet.ControlMaster == bot) pets.Add(pet);
            }
            foreach (BaseCreature pet in pets) pet.Delete();
        }

        private static BaseCreature FindQuarry(PlayerBot bot)
        {
            foreach (Mobile mobile in bot.GetMobilesInRange(SearchRange))
            {
                var quarry = mobile as BaseCreature;
                if (quarry == null || quarry.Deleted || !quarry.Alive || quarry.Map != bot.Map
                    || !quarry.Tamable || quarry.Controlled || quarry.Summoned || quarry.IsDeadPet
                    || quarry.Combatant != null || quarry.CurrentTameSkill > bot.Skills[SkillName.AnimalTaming].Value)
                    continue;

                return quarry;
            }

            return null;
        }

        private static void FinishAttempt(PlayerBot bot, BaseCreature quarry)
        {
            if (quarry == null || quarry.Deleted || !quarry.Alive || quarry.Map != bot.Map
                || !bot.InRange(quarry, TameRange) || bot.Target == null)
            {
                PendingTargets.Remove(bot.Serial.Value);
                bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                return;
            }

            bot.Target.Invoke(bot, quarry);
            PendingTargets.Remove(bot.Serial.Value);
            // AnimalTaming's native timer now owns the attempt. Do not issue
            // another target while it resolves its normal messages and roll.
            bot.NextAction = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        }
    }
}
