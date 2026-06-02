using BetterBTD.Models.GameElements;

namespace BetterBTD.Services.Tools;

internal sealed record ParagonMonkeyDefinition(MonkeyTowerType TowerType, int BaseCost);

internal static class ParagonToolCatalog
{
    private static readonly ParagonMonkeyDefinition[] MonkeyDefinitions =
    [
        new(MonkeyTowerType.DartMonkey, 150000),
        new(MonkeyTowerType.BoomerangMonkey, 375000),
        new(MonkeyTowerType.BombShooter, 600000),
        new(MonkeyTowerType.TackShooter, 200000),
        new(MonkeyTowerType.IceMonkey, 300000),
        new(MonkeyTowerType.MonkeySub, 400000),
        new(MonkeyTowerType.MonkeyBuccaneer, 550000),
        new(MonkeyTowerType.MonkeyAce, 900000),
        new(MonkeyTowerType.WizardMonkey, 800000),
        new(MonkeyTowerType.NinjaMonkey, 500000),
        new(MonkeyTowerType.Druid, 475000),
        new(MonkeyTowerType.SpikeFactory, 750000),
        new(MonkeyTowerType.EngineerMonkey, 650000)
    ];

    private static readonly Dictionary<string, ParagonMonkeyDefinition> DefinitionsByCode = MonkeyDefinitions
        .ToDictionary(definition => definition.TowerType.ToString(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ParagonMonkeyDefinition> EligibleMonkeys { get; } = MonkeyDefinitions;

    public static int GetBaseCostOrDefault(string? monkeyCode)
    {
        if (TryGetDefinition(monkeyCode, out var definition))
        {
            return definition.BaseCost;
        }

        return EligibleMonkeys[0].BaseCost;
    }

    public static bool TryGetDefinition(string? monkeyCode, out ParagonMonkeyDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(monkeyCode) &&
            DefinitionsByCode.TryGetValue(monkeyCode, out definition!))
        {
            return true;
        }

        definition = EligibleMonkeys[0];
        return false;
    }
}
