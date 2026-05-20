using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;

namespace BetterBTD.Services.MyScripts;

public sealed class ManagedScriptSlotCatalogService
{
    private static readonly Lazy<ManagedScriptSlotCatalogService> InstanceHolder =
        new(() => new ManagedScriptSlotCatalogService());

    private readonly IReadOnlyList<ManagedScriptSlotDefinition> _slots;
    private readonly IReadOnlyDictionary<string, ManagedScriptSlotDefinition> _slotsById;

    private ManagedScriptSlotCatalogService()
    {
        var slots = BuildSlots();
        _slots = slots;
        _slotsById = slots.ToDictionary(x => x.SlotId, StringComparer.OrdinalIgnoreCase);
    }

    public static ManagedScriptSlotCatalogService Instance => InstanceHolder.Value;

    public IReadOnlyList<ManagedScriptSlotDefinition> GetAll()
    {
        return _slots;
    }

    public IReadOnlyList<ManagedScriptSlotDefinition> GetByTaskKind(AutoTaskKind kind)
    {
        return _slots.Where(x => x.TaskKind == kind).ToList();
    }

    public bool TryGetById(string? slotId, out ManagedScriptSlotDefinition slot)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            slot = null!;
            return false;
        }

        return _slotsById.TryGetValue(slotId.Trim(), out slot!);
    }

    public string BuildBlackBorderSlotId(StageEntryTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
            target.Map,
            target.Difficulty,
            target.Mode);
    }

    private static IReadOnlyList<ManagedScriptSlotDefinition> BuildSlots()
    {
        var slots = new List<ManagedScriptSlotDefinition>();
        slots.AddRange(BuildCustomSlots());
        slots.AddRange(BuildCollectionSlots());
        slots.AddRange(BuildBlackBorderSlots());
        slots.AddRange(BuildRaceSlots());
        return slots;
    }

    private static IEnumerable<ManagedScriptSlotDefinition> BuildCustomSlots()
    {
        yield return new ManagedScriptSlotDefinition
        {
            SlotId = ManagedScriptSlotIdFactory.CreateCustomDefaultSlotId(),
            TaskKind = AutoTaskKind.Custom,
            GroupName = "Custom",
            DisplayName = "Default Script"
        };
    }

    private static IEnumerable<ManagedScriptSlotDefinition> BuildCollectionSlots()
    {
        for (var modeIndex = 1; modeIndex <= 3; modeIndex++)
        {
            for (var scriptIndex = 1; scriptIndex <= 13; scriptIndex++)
            {
                yield return new ManagedScriptSlotDefinition
                {
                    SlotId = ManagedScriptSlotIdFactory.CreateCollectionSlotId(modeIndex, scriptIndex),
                    TaskKind = AutoTaskKind.Collection,
                    GroupName = $"Mode {modeIndex}",
                    DisplayName = $"Stage Script {scriptIndex:00}",
                    Qualifiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["modeIndex"] = modeIndex.ToString(),
                        ["scriptIndex"] = scriptIndex.ToString()
                    },
                    SuggestedTags = ["collection"],
                    IsPlaceholder = true
                };
            }
        }
    }

    private static IEnumerable<ManagedScriptSlotDefinition> BuildBlackBorderSlots()
    {
        foreach (var map in GameElementCatalog.Maps)
        {
            foreach (var difficulty in GetBlackBorderDifficulties())
            {
                foreach (var mode in GetModesForDifficulty(difficulty))
                {
                    yield return new ManagedScriptSlotDefinition
                    {
                        SlotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(map.Type, difficulty, mode),
                        TaskKind = AutoTaskKind.BlackBorder,
                        GroupName = $"{GameElementCatalog.GetMapDisplayName(map.Type)} / {GameElementCatalog.GetStageDifficultyDisplayName(difficulty)}",
                        DisplayName = GameElementCatalog.GetStageModeDisplayName(mode),
                        StageTarget = new StageEntryTarget
                        {
                            Map = map.Type,
                            Difficulty = difficulty,
                            Mode = mode
                        },
                        SuggestedTags = ["black-border"]
                    };
                }
            }
        }
    }

    private static IEnumerable<ManagedScriptSlotDefinition> BuildRaceSlots()
    {
        yield return new ManagedScriptSlotDefinition
        {
            SlotId = ManagedScriptSlotIdFactory.CreateRaceCurrentSlotId(),
            TaskKind = AutoTaskKind.Race,
            GroupName = "Race",
            DisplayName = "Current Event",
            SuggestedTags = ["race"],
            IsPlaceholder = true
        };
    }

    private static IReadOnlyList<StageDifficulty> GetBlackBorderDifficulties()
    {
        return [StageDifficulty.Easy, StageDifficulty.Medium, StageDifficulty.Hard];
    }

    private static IReadOnlyList<StageMode> GetModesForDifficulty(StageDifficulty difficulty)
    {
        return difficulty switch
        {
            StageDifficulty.Easy =>
            [
                StageMode.Standard,
                StageMode.PrimaryOnly,
                StageMode.Deflation
            ],
            StageDifficulty.Medium =>
            [
                StageMode.Standard,
                StageMode.MilitaryOnly,
                StageMode.Apopalypse,
                StageMode.Reverse
            ],
            StageDifficulty.Hard =>
            [
                StageMode.Standard,
                StageMode.MagicOnly,
                StageMode.DoubleHpMoabs,
                StageMode.HalfCash,
                StageMode.AlternateBloonsRounds,
                StageMode.Impoppable,
                StageMode.CHIMPS
            ],
            _ => [StageMode.Standard]
        };
    }
}
