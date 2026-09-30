using System;
using System.Collections.Generic;

namespace Server.CustomBots
{
    // A behavior owns one complete actor mode. It must either consume the
    // current tick or decline it; it must never leave a bot half-claimed for
    // a later behavior to mutate in the same tick.
    public abstract class PlayerBotBehavior
    {
        public abstract int Priority { get; }

        public abstract bool Handles(PlayerBot bot);

        public abstract void Tick(PlayerBot bot);
    }

    // The extension seam for optional PlayerBot packages. The base mod owns
    // core safety and movement behavior. An add-on can register a higher-
    // priority behavior during its Initialize method without editing the
    // PlayerBot service or replacing its timer.
    public static class PlayerBotBehaviorRegistry
    {
        private static readonly List<PlayerBotBehavior> Behaviors = new List<PlayerBotBehavior>();
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            Register(new DeadPlayerBotBehavior());
            Register(new CorpseRecoveryPlayerBotBehavior());
            Register(new BankSitterPlayerBotBehavior());
            Register(new DestinationVisitorPlayerBotBehavior());
            Register(new PartyPlayerBotBehavior());
            Register(new CombatPlayerBotBehavior());
            Register(new LaborPlayerBotBehavior());
            Register(new TravelPlayerBotBehavior());
        }

        public static void Register(PlayerBotBehavior behavior)
        {
            if (behavior == null || Behaviors.Contains(behavior)) return;
            Behaviors.Add(behavior);
            Behaviors.Sort(delegate(PlayerBotBehavior left, PlayerBotBehavior right)
            {
                return right.Priority.CompareTo(left.Priority);
            });
        }

        internal static void Tick(PlayerBot bot)
        {
            foreach (PlayerBotBehavior behavior in Behaviors)
            {
                if (!behavior.Handles(bot)) continue;
                behavior.Tick(bot);
                return;
            }
        }

        private sealed class DeadPlayerBotBehavior : PlayerBotBehavior
        {
            public override int Priority { get { return 1000; } }
            public override bool Handles(PlayerBot bot) { return bot != null && !bot.Alive; }
            public override void Tick(PlayerBot bot) { PlayerBotService.TickDeadBehavior(bot); }
        }

        private sealed class BankSitterPlayerBotBehavior : PlayerBotBehavior
        {
            public override int Priority { get { return 900; } }
            public override bool Handles(PlayerBot bot) { return PlayerBotService.IsBankSitterBehavior(bot); }
            public override void Tick(PlayerBot bot) { PlayerBotService.TickBankSitterBehavior(bot); }
        }

        private sealed class CorpseRecoveryPlayerBotBehavior : PlayerBotBehavior
        {
            public override int Priority { get { return 950; } }
            public override bool Handles(PlayerBot bot) { return PlayerBotService.HasCorpseRecoveryBehavior(bot); }
            public override void Tick(PlayerBot bot) { PlayerBotService.TickCorpseRecoveryBehavior(bot); }
        }

        private sealed class DestinationVisitorPlayerBotBehavior : PlayerBotBehavior
        {
            public override int Priority { get { return 800; } }
            public override bool Handles(PlayerBot bot) { return PlayerBotService.IsDestinationVisitorBehavior(bot); }
            public override void Tick(PlayerBot bot) { PlayerBotService.TickDestinationVisitorBehavior(bot); }
        }

        private sealed class CombatPlayerBotBehavior : PlayerBotBehavior
        {
            public override int Priority { get { return 700; } }
            public override bool Handles(PlayerBot bot) { return PlayerBotService.ShouldFightBehavior(bot); }
            public override void Tick(PlayerBot bot) { PlayerBotService.TickCombatBehavior(bot); }
        }

        private sealed class PartyPlayerBotBehavior : PlayerBotBehavior
        {
            public override int Priority { get { return 750; } }
            public override bool Handles(PlayerBot bot) { return PlayerBotParties.IsManagedMember(bot); }
            public override void Tick(PlayerBot bot) { PlayerBotParties.Tick(bot); }
        }

        private sealed class LaborPlayerBotBehavior : PlayerBotBehavior
        {
            public override int Priority { get { return 650; } }
            public override bool Handles(PlayerBot bot) { return bot != null && (bot.LaborKind != PlayerBotLaborKind.None || bot.LaborReturning); }
            public override void Tick(PlayerBot bot) { PlayerBotLabor.Tick(bot); }
        }

        private sealed class TravelPlayerBotBehavior : PlayerBotBehavior
        {
            public override int Priority { get { return Int32.MinValue; } }
            public override bool Handles(PlayerBot bot) { return bot != null; }
            public override void Tick(PlayerBot bot) { PlayerBotService.TickTravelBehavior(bot); }
        }
    }
}
