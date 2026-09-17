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
        Townie
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
            Name = PlayerBotNames.Next();
            Female = Utility.RandomBool();
            Body = Female ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();
            SpeechHue = Utility.RandomMinMax(0x3B2, 0x59);
            RawStr = Utility.RandomMinMax(80, 100);
            RawDex = Utility.RandomMinMax(80, 100);
            RawInt = Utility.RandomMinMax(40, 70);
            Hits = HitsMax;
            Stam = StamMax;
            Mana = ManaMax;
            Skills[SkillName.Swords].Base = 75;
            Skills[SkillName.Tactics].Base = 75;
            Skills[SkillName.Anatomy].Base = 65;
            Skills[SkillName.Healing].Base = 60;
            AddToBackpack(new Gold(Utility.RandomMinMax(100, 450)));
            Destination = Point3D.Zero;
            DestinationName = "";
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
            writer.Write(0);
            writer.Write((int)BotRole);
            writer.Write(Destination);
            writer.Write(DestinationName);
            writer.Write(NextAction);
            writer.Write(NextChat);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            _ = reader.ReadInt();
            BotRole = (PlayerBotRole)reader.ReadInt();
            Destination = reader.ReadPoint3D();
            DestinationName = reader.ReadString() ?? "";
            NextAction = reader.ReadDateTime();
            NextChat = reader.ReadDateTime();
            Player = false;
        }
    }

    internal static class PlayerBotNames
    {
        private static readonly string[] Names =
        {
            "Alden", "Aric", "Brenna", "Celia", "Corwin", "Darian", "Elise", "Fiona",
            "Gareth", "Hilda", "Ivor", "Jessa", "Kara", "Loric", "Mira", "Nolan",
            "Orin", "Petra", "Quinn", "Rhea", "Silas", "Tessa", "Ulric", "Vera"
        };

        public static string Next()
        {
            return Names[Utility.Random(Names.Length)] + " " + Utility.RandomMinMax(10, 999);
        }
    }
}
