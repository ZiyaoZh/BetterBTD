using System;
using System.Collections.Generic;
using BetterBTD.Services;

namespace BetterBTD.Models.GameElements;

public sealed record MonkeyTowerDefinition(MonkeyTowerType Type, MonkeyTowerCategory Category, string NameKey);

public sealed record HeroDefinition(HeroType Type, string NameKey);

public sealed record MapDefinition(GameMapType Type, MapDifficultyTier Tier, string NameKey);

public sealed record InventoryDefinition(InventoryType Type, string NameKey);

public sealed record ActivatedAbilityDefinition(ActivatedAbilityType Type, string NameKey);

public static class GameElementCatalog
{
    public static IReadOnlyList<MonkeyTowerDefinition> MonkeyTowers { get; } =
    [
        new(MonkeyTowerType.DartMonkey, MonkeyTowerCategory.Primary, "GameElements.MonkeyTower.DartMonkey"),
        new(MonkeyTowerType.BoomerangMonkey, MonkeyTowerCategory.Primary, "GameElements.MonkeyTower.BoomerangMonkey"),
        new(MonkeyTowerType.BombShooter, MonkeyTowerCategory.Primary, "GameElements.MonkeyTower.BombShooter"),
        new(MonkeyTowerType.TackShooter, MonkeyTowerCategory.Primary, "GameElements.MonkeyTower.TackShooter"),
        new(MonkeyTowerType.IceMonkey, MonkeyTowerCategory.Primary, "GameElements.MonkeyTower.IceMonkey"),
        new(MonkeyTowerType.GlueGunner, MonkeyTowerCategory.Primary, "GameElements.MonkeyTower.GlueGunner"),
        new(MonkeyTowerType.Desperado, MonkeyTowerCategory.Primary, "GameElements.MonkeyTower.Desperado"),

        new(MonkeyTowerType.SniperMonkey, MonkeyTowerCategory.Military, "GameElements.MonkeyTower.SniperMonkey"),
        new(MonkeyTowerType.MonkeySub, MonkeyTowerCategory.Military, "GameElements.MonkeyTower.MonkeySub"),
        new(MonkeyTowerType.MonkeyBuccaneer, MonkeyTowerCategory.Military, "GameElements.MonkeyTower.MonkeyBuccaneer"),
        new(MonkeyTowerType.MonkeyAce, MonkeyTowerCategory.Military, "GameElements.MonkeyTower.MonkeyAce"),
        new(MonkeyTowerType.HeliPilot, MonkeyTowerCategory.Military, "GameElements.MonkeyTower.HeliPilot"),
        new(MonkeyTowerType.MortarMonkey, MonkeyTowerCategory.Military, "GameElements.MonkeyTower.MortarMonkey"),
        new(MonkeyTowerType.DartlingGunner, MonkeyTowerCategory.Military, "GameElements.MonkeyTower.DartlingGunner"),

        new(MonkeyTowerType.WizardMonkey, MonkeyTowerCategory.Magic, "GameElements.MonkeyTower.WizardMonkey"),
        new(MonkeyTowerType.SuperMonkey, MonkeyTowerCategory.Magic, "GameElements.MonkeyTower.SuperMonkey"),
        new(MonkeyTowerType.NinjaMonkey, MonkeyTowerCategory.Magic, "GameElements.MonkeyTower.NinjaMonkey"),
        new(MonkeyTowerType.Alchemist, MonkeyTowerCategory.Magic, "GameElements.MonkeyTower.Alchemist"),
        new(MonkeyTowerType.Druid, MonkeyTowerCategory.Magic, "GameElements.MonkeyTower.Druid"),
        new(MonkeyTowerType.MerMonkey, MonkeyTowerCategory.Magic, "GameElements.MonkeyTower.MerMonkey"),

        new(MonkeyTowerType.BananaFarm, MonkeyTowerCategory.Support, "GameElements.MonkeyTower.BananaFarm"),
        new(MonkeyTowerType.SpikeFactory, MonkeyTowerCategory.Support, "GameElements.MonkeyTower.SpikeFactory"),
        new(MonkeyTowerType.MonkeyVillage, MonkeyTowerCategory.Support, "GameElements.MonkeyTower.MonkeyVillage"),
        new(MonkeyTowerType.EngineerMonkey, MonkeyTowerCategory.Support, "GameElements.MonkeyTower.EngineerMonkey"),
        new(MonkeyTowerType.BeastHandler, MonkeyTowerCategory.Support, "GameElements.MonkeyTower.BeastHandler")
    ];

    public static IReadOnlyList<HeroDefinition> Heroes { get; } =
    [
        new(HeroType.Quincy, "GameElements.Hero.Quincy"),
        new(HeroType.Gwendolin, "GameElements.Hero.Gwendolin"),
        new(HeroType.StrikerJones, "GameElements.Hero.StrikerJones"),
        new(HeroType.ObynGreenfoot, "GameElements.Hero.ObynGreenfoot"),
        new(HeroType.Rosalia, "GameElements.Hero.Rosalia"),
        new(HeroType.CaptainChurchill, "GameElements.Hero.CaptainChurchill"),
        new(HeroType.Benjamin, "GameElements.Hero.Benjamin"),
        new(HeroType.PatFusty, "GameElements.Hero.PatFusty"),
        new(HeroType.Ezili, "GameElements.Hero.Ezili"),
        new(HeroType.Adora, "GameElements.Hero.Adora"),
        new(HeroType.Etienne, "GameElements.Hero.Etienne"),
        new(HeroType.Sauda, "GameElements.Hero.Sauda"),
        new(HeroType.AdmiralBrickell, "GameElements.Hero.AdmiralBrickell"),
        new(HeroType.Psi, "GameElements.Hero.Psi"),
        new(HeroType.Geraldo, "GameElements.Hero.Geraldo"),
        new(HeroType.Corvus, "GameElements.Hero.Corvus"),
        new(HeroType.Silas, "GameElements.Hero.Silas")
    ];

    public static IReadOnlyList<InventoryDefinition> InventoryItems { get; } =
    [
        new(InventoryType.Inventory1, "GameElements.Inventory.Inventory1"),
        new(InventoryType.Inventory2, "GameElements.Inventory.Inventory2"),
        new(InventoryType.Inventory3, "GameElements.Inventory.Inventory3"),
        new(InventoryType.Inventory4, "GameElements.Inventory.Inventory4"),
        new(InventoryType.Inventory5, "GameElements.Inventory.Inventory5"),
        new(InventoryType.Inventory6, "GameElements.Inventory.Inventory6"),
        new(InventoryType.Inventory7, "GameElements.Inventory.Inventory7"),
        new(InventoryType.Inventory8, "GameElements.Inventory.Inventory8"),
        new(InventoryType.Inventory9, "GameElements.Inventory.Inventory9"),
        new(InventoryType.Inventory10, "GameElements.Inventory.Inventory10"),
        new(InventoryType.Inventory11, "GameElements.Inventory.Inventory11"),
        new(InventoryType.Inventory12, "GameElements.Inventory.Inventory12"),
        new(InventoryType.Inventory13, "GameElements.Inventory.Inventory13"),
        new(InventoryType.Inventory14, "GameElements.Inventory.Inventory14"),
        new(InventoryType.Inventory15, "GameElements.Inventory.Inventory15"),
        new(InventoryType.Inventory16, "GameElements.Inventory.Inventory16")
    ];

    public static IReadOnlyList<ActivatedAbilityDefinition> ActivatedAbilities { get; } =
    [
        new(ActivatedAbilityType.ActivatedAbility1, "GameElements.ActivatedAbility.ActivatedAbility1"),
        new(ActivatedAbilityType.ActivatedAbility2, "GameElements.ActivatedAbility.ActivatedAbility2"),
        new(ActivatedAbilityType.ActivatedAbility3, "GameElements.ActivatedAbility.ActivatedAbility3"),
        new(ActivatedAbilityType.ActivatedAbility4, "GameElements.ActivatedAbility.ActivatedAbility4"),
        new(ActivatedAbilityType.ActivatedAbility5, "GameElements.ActivatedAbility.ActivatedAbility5"),
        new(ActivatedAbilityType.ActivatedAbility6, "GameElements.ActivatedAbility.ActivatedAbility6"),
        new(ActivatedAbilityType.ActivatedAbility7, "GameElements.ActivatedAbility.ActivatedAbility7"),
        new(ActivatedAbilityType.ActivatedAbility8, "GameElements.ActivatedAbility.ActivatedAbility8"),
        new(ActivatedAbilityType.ActivatedAbility9, "GameElements.ActivatedAbility.ActivatedAbility9"),
        new(ActivatedAbilityType.ActivatedAbility10, "GameElements.ActivatedAbility.ActivatedAbility10"),
        new(ActivatedAbilityType.ActivatedAbility11, "GameElements.ActivatedAbility.ActivatedAbility11"),
        new(ActivatedAbilityType.ActivatedAbility12, "GameElements.ActivatedAbility.ActivatedAbility12")
    ];

    public static IReadOnlyList<string> NextRoundActions { get; } =
    [
        "PlayFastForward",
        "SendNextRound"
    ];

    public static string GetMonkeyTowerDisplayName(MonkeyTowerType type)
    {
        var localizationService = LocalizationService.Instance;
        foreach (var definition in MonkeyTowers)
        {
            if (definition.Type == type)
            {
                return localizationService.T(definition.NameKey);
            }
        }

        return type.ToString();
    }

    public static string GetHeroDisplayName(HeroType type)
    {
        var localizationService = LocalizationService.Instance;
        foreach (var definition in Heroes)
        {
            if (definition.Type == type)
            {
                return localizationService.T(definition.NameKey);
            }
        }

        return type.ToString();
    }

    public static string GetInventoryDisplayName(InventoryType type)
    {
        var localizationService = LocalizationService.Instance;
        foreach (var definition in InventoryItems)
        {
            if (definition.Type == type)
            {
                return localizationService.T(definition.NameKey);
            }
        }

        return type.ToString();
    }

    public static string GetActivatedAbilityDisplayName(ActivatedAbilityType type)
    {
        var localizationService = LocalizationService.Instance;
        foreach (var definition in ActivatedAbilities)
        {
            if (definition.Type == type)
            {
                return localizationService.T(definition.NameKey);
            }
        }

        return type.ToString();
    }

    public static string GetNextRoundActionDisplayName(string action)
    {
        return action switch
        {
            "PlayFastForward" => LocalizationService.Instance.T("Editor.Property.NextRound.PlayFastForward"),
            "SendNextRound" => LocalizationService.Instance.T("Editor.Property.NextRound.SendNextRound"),
            _ => action
        };
    }

    public static string GetMapDisplayName(GameMapType type)
    {
        var localizationService = LocalizationService.Instance;
        foreach (var definition in Maps)
        {
            if (definition.Type == type)
            {
                return localizationService.T(definition.NameKey);
            }
        }

        return type.ToString();
    }

    public static string GetMapTierDisplayName(MapDifficultyTier tier)
    {
        return tier switch
        {
            MapDifficultyTier.Beginner => LocalizationService.Instance.T("GameElements.MapTier.Beginner"),
            MapDifficultyTier.Intermediate => LocalizationService.Instance.T("GameElements.MapTier.Intermediate"),
            MapDifficultyTier.Advanced => LocalizationService.Instance.T("GameElements.MapTier.Advanced"),
            MapDifficultyTier.Expert => LocalizationService.Instance.T("GameElements.MapTier.Expert"),
            _ => tier.ToString()
        };
    }

    public static IReadOnlyList<MapDefinition> Maps { get; } =
    [
        // Beginner
        new(GameMapType.MonkeyMeadow, MapDifficultyTier.Beginner, "GameElements.Map.MonkeyMeadow"),
        new(GameMapType.InTheLoop, MapDifficultyTier.Beginner, "GameElements.Map.InTheLoop"),
        new(GameMapType.ThreeMilesRound, MapDifficultyTier.Beginner, "GameElements.Map.ThreeMilesRound"),
        new(GameMapType.MiddleOfTheRoad, MapDifficultyTier.Beginner, "GameElements.Map.MiddleOfTheRoad"),
        new(GameMapType.SpaPits, MapDifficultyTier.Beginner, "GameElements.Map.SpaPits"),
        new(GameMapType.TinkerTon, MapDifficultyTier.Beginner, "GameElements.Map.TinkerTon"),
        new(GameMapType.TreeStump, MapDifficultyTier.Beginner, "GameElements.Map.TreeStump"),
        new(GameMapType.TownCenter, MapDifficultyTier.Beginner, "GameElements.Map.TownCenter"),
        new(GameMapType.OneTwoTree, MapDifficultyTier.Beginner, "GameElements.Map.OneTwoTree"),
        new(GameMapType.ScrapYard, MapDifficultyTier.Beginner, "GameElements.Map.ScrapYard"),
        new(GameMapType.TheCabin, MapDifficultyTier.Beginner, "GameElements.Map.TheCabin"),
        new(GameMapType.Resort, MapDifficultyTier.Beginner, "GameElements.Map.Resort"),
        new(GameMapType.Skates, MapDifficultyTier.Beginner, "GameElements.Map.Skates"),
        new(GameMapType.LotusIsland, MapDifficultyTier.Beginner, "GameElements.Map.LotusIsland"),
        new(GameMapType.CandyFalls, MapDifficultyTier.Beginner, "GameElements.Map.CandyFalls"),
        new(GameMapType.WinterPark, MapDifficultyTier.Beginner, "GameElements.Map.WinterPark"),
        new(GameMapType.Carved, MapDifficultyTier.Beginner, "GameElements.Map.Carved"),
        new(GameMapType.ParkPath, MapDifficultyTier.Beginner, "GameElements.Map.ParkPath"),
        new(GameMapType.AlpineRun, MapDifficultyTier.Beginner, "GameElements.Map.AlpineRun"),
        new(GameMapType.FrozenOver, MapDifficultyTier.Beginner, "GameElements.Map.FrozenOver"),
        new(GameMapType.Cubism, MapDifficultyTier.Beginner, "GameElements.Map.Cubism"),
        new(GameMapType.FourCircles, MapDifficultyTier.Beginner, "GameElements.Map.FourCircles"),
        new(GameMapType.Hedge, MapDifficultyTier.Beginner, "GameElements.Map.Hedge"),
        new(GameMapType.EndOfTheRoad, MapDifficultyTier.Beginner, "GameElements.Map.EndOfTheRoad"),
        new(GameMapType.Logs, MapDifficultyTier.Beginner, "GameElements.Map.Logs"),
        new(GameMapType.SkullTweak, MapDifficultyTier.Beginner, "GameElements.Map.SkullTweak"),

        // Intermediate
        new(GameMapType.SulfurSprings, MapDifficultyTier.Intermediate, "GameElements.Map.SulfurSprings"),
        new(GameMapType.WaterPark, MapDifficultyTier.Intermediate, "GameElements.Map.WaterPark"),
        new(GameMapType.Polyphemus, MapDifficultyTier.Intermediate, "GameElements.Map.Polyphemus"),
        new(GameMapType.CoveredGarden, MapDifficultyTier.Intermediate, "GameElements.Map.CoveredGarden"),
        new(GameMapType.Quarry, MapDifficultyTier.Intermediate, "GameElements.Map.Quarry"),
        new(GameMapType.QuietStreet, MapDifficultyTier.Intermediate, "GameElements.Map.QuietStreet"),
        new(GameMapType.BloonariusPrime, MapDifficultyTier.Intermediate, "GameElements.Map.BloonariusPrime"),
        new(GameMapType.Balance, MapDifficultyTier.Intermediate, "GameElements.Map.Balance"),
        new(GameMapType.Encrypted, MapDifficultyTier.Intermediate, "GameElements.Map.Encrypted"),
        new(GameMapType.Bazaar, MapDifficultyTier.Intermediate, "GameElements.Map.Bazaar"),
        new(GameMapType.AdorasTemple, MapDifficultyTier.Intermediate, "GameElements.Map.AdorasTemple"),
        new(GameMapType.SpringSpring, MapDifficultyTier.Intermediate, "GameElements.Map.SpringSpring"),
        new(GameMapType.KartMonkey, MapDifficultyTier.Intermediate, "GameElements.Map.KartMonkey"),
        new(GameMapType.MoonLanding, MapDifficultyTier.Intermediate, "GameElements.Map.MoonLanding"),
        new(GameMapType.Haunted, MapDifficultyTier.Intermediate, "GameElements.Map.Haunted"),
        new(GameMapType.Downstream, MapDifficultyTier.Intermediate, "GameElements.Map.Downstream"),
        new(GameMapType.FiringRange, MapDifficultyTier.Intermediate, "GameElements.Map.FiringRange"),
        new(GameMapType.Cracked, MapDifficultyTier.Intermediate, "GameElements.Map.Cracked"),
        new(GameMapType.Streambed, MapDifficultyTier.Intermediate, "GameElements.Map.Streambed"),
        new(GameMapType.Chutes, MapDifficultyTier.Intermediate, "GameElements.Map.Chutes"),
        new(GameMapType.Rake, MapDifficultyTier.Intermediate, "GameElements.Map.Rake"),
        new(GameMapType.SpiceIslands, MapDifficultyTier.Intermediate, "GameElements.Map.SpiceIslands"),
        new(GameMapType.LuminousCove, MapDifficultyTier.Intermediate, "GameElements.Map.LuminousCove"),
        new(GameMapType.LostCrevasse, MapDifficultyTier.Intermediate, "GameElements.Map.LostCrevasse"),
        new(GameMapType.AncientPortal, MapDifficultyTier.Intermediate, "GameElements.Map.AncientPortal"),

        // Advanced
        new(GameMapType.CastleRevenge, MapDifficultyTier.Advanced, "GameElements.Map.CastleRevenge"),
        new(GameMapType.DarkPath, MapDifficultyTier.Advanced, "GameElements.Map.DarkPath"),
        new(GameMapType.Erosion, MapDifficultyTier.Advanced, "GameElements.Map.Erosion"),
        new(GameMapType.MidnightMansion, MapDifficultyTier.Advanced, "GameElements.Map.MidnightMansion"),
        new(GameMapType.SunkenColumns, MapDifficultyTier.Advanced, "GameElements.Map.SunkenColumns"),
        new(GameMapType.XFactor, MapDifficultyTier.Advanced, "GameElements.Map.XFactor"),
        new(GameMapType.Mesa, MapDifficultyTier.Advanced, "GameElements.Map.Mesa"),
        new(GameMapType.Geared, MapDifficultyTier.Advanced, "GameElements.Map.Geared"),
        new(GameMapType.Spillway, MapDifficultyTier.Advanced, "GameElements.Map.Spillway"),
        new(GameMapType.Cargo, MapDifficultyTier.Advanced, "GameElements.Map.Cargo"),
        new(GameMapType.PatsPond, MapDifficultyTier.Advanced, "GameElements.Map.PatsPond"),
        new(GameMapType.Peninsula, MapDifficultyTier.Advanced, "GameElements.Map.Peninsula"),
        new(GameMapType.HighFinance, MapDifficultyTier.Advanced, "GameElements.Map.HighFinance"),
        new(GameMapType.AnotherBrick, MapDifficultyTier.Advanced, "GameElements.Map.AnotherBrick"),
        new(GameMapType.OffTheCoast, MapDifficultyTier.Advanced, "GameElements.Map.OffTheCoast"),
        new(GameMapType.Cornfield, MapDifficultyTier.Advanced, "GameElements.Map.Cornfield"),
        new(GameMapType.Underground, MapDifficultyTier.Advanced, "GameElements.Map.Underground"),
        new(GameMapType.LastResort, MapDifficultyTier.Advanced, "GameElements.Map.LastResort"),
        new(GameMapType.EnchantedGlade, MapDifficultyTier.Advanced, "GameElements.Map.EnchantedGlade"),
        new(GameMapType.SunsetGulch, MapDifficultyTier.Advanced, "GameElements.Map.SunsetGulch"),
        new(GameMapType.PartyParade, MapDifficultyTier.Advanced, "GameElements.Map.PartyParade"),
        new(GameMapType.MushroomGortto, MapDifficultyTier.Advanced, "GameElements.Map.MushroomGortto"),

        // Expert
        new(GameMapType.GlacialTrail, MapDifficultyTier.Expert, "GameElements.Map.GlacialTrail"),
        new(GameMapType.DarkDungeon, MapDifficultyTier.Expert, "GameElements.Map.DarkDungeon"),
        new(GameMapType.Sanctuary, MapDifficultyTier.Expert, "GameElements.Map.Sanctuary"),
        new(GameMapType.Ravine, MapDifficultyTier.Expert, "GameElements.Map.Ravine"),
        new(GameMapType.FloodedValley, MapDifficultyTier.Expert, "GameElements.Map.FloodedValley"),
        new(GameMapType.Infernal, MapDifficultyTier.Expert, "GameElements.Map.Infernal"),
        new(GameMapType.BloodyPuddles, MapDifficultyTier.Expert, "GameElements.Map.BloodyPuddles"),
        new(GameMapType.Workshop, MapDifficultyTier.Expert, "GameElements.Map.Workshop"),
        new(GameMapType.Quad, MapDifficultyTier.Expert, "GameElements.Map.Quad"),
        new(GameMapType.DarkCastle, MapDifficultyTier.Expert, "GameElements.Map.DarkCastle"),
        new(GameMapType.MuddyPuddles, MapDifficultyTier.Expert, "GameElements.Map.MuddyPuddles"),
        new(GameMapType.Ouch, MapDifficultyTier.Expert, "GameElements.Map.Ouch"),
        new(GameMapType.TrickyTracks, MapDifficultyTier.Expert, "GameElements.Map.TrickyTracks")
    ];

    public static string GetStageDifficultyDisplayName(StageDifficulty difficulty)
    {
        return difficulty switch
        {
            StageDifficulty.Easy => LocalizationService.Instance.T("GameElements.StageDifficulty.Easy"),
            StageDifficulty.Medium => LocalizationService.Instance.T("GameElements.StageDifficulty.Medium"),
            StageDifficulty.Hard => LocalizationService.Instance.T("GameElements.StageDifficulty.Hard"),
            _ => difficulty.ToString()
        };
    }

    public static string GetStageModeDisplayName(StageMode mode)
    {
        return mode switch
        {
            StageMode.Standard => LocalizationService.Instance.T("GameElements.StageMode.Standard"),
            StageMode.PrimaryOnly => LocalizationService.Instance.T("GameElements.StageMode.PrimaryOnly"),
            StageMode.Deflation => LocalizationService.Instance.T("GameElements.StageMode.Deflation"),
            StageMode.MilitaryOnly => LocalizationService.Instance.T("GameElements.StageMode.MilitaryOnly"),
            StageMode.Apopalypse => LocalizationService.Instance.T("GameElements.StageMode.Apopalypse"),
            StageMode.Reverse => LocalizationService.Instance.T("GameElements.StageMode.Reverse"),
            StageMode.MagicOnly => LocalizationService.Instance.T("GameElements.StageMode.MagicOnly"),
            StageMode.DoubleHpMoabs => LocalizationService.Instance.T("GameElements.StageMode.DoubleHpMoabs"),
            StageMode.HalfCash => LocalizationService.Instance.T("GameElements.StageMode.HalfCash"),
            StageMode.AlternateBloonsRounds => LocalizationService.Instance.T("GameElements.StageMode.AlternateBloonsRounds"),
            StageMode.Impoppable => LocalizationService.Instance.T("GameElements.StageMode.Impoppable"),
            StageMode.CHIMPS => LocalizationService.Instance.T("GameElements.StageMode.CHIMPS"),
            _ => mode.ToString()
        };
    }
}
