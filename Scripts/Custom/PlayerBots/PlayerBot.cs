using System;
using Server.Items;
using Server.Mobiles;

namespace Server.CustomBots
{
    public enum PlayerBotRole
    {
        Traveler,
        Banker,
        Adventurer,
        Townie,
        PlayerKiller
    }

    // A persistent PlayerMobile without a NetState. It deliberately uses
    // ServUO's native save format and combat/notoriety rules.
    public class PlayerBot : PlayerMobile
    {
        [CommandProperty(AccessLevel.GameMaster)]
        public PlayerBotRole BotRole { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public Point3D Destination { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public string DestinationName { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime NextAction { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime NextChat { get; set; }

        // Empty for manual/population bots. Stored-spawn bots retain the
        // definition that owns them so regenerate never touches others.
        [CommandProperty(AccessLevel.GameMaster)]
        public string SpawnSource { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public string DungeonReturnName { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime DungeonReturnAt { get; set; }

        [Constructable]
        public PlayerBot() : this(PlayerBotRole.Traveler)
        {
        }

        public PlayerBot(PlayerBotRole role)
        {
            // PlayerMobile normally marks itself as a logged-out human and the
            // core moves such mobiles to Internal on load. Bots are headless
            // actors, so keep this false while retaining PlayerMobile combat
            // and skill behavior.
            Player = false;
            BotRole = role;
            RawStr = Utility.RandomMinMax(80, 100);
            RawDex = Utility.RandomMinMax(80, 100);
            RawInt = Utility.RandomMinMax(40, 70);
            Skills[SkillName.Swords].Base = 75;
            Skills[SkillName.Tactics].Base = 75;
            Skills[SkillName.Anatomy].Base = 65;
            Skills[SkillName.Healing].Base = 60;
            AddToBackpack(new Gold(Utility.RandomMinMax(100, 450)));
            if (role == PlayerBotRole.PlayerKiller)
            {
                RawStr = 100;
                RawDex = 100;
                Skills[SkillName.Swords].Base = 90;
                Skills[SkillName.Tactics].Base = 90;
                Skills[SkillName.Anatomy].Base = 80;
            }
            Hits = HitsMax;
            Stam = StamMax;
            Mana = ManaMax;
            PlayerBotPersonas.ApplyNew(this);
            Destination = Point3D.Zero;
            DestinationName = "";
            SpawnSource = "";
            DungeonReturnName = "";
            DungeonReturnAt = DateTime.MinValue;
        }

        public PlayerBot(Serial serial) : base(serial)
        {
        }

        public override void OnAfterSpawn()
        {
            base.OnAfterSpawn();
            PlayerBotService.EnsureDestination(this);
        }

        public override void OnDeath(Container c)
        {
            PlayerBotService.ReportMurder(this);
            base.OnDeath(c);
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(2);
            writer.Write((int)BotRole);
            writer.Write(Destination);
            writer.Write(DestinationName);
            writer.Write(NextAction);
            writer.Write(NextChat);
            writer.Write(SpawnSource);
            writer.Write(DungeonReturnName);
            writer.Write(DungeonReturnAt);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            var version = reader.ReadInt();
            BotRole = (PlayerBotRole)reader.ReadInt();
            Destination = reader.ReadPoint3D();
            DestinationName = reader.ReadString() ?? "";
            NextAction = reader.ReadDateTime();
            NextChat = reader.ReadDateTime();
            SpawnSource = version >= 1 ? reader.ReadString() ?? "" : "";
            DungeonReturnName = version >= 2 ? reader.ReadString() ?? "" : "";
            DungeonReturnAt = version >= 2 ? reader.ReadDateTime() : DateTime.MinValue;
            Player = false;
        }
    }

}
