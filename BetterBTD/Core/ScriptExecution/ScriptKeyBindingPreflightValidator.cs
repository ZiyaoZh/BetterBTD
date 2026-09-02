using BetterBTD.Core.Config;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;

namespace BetterBTD.Core.ScriptExecution;

public sealed record ScriptKeyBindingPreflightIssue(
    string ConfigPropertyPath,
    string LocalizationKey,
    int FirstStepIndex);

public static class ScriptKeyBindingPreflightValidator
{
    public static IReadOnlyList<ScriptKeyBindingPreflightIssue> Validate(
        ScriptTaskFlow taskFlow,
        KeyBindingsConfig keyBindings,
        int startStepIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(taskFlow);
        ArgumentNullException.ThrowIfNull(keyBindings);

        var issues = new Dictionary<string, ScriptKeyBindingPreflightIssue>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in taskFlow.Steps.Where(step => step.Index >= Math.Max(0, startStepIndex)))
        {
            foreach (var requirement in GetRequirements(step, taskFlow, keyBindings))
            {
                if (requirement.Binding.Key is not KeyId.None and not KeyId.Unknown ||
                    issues.ContainsKey(requirement.ConfigPropertyPath))
                {
                    continue;
                }

                issues.Add(
                    requirement.ConfigPropertyPath,
                    new ScriptKeyBindingPreflightIssue(
                        requirement.ConfigPropertyPath,
                        requirement.LocalizationKey,
                        step.Index));
            }
        }

        return issues.Values.OrderBy(issue => issue.FirstStepIndex).ToArray();
    }

    private static IEnumerable<Requirement> GetRequirements(
        ScriptTaskFlowStep step,
        ScriptTaskFlow taskFlow,
        KeyBindingsConfig keyBindings)
    {
        var instruction = step.Instruction;
        switch (step.CommandType)
        {
            case ScriptCommandType.PlaceMonkey:
                var selectionCode = ScriptEditorInstructionService.NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower);
                if (ScriptEditorInstructionService.IsHeroSelectionCode(selectionCode))
                {
                    yield return General(keyBindings.General.Hero, nameof(GeneralActionBindings.Hero));
                }
                else if (ScriptEditorInstructionService.TryParseTowerSelection(selectionCode, out var towerType))
                {
                    var propertyName = towerType.ToString();
                    var binding = GetBinding(keyBindings.TowerPlacement, propertyName);
                    if (binding is not null)
                    {
                        yield return new Requirement(binding, $"{nameof(KeyBindingsConfig.TowerPlacement)}.{propertyName}", $"Settings.KeyBindings.Item.{propertyName}");
                    }
                }

                break;
            case ScriptCommandType.UpgradeMonkey:
                if (Enum.TryParse<UpgradePathType>(instruction.UpgradePath, true, out var upgradePath))
                {
                    var propertyName = upgradePath switch
                    {
                        UpgradePathType.Top => nameof(GeneralActionBindings.UpgradePath1),
                        UpgradePathType.Middle => nameof(GeneralActionBindings.UpgradePath2),
                        UpgradePathType.Bottom => nameof(GeneralActionBindings.UpgradePath3),
                        _ => string.Empty
                    };
                    if (!string.IsNullOrEmpty(propertyName))
                    {
                        yield return General(GetBinding(keyBindings.General, propertyName)!, propertyName);
                    }
                }

                if (IsHeroTarget(instruction, taskFlow))
                {
                    yield return General(keyBindings.General.Hero, nameof(GeneralActionBindings.Hero));
                }

                break;
            case ScriptCommandType.SwitchMonkeyTarget:
                if (Enum.TryParse<SwitchDirectionType>(instruction.SwitchDirection, true, out var direction))
                {
                    var propertyName = direction == SwitchDirectionType.Right
                        ? nameof(GeneralActionBindings.ChangeTargeting)
                        : nameof(GeneralActionBindings.ReverseChangeTargeting);
                    yield return General(GetBinding(keyBindings.General, propertyName)!, propertyName);
                }

                break;
            case ScriptCommandType.SetMonkeyAbility:
                if (Enum.TryParse<MonkeyAbilityType>(instruction.SelectedAbility, true, out var monkeyAbility))
                {
                    var propertyName = monkeyAbility == MonkeyAbilityType.Ability1
                        ? nameof(GeneralActionBindings.MonkeySpecial)
                        : nameof(GeneralActionBindings.MonkeySpecial2);
                    var localizationName = monkeyAbility == MonkeyAbilityType.Ability1 ? "MonkeySpecial1" : "MonkeySpecial2";
                    yield return General(GetBinding(keyBindings.General, propertyName)!, propertyName, localizationName);
                }

                break;
            case ScriptCommandType.SellMonkey:
                yield return General(keyBindings.General.Sell, nameof(GeneralActionBindings.Sell));
                break;
            case ScriptCommandType.PlaceHeroInventory:
                yield return General(keyBindings.General.Hero, nameof(GeneralActionBindings.Hero));
                if (Enum.TryParse<InventoryType>(instruction.SelectedInventoryItem, true, out var inventoryType))
                {
                    var propertyName = inventoryType.ToString();
                    var binding = GetBinding(keyBindings.HeroInventory, propertyName);
                    if (binding is not null)
                    {
                        yield return new Requirement(binding, $"{nameof(KeyBindingsConfig.HeroInventory)}.{propertyName}", $"Settings.KeyBindings.Item.{propertyName}");
                    }
                }

                break;
            case ScriptCommandType.ActivateAbility:
                if (Enum.TryParse<ActivatedAbilityType>(instruction.SelectedActivatedAbility, true, out var abilityType))
                {
                    var propertyName = abilityType.ToString();
                    var binding = GetBinding(keyBindings.Abilities, propertyName);
                    if (binding is not null)
                    {
                        yield return new Requirement(binding, $"{nameof(KeyBindingsConfig.Abilities)}.{propertyName}", $"Settings.KeyBindings.Item.{propertyName}");
                    }
                }

                break;
            case ScriptCommandType.NextRound:
                var nextRoundProperty = instruction.NextRoundAction switch
                {
                    "PlayFastForward" => nameof(GeneralActionBindings.PlayFastForward),
                    "SendNextRound" => nameof(GeneralActionBindings.SendNextRound),
                    _ => string.Empty
                };
                if (!string.IsNullOrEmpty(nextRoundProperty))
                {
                    yield return General(GetBinding(keyBindings.General, nextRoundProperty)!, nextRoundProperty);
                }

                break;
        }
    }

    private static bool IsHeroTarget(ScriptInstructionDocument instruction, ScriptTaskFlow taskFlow)
    {
        if (ScriptEditorInstructionService.IsHeroSelectionCode(instruction.TargetMonkeyObjectId))
        {
            return true;
        }

        return taskFlow.MonkeyObjectsByBindingId.TryGetValue(instruction.TargetMonkeyBindingId, out var monkey) &&
               (ScriptEditorInstructionService.IsHeroSelectionCode(monkey.SelectionCode) ||
                ScriptEditorInstructionService.IsHeroSelectionCode(monkey.ObjectId));
    }

    private static HotkeyBinding? GetBinding(object owner, string propertyName)
    {
        return owner.GetType().GetProperty(propertyName)?.GetValue(owner) as HotkeyBinding;
    }

    private static Requirement General(HotkeyBinding binding, string propertyName, string? localizationName = null)
    {
        return new Requirement(
            binding,
            $"{nameof(KeyBindingsConfig.General)}.{propertyName}",
            $"Settings.KeyBindings.Item.{localizationName ?? propertyName}");
    }

    private sealed record Requirement(HotkeyBinding Binding, string ConfigPropertyPath, string LocalizationKey);
}
