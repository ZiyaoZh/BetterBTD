using System.IO;
using System.Text.Json.Serialization;

namespace BetterBTD.Models.ScriptEditor;

public static class LegacyScriptFormat
{
    public const string FileExtension = ".btd6";
    public const int InstructionSlotCount = 12;
    public const int CoordinateScale = 10_000;
}

public enum LegacyActionType
{
    PlaceMonkey = 0,
    UpgradeMonkey = 1,
    SwitchMonkeyTarget = 2,
    ActivateAbility = 3,
    ToggleGameSpeed = 4,
    SellMonkey = 5,
    SetMonkeyAbility = 6,
    PlaceHero = 7,
    UpgradeHero = 8,
    PlaceHeroInventory = 9,
    SwitchHeroTarget = 10,
    SetHeroAbility = 11,
    SellHero = 12,
    MouseClick = 13,
    ModifyMonkeyCoordinate = 14,
    Wait = 15,
    StartFreeplay = 16,
    EndFreeplay = 17,
    Jump = 18,
    InstructionsBundle = 25,
    Unknown = 255
}

public enum LegacyUpgradeType
{
    Top = 0,
    Middle = 1,
    Bottom = 2,
    TopOnce = 3,
    MiddleOnce = 4,
    BottomOnce = 5
}

public enum LegacyTargetType
{
    Right = 0,
    RightDouble = 1,
    RightTriple = 2,
    Left = 3,
    LeftDouble = 4,
    LeftTriple = 5
}

public enum LegacyMonkeyFunctionType
{
    Function1 = 0,
    Function1Coordinate = 1,
    Function2 = 2,
    Function2Coordinate = 3
}

public enum LegacyPlaceCheckType
{
    Check = 0,
    None = 1
}

public enum LegacySpeedType
{
    Switch = 0,
    NextRound = 1
}

public enum LegacyCoordinateType
{
    None = 0,
    Coordinate = 1
}

public enum LegacyMapType
{
    MonkeyMeadow = 0,
    InTheLoop = 1,
    MiddleOfTheRoad = 2,
    TinkerTon = 3,
    TreeStump = 4,
    TownCenter = 5,
    OneTwoTree = 6,
    ScrapYard = 7,
    TheCabin = 8,
    Resort = 9,
    Skates = 10,
    LotusIsland = 11,
    CandyFalls = 12,
    WinterPark = 13,
    Carved = 14,
    ParkPath = 15,
    AlpineRun = 16,
    FrozenOver = 17,
    Cubism = 18,
    FourCircles = 19,
    Hedge = 20,
    EndOfTheRoad = 21,
    Logs = 22,
    SpaPits = 23,
    ThreeMilesRound = 24,
    SulfurSprings = 30,
    WaterPark = 31,
    Polyphemus = 32,
    CoveredGarden = 33,
    Quarry = 34,
    QuietStreet = 35,
    BloonariusPrime = 36,
    Balance = 37,
    Encrypted = 38,
    Bazaar = 39,
    AdorasTemple = 40,
    SpringSpring = 41,
    KartMonkey = 42,
    MoonLanding = 43,
    Haunted = 44,
    Downstream = 45,
    FiringRange = 46,
    Cracked = 47,
    Streambed = 48,
    Chutes = 49,
    Rake = 50,
    SpiceIslands = 51,
    LuminousCove = 52,
    LostCrevasse = 53,
    CastleRevenge = 60,
    DarkPath = 61,
    Erosion = 62,
    MidnightMansion = 63,
    SunkenColumns = 64,
    XFactor = 65,
    Mesa = 66,
    Geared = 67,
    Spillway = 68,
    Cargo = 69,
    PatsPond = 70,
    Peninsula = 71,
    HighFinance = 72,
    AnotherBrick = 73,
    OffTheCoast = 74,
    Cornfield = 75,
    Underground = 76,
    AncientPortal = 77,
    LastResort = 78,
    EnchantedGlade = 79,
    SunsetGulch = 80,
    PartyParade = 81,
    MushroomGortto = 82,
    GlacialTrail = 90,
    DarkDungeon = 91,
    Sanctuary = 92,
    Ravine = 93,
    FloodedValley = 94,
    Infernal = 95,
    BloodyPuddles = 96,
    Workshop = 97,
    Quad = 98,
    DarkCastle = 99,
    MuddyPuddles = 100,
    Ouch = 101,
    TrickyTracks = 102,
    Unknown = 255
}

public enum LegacyMonkeyType
{
    DartMonkey = 0,
    BoomerangMonkey = 1,
    BombShooter = 2,
    TackShooter = 3,
    IceMonkey = 4,
    GlueGunner = 5,
    Desperado = 6,
    SniperMonkey = 10,
    MonkeySub = 11,
    MonkeyBuccaneer = 12,
    MonkeyAce = 13,
    HeliPilot = 14,
    MortarMonkey = 15,
    DartlingGunner = 16,
    WizardMonkey = 20,
    SuperMonkey = 21,
    NinjaMonkey = 22,
    Alchemist = 23,
    Druid = 24,
    MerMonkey = 25,
    BananaFarm = 30,
    SpikeFactory = 31,
    MonkeyVillage = 32,
    EngineerMonkey = 33,
    BeastHandler = 34,
    Unknown = 255
}

public enum LegacyHeroType
{
    Quincy = 0,
    Gwendolin = 1,
    StrikerJones = 2,
    ObynGreenfoot = 3,
    Rosalia = 4,
    CaptainChurchill = 5,
    Benjamin = 6,
    PatFusty = 7,
    Ezili = 8,
    Adora = 9,
    Etienne = 10,
    Sauda = 11,
    AdmiralBrickell = 12,
    Psi = 13,
    Geraldo = 14,
    Corvus = 15,
    Silas = 16,
    Unknown = 255
}

public enum LegacyLevelDifficulty
{
    Easy = 0,
    Medium = 1,
    Hard = 2,
    Any = 3,
    Unknown = 255
}

public enum LegacyLevelMode
{
    Standard = 0,
    Deflation = 1,
    Apopalypse = 2,
    Reverse = 3,
    HalfCash = 4,
    DoubleHpMoabs = 5,
    AlternateBloonsRounds = 6,
    Impoppable = 7,
    CHIMPS = 8,
    PrimaryOnly = 9,
    MilitaryOnly = 10,
    MagicMonkeysOnly = 11,
    Unknown = 255
}

public enum LegacySkillType
{
    Skill1 = 0,
    Skill2 = 1,
    Skill3 = 2,
    Skill4 = 3,
    Skill5 = 4,
    Skill6 = 5,
    Skill7 = 6,
    Skill8 = 7,
    Skill9 = 8,
    Skill10 = 9,
    Skill11 = 10,
    Skill12 = 11
}

public enum LegacyHeroObjectType
{
    HeroObject1 = 1,
    HeroObject2 = 2,
    HeroObject3 = 3,
    HeroObject4 = 4,
    HeroObject5 = 5,
    HeroObject6 = 6,
    HeroObject7 = 7,
    HeroObject8 = 8,
    HeroObject9 = 9,
    HeroObject10 = 10,
    HeroObject11 = 11,
    HeroObject12 = 12,
    HeroObject13 = 13,
    HeroObject14 = 14,
    HeroObject15 = 15,
    HeroObject16 = 16
}

public sealed class LegacyScriptModel
{
    public LegacyScriptMetadata Metadata { get; set; } = new();

    public List<int[]> InstructionsList { get; set; } = [];

    public List<int> MonkeyCounts { get; set; } = [];

    public List<int> MonkeyIds { get; set; } = [];
}

public sealed class LegacyScriptMetadata
{
    public string Version { get; set; } = string.Empty;

    public string ScriptName { get; set; } = string.Empty;

    public int SelectedMap { get; set; }

    public int SelectedDifficulty { get; set; }

    public int SelectedMode { get; set; }

    public int SelectedHero { get; set; }

    public LegacyAnchorCoordinates AnchorCoords { get; set; } = new();
}

public sealed class LegacyAnchorCoordinates
{
    [JsonPropertyName("Item1")]
    public double X { get; set; }

    [JsonPropertyName("Item2")]
    public double Y { get; set; }
}

public sealed class LegacyScriptInstruction
{
    public required int Index { get; init; }

    public required int[] Slots { get; init; }

    public LegacyActionType? ActionType =>
        Enum.IsDefined(typeof(LegacyActionType), Slots[0])
            ? (LegacyActionType)Slots[0]
            : null;

    public int Argument1 => Slots[1];
    public int Argument2 => Slots[2];
    public int Argument3 => Slots[3];
    public int Argument4 => Slots[4];
    public int Argument5 => Slots[5];
    public int Argument6 => Slots[6];
    public int Argument7 => Slots[7];
    public int CoordinateXEncoded => Slots[8];
    public int CoordinateYEncoded => Slots[9];
    public int RoundTrigger => Slots[10];
    public int CoinTrigger => Slots[11];

    public bool HasCoordinate =>
        CoordinateXEncoded >= 0 && CoordinateYEncoded >= 0;

    public double CoordinateX => CoordinateXEncoded / (double)LegacyScriptFormat.CoordinateScale;

    public double CoordinateY => CoordinateYEncoded / (double)LegacyScriptFormat.CoordinateScale;

    public static LegacyScriptInstruction FromSlots(int index, int[] slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (slots.Length != LegacyScriptFormat.InstructionSlotCount)
        {
            throw new InvalidDataException(
                $"Legacy instruction at index {index} must contain {LegacyScriptFormat.InstructionSlotCount} integers, but found {slots.Length}.");
        }

        return new LegacyScriptInstruction
        {
            Index = index,
            Slots = slots
        };
    }
}
