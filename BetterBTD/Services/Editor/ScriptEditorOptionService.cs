using BetterBTD.Models;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services.Editor;

public sealed class ScriptEditorOptionService
{
    private static readonly Lazy<ScriptEditorOptionService> InstanceHolder = new(() => new ScriptEditorOptionService());

    private ScriptEditorOptionService()
    {
    }

    public static ScriptEditorOptionService Instance => InstanceHolder.Value;

    public ScriptEditorParameterOptions CreateParameterOptions(LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return new ScriptEditorParameterOptions
        {
            UpgradePathOptions =
            [
                new LanguageOption { Code = UpgradePathType.Top.ToString(), DisplayName = localizationService.T("Editor.Property.UpgradePath.Top") },
                new LanguageOption { Code = UpgradePathType.Middle.ToString(), DisplayName = localizationService.T("Editor.Property.UpgradePath.Middle") },
                new LanguageOption { Code = UpgradePathType.Bottom.ToString(), DisplayName = localizationService.T("Editor.Property.UpgradePath.Bottom") }
            ],
            SwitchDirectionOptions =
            [
                new LanguageOption { Code = SwitchDirectionType.Right.ToString(), DisplayName = localizationService.T("Editor.Property.SwitchDirection.Right") },
                new LanguageOption { Code = SwitchDirectionType.Left.ToString(), DisplayName = localizationService.T("Editor.Property.SwitchDirection.Left") }
            ],
            MonkeyAbilityOptions =
            [
                new LanguageOption { Code = MonkeyAbilityType.Ability1.ToString(), DisplayName = localizationService.T("Editor.Property.Ability1") },
                new LanguageOption { Code = MonkeyAbilityType.Ability2.ToString(), DisplayName = localizationService.T("Editor.Property.Ability2") }
            ],
            InventoryOptions = GameElementCatalog.InventoryItems
                .Select(inventory => new LanguageOption
                {
                    Code = inventory.Type.ToString(),
                    DisplayName = localizationService.T(inventory.NameKey)
                })
                .ToList(),
            ActivatedAbilityOptions = GameElementCatalog.ActivatedAbilities
                .Select(ability => new LanguageOption
                {
                    Code = ability.Type.ToString(),
                    DisplayName = localizationService.T(ability.NameKey)
                })
                .ToList(),
            NextRoundActionOptions = GameElementCatalog.NextRoundActions
                .Select(action => new LanguageOption
                {
                    Code = action,
                    DisplayName = GameElementCatalog.GetNextRoundActionDisplayName(action)
                })
                .ToList(),
            WaitModeOptions =
            [
                new LanguageOption { Code = WaitModeType.Time.ToString(), DisplayName = localizationService.T("Editor.Property.WaitMode.Time") },
                new LanguageOption { Code = WaitModeType.Gold.ToString(), DisplayName = localizationService.T("Editor.Property.WaitMode.Gold") },
                new LanguageOption { Code = WaitModeType.Round.ToString(), DisplayName = localizationService.T("Editor.Property.WaitMode.Round") },
                new LanguageOption { Code = WaitModeType.CoordinateColor.ToString(), DisplayName = localizationService.T("Editor.Property.WaitMode.CoordinateColor") }
            ]
        };
    }

    public ScriptEditorMetadataOptions CreateMetadataOptions(LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return new ScriptEditorMetadataOptions
        {
            DifficultyOptions =
            [
                new LanguageOption { Code = StageDifficulty.Easy.ToString(), DisplayName = localizationService.T("GameElements.StageDifficulty.Easy") },
                new LanguageOption { Code = StageDifficulty.Medium.ToString(), DisplayName = localizationService.T("GameElements.StageDifficulty.Medium") },
                new LanguageOption { Code = StageDifficulty.Hard.ToString(), DisplayName = localizationService.T("GameElements.StageDifficulty.Hard") }
            ],
            ModeOptions = new[]
            {
                StageMode.Standard,
                StageMode.PrimaryOnly,
                StageMode.Deflation,
                StageMode.MilitaryOnly,
                StageMode.Apopalypse,
                StageMode.Reverse,
                StageMode.MagicOnly,
                StageMode.DoubleHpMoabs,
                StageMode.HalfCash,
                StageMode.AlternateBloonsRounds,
                StageMode.Impoppable,
                StageMode.CHIMPS
            }
                .Select(mode => new LanguageOption
                {
                    Code = mode.ToString(),
                    DisplayName = localizationService.T($"GameElements.StageMode.{mode}")
                })
                .ToList(),
            HeroOptions = GameElementCatalog.Heroes
                .Select(hero => new LanguageOption
                {
                    Code = hero.Type.ToString(),
                    DisplayName = localizationService.T(hero.NameKey)
                })
                .ToList()
        };
    }
}

public sealed class ScriptEditorParameterOptions
{
    public required IReadOnlyList<LanguageOption> UpgradePathOptions { get; init; }
    public required IReadOnlyList<LanguageOption> SwitchDirectionOptions { get; init; }
    public required IReadOnlyList<LanguageOption> MonkeyAbilityOptions { get; init; }
    public required IReadOnlyList<LanguageOption> InventoryOptions { get; init; }
    public required IReadOnlyList<LanguageOption> ActivatedAbilityOptions { get; init; }
    public required IReadOnlyList<LanguageOption> NextRoundActionOptions { get; init; }
    public required IReadOnlyList<LanguageOption> WaitModeOptions { get; init; }
}

public sealed class ScriptEditorMetadataOptions
{
    public required IReadOnlyList<LanguageOption> DifficultyOptions { get; init; }
    public required IReadOnlyList<LanguageOption> ModeOptions { get; init; }
    public required IReadOnlyList<LanguageOption> HeroOptions { get; init; }
}

