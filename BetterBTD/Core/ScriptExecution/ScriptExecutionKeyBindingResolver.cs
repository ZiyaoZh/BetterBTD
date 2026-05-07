using BetterBTD.Core.Config;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

namespace BetterBTD.Core.ScriptExecution;

public static class ScriptExecutionKeyBindingResolver
{
    public static HotkeyBinding ResolvePlacementHotkey(string selectionCode)
    {
        var normalizedSelectionCode = ScriptEditorInstructionService.NormalizePlaceSelectionCode(selectionCode);
        var keyBindings = ConfigurationService.Instance.Current.KeyBindings;

        if (ScriptEditorInstructionService.TryParseTowerSelection(normalizedSelectionCode, out var towerType))
        {
            return EnsureBound(
                towerType switch
                {
                    MonkeyTowerType.DartMonkey => keyBindings.TowerPlacement.DartMonkey,
                    MonkeyTowerType.BoomerangMonkey => keyBindings.TowerPlacement.BoomerangMonkey,
                    MonkeyTowerType.BombShooter => keyBindings.TowerPlacement.BombShooter,
                    MonkeyTowerType.TackShooter => keyBindings.TowerPlacement.TackShooter,
                    MonkeyTowerType.IceMonkey => keyBindings.TowerPlacement.IceMonkey,
                    MonkeyTowerType.GlueGunner => keyBindings.TowerPlacement.GlueGunner,
                    MonkeyTowerType.Desperado => keyBindings.TowerPlacement.Desperado,
                    MonkeyTowerType.SniperMonkey => keyBindings.TowerPlacement.SniperMonkey,
                    MonkeyTowerType.MonkeySub => keyBindings.TowerPlacement.MonkeySub,
                    MonkeyTowerType.MonkeyBuccaneer => keyBindings.TowerPlacement.MonkeyBuccaneer,
                    MonkeyTowerType.MonkeyAce => keyBindings.TowerPlacement.MonkeyAce,
                    MonkeyTowerType.HeliPilot => keyBindings.TowerPlacement.HeliPilot,
                    MonkeyTowerType.MortarMonkey => keyBindings.TowerPlacement.MortarMonkey,
                    MonkeyTowerType.DartlingGunner => keyBindings.TowerPlacement.DartlingGunner,
                    MonkeyTowerType.WizardMonkey => keyBindings.TowerPlacement.WizardMonkey,
                    MonkeyTowerType.SuperMonkey => keyBindings.TowerPlacement.SuperMonkey,
                    MonkeyTowerType.NinjaMonkey => keyBindings.TowerPlacement.NinjaMonkey,
                    MonkeyTowerType.Alchemist => keyBindings.TowerPlacement.Alchemist,
                    MonkeyTowerType.Druid => keyBindings.TowerPlacement.Druid,
                    MonkeyTowerType.MerMonkey => keyBindings.TowerPlacement.MerMonkey,
                    MonkeyTowerType.BananaFarm => keyBindings.TowerPlacement.BananaFarm,
                    MonkeyTowerType.SpikeFactory => keyBindings.TowerPlacement.SpikeFactory,
                    MonkeyTowerType.MonkeyVillage => keyBindings.TowerPlacement.MonkeyVillage,
                    MonkeyTowerType.EngineerMonkey => keyBindings.TowerPlacement.EngineerMonkey,
                    MonkeyTowerType.BeastHandler => keyBindings.TowerPlacement.BeastHandler,
                    _ => throw new InvalidOperationException($"Unsupported tower placement selection '{towerType}'.")
                },
                $"placement hotkey for '{towerType}'");
        }

        if (ScriptEditorInstructionService.TryParseHeroSelection(normalizedSelectionCode, out _))
        {
            return EnsureBound(keyBindings.General.Hero, "hero placement hotkey");
        }

        throw new InvalidOperationException($"Unsupported placement selection code '{selectionCode}'.");
    }

    public static HotkeyBinding ResolveUpgradeHotkey(UpgradePathType upgradePath)
    {
        var generalBindings = ConfigurationService.Instance.Current.KeyBindings.General;

        return EnsureBound(
            upgradePath switch
            {
                UpgradePathType.Top => generalBindings.UpgradePath1,
                UpgradePathType.Middle => generalBindings.UpgradePath2,
                UpgradePathType.Bottom => generalBindings.UpgradePath3,
                _ => throw new InvalidOperationException($"Unsupported upgrade path '{upgradePath}'.")
            },
            $"upgrade hotkey for '{upgradePath}'");
    }

    private static HotkeyBinding EnsureBound(HotkeyBinding hotkeyBinding, string description)
    {
        ArgumentNullException.ThrowIfNull(hotkeyBinding);

        if (hotkeyBinding.Key is KeyId.None or KeyId.Unknown)
        {
            throw new InvalidOperationException($"The {description} is not configured.");
        }

        return hotkeyBinding;
    }
}
