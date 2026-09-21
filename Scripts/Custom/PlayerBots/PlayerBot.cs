using System;
using System.Collections.Generic;
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
        PlayerKiller,
        Thief
    }

    // Mirrors UO Offline's BankSitterBehavior roles. Bank crowd behavior is
    // deliberately separate from a character's combat/travel identity.
    public enum PlayerBotBankRole
    {
        Regular,
        Hawker,
        Afk,
        ResistMacro,
        HidingMacro,
        StealthMacro
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

        [CommandProperty(AccessLevel.GameMaster)]
        public PlayerBotBankRole BankRole { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public Point3D BankHome { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime NextBankAction { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool BankSitterInitialized { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool BankWallSitter { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public int BankSitterLayoutVersion { get; set; }

        // UO Offline visitors keep a short-lived activity state separate from
        // their travel identity.  A traveler can therefore use a bank, pause
        // there, and then resume an ordinary trip instead of turning into a
        // permanent bank sitter.
        [CommandProperty(AccessLevel.GameMaster)]
        public string BankVisitName { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public Point3D BankVisitHome { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime BankVisitUntil { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime NextBankVisitAction { get; set; }

        // Empty for manual/population bots. Stored-spawn bots retain the
        // definition that owns them so regenerate never touches others.
        [CommandProperty(AccessLevel.GameMaster)]
        public string SpawnSource { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public string DungeonReturnName { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime DungeonReturnAt { get; set; }

        // Native dungeon travel is a small persisted state machine.  It
        // records physical pads only after the live audit has verified both
        // teleporters, so a restart cannot turn it into a coordinate jump.
        [CommandProperty(AccessLevel.GameMaster)]
        public string DungeonTravelState { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public string DungeonInteriorName { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public Point3D DungeonLanding { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public Point3D DungeonReturnPad { get; set; }

        // A route is an ordered set of short, authored legs.  It is kept on
        // the bot so the graph can be rebuilt without losing its current
        // progress, and so no long-distance movement is silently converted
        // into a moongate hop.
        public List<Point3D> RoutePoints { get; private set; }

        public int RouteIndex { get; set; }

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
            else if (role == PlayerBotRole.Thief)
            {
                RawDex = 100;
                Skills[SkillName.Stealing].Base = 75;
                Skills[SkillName.Hiding].Base = 65;
            }
            Hits = HitsMax;
            Stam = StamMax;
            Mana = ManaMax;
            PlayerBotPersonas.ApplyNew(this);
            Destination = Point3D.Zero;
            DestinationName = "";
            BankRole = PlayerBotBankRole.Regular;
            BankHome = Point3D.Zero;
            NextBankAction = DateTime.MinValue;
            BankSitterInitialized = false;
            BankWallSitter = false;
            BankSitterLayoutVersion = 0;
            BankVisitName = "";
            BankVisitHome = Point3D.Zero;
            BankVisitUntil = DateTime.MinValue;
            NextBankVisitAction = DateTime.MinValue;
            SpawnSource = "";
            DungeonReturnName = "";
            DungeonReturnAt = DateTime.MinValue;
            DungeonTravelState = "";
            DungeonInteriorName = "";
            DungeonLanding = Point3D.Zero;
            DungeonReturnPad = Point3D.Zero;
            RoutePoints = new List<Point3D>();
            RouteIndex = 0;
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
            writer.Write(7);
            writer.Write((int)BotRole);
            writer.Write(Destination);
            writer.Write(DestinationName);
            writer.Write(NextAction);
            writer.Write(NextChat);
            writer.Write((int)BankRole);
            writer.Write(BankHome);
            writer.Write(NextBankAction);
            writer.Write(BankSitterInitialized);
            writer.Write(BankWallSitter);
            writer.Write(BankSitterLayoutVersion);
            writer.Write(BankVisitName);
            writer.Write(BankVisitHome);
            writer.Write(BankVisitUntil);
            writer.Write(NextBankVisitAction);
            writer.Write(SpawnSource);
            writer.Write(DungeonReturnName);
            writer.Write(DungeonReturnAt);
            writer.Write(RoutePoints == null ? 0 : RoutePoints.Count);
            if (RoutePoints != null)
            foreach (var point in RoutePoints) writer.Write(point);
            writer.Write(RouteIndex);
            writer.Write(DungeonTravelState);
            writer.Write(DungeonInteriorName);
            writer.Write(DungeonLanding);
            writer.Write(DungeonReturnPad);
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
            BankRole = version >= 5 ? (PlayerBotBankRole)reader.ReadInt() : PlayerBotBankRole.Regular;
            BankHome = version >= 5 ? reader.ReadPoint3D() : Point3D.Zero;
            NextBankAction = version >= 5 ? reader.ReadDateTime() : DateTime.MinValue;
            BankSitterInitialized = version >= 5 && reader.ReadBool();
            BankWallSitter = version >= 6 && reader.ReadBool();
            BankSitterLayoutVersion = version >= 6 ? reader.ReadInt() : 0;
            BankVisitName = version >= 7 ? reader.ReadString() ?? "" : "";
            BankVisitHome = version >= 7 ? reader.ReadPoint3D() : Point3D.Zero;
            BankVisitUntil = version >= 7 ? reader.ReadDateTime() : DateTime.MinValue;
            NextBankVisitAction = version >= 7 ? reader.ReadDateTime() : DateTime.MinValue;
            SpawnSource = version >= 1 ? reader.ReadString() ?? "" : "";
            DungeonReturnName = version >= 2 ? reader.ReadString() ?? "" : "";
            DungeonReturnAt = version >= 2 ? reader.ReadDateTime() : DateTime.MinValue;
            RoutePoints = new List<Point3D>();
            if (version >= 3)
            {
                var count = reader.ReadInt();
                for (var i = 0; i < count; i++) RoutePoints.Add(reader.ReadPoint3D());
                RouteIndex = reader.ReadInt();
            }
            else RouteIndex = 0;
            DungeonTravelState = version >= 4 ? reader.ReadString() ?? "" : "";
            DungeonInteriorName = version >= 4 ? reader.ReadString() ?? "" : "";
            DungeonLanding = version >= 4 ? reader.ReadPoint3D() : Point3D.Zero;
            DungeonReturnPad = version >= 4 ? reader.ReadPoint3D() : Point3D.Zero;
            Player = false;
        }
    }

}
