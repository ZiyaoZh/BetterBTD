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

        if (ScriptEditorInstructionService.IsHeroSelectionCode(normalizedSelectionCode))
        {
            return EnsureBound(keyBindings.General.Hero, "hero placement hotkey");
        }

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
                    MonkeyTowerType.Skywarden => keyBindings.TowerPlacement.Skywarden,
                    MonkeyTowerType.BananaFarm => keyBindings.TowerPlacement.BananaFarm,
                    MonkeyTowerType.SpikeFactory => keyBindings.TowerPlacement.SpikeFactory,
                    MonkeyTowerType.MonkeyVillage => keyBindings.TowerPlacement.MonkeyVillage,
                    MonkeyTowerType.EngineerMonkey => keyBindings.TowerPlacement.EngineerMonkey,
                    MonkeyTowerType.BeastHandler => keyBindings.TowerPlacement.BeastHandler,
                    _ => throw new InvalidOperationException($"Unsupported tower placement selection '{towerType}'.")
                },
                $"placement hotkey for '{towerType}'");
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

    public static HotkeyBinding ResolveSwitchTargetHotkey(SwitchDirectionType switchDirection)
    {
        var generalBindings = ConfigurationService.Instance.Current.KeyBindings.General;

        return EnsureBound(
            switchDirection switch
            {
                SwitchDirectionType.Right => generalBindings.ChangeTargeting,
                SwitchDirectionType.Left => generalBindings.ReverseChangeTargeting,
                _ => throw new InvalidOperationException($"Unsupported switch direction '{switchDirection}'.")
            },
            $"target switching hotkey for '{switchDirection}'");
    }

    public static HotkeyBinding ResolveMonkeyAbilityHotkey(MonkeyAbilityType abilityType)
    {
        var generalBindings = ConfigurationService.Instance.Current.KeyBindings.General;

        return EnsureBound(
            abilityType switch
            {
                MonkeyAbilityType.Ability1 => generalBindings.MonkeySpecial,
                MonkeyAbilityType.Ability2 => generalBindings.MonkeySpecial2,
                _ => throw new InvalidOperationException($"Unsupported monkey ability '{abilityType}'.")
            },
            $"monkey ability hotkey for '{abilityType}'");
    }

    public static HotkeyBinding ResolveHeroHotkey()
    {
        return EnsureBound(
            ConfigurationService.Instance.Current.KeyBindings.General.Hero,
            "hero hotkey");
    }

    public static HotkeyBinding ResolveSellHotkey()
    {
        return EnsureBound(
            ConfigurationService.Instance.Current.KeyBindings.General.Sell,
            "sell hotkey");
    }

    public static HotkeyBinding ResolveHeroInventoryHotkey(InventoryType inventoryType)
    {
        var inventoryBindings = ConfigurationService.Instance.Current.KeyBindings.HeroInventory;

        return EnsureBound(
            inventoryType switch
            {
                InventoryType.Inventory1 => inventoryBindings.Inventory1,
                InventoryType.Inventory2 => inventoryBindings.Inventory2,
                InventoryType.Inventory3 => inventoryBindings.Inventory3,
                InventoryType.Inventory4 => inventoryBindings.Inventory4,
                InventoryType.Inventory5 => inventoryBindings.Inventory5,
                InventoryType.Inventory6 => inventoryBindings.Inventory6,
                InventoryType.Inventory7 => inventoryBindings.Inventory7,
                InventoryType.Inventory8 => inventoryBindings.Inventory8,
                InventoryType.Inventory9 => inventoryBindings.Inventory9,
                InventoryType.Inventory10 => inventoryBindings.Inventory10,
                InventoryType.Inventory11 => inventoryBindings.Inventory11,
                InventoryType.Inventory12 => inventoryBindings.Inventory12,
                InventoryType.Inventory13 => inventoryBindings.Inventory13,
                InventoryType.Inventory14 => inventoryBindings.Inventory14,
                InventoryType.Inventory15 => inventoryBindings.Inventory15,
                InventoryType.Inventory16 => inventoryBindings.Inventory16,
                _ => throw new InvalidOperationException($"Unsupported hero inventory '{inventoryType}'.")
            },
            $"hero inventory hotkey for '{inventoryType}'");
    }

    public static HotkeyBinding ResolveActivatedAbilityHotkey(ActivatedAbilityType abilityType)
    {
        var abilityBindings = ConfigurationService.Instance.Current.KeyBindings.Abilities;

        return EnsureBound(
            abilityType switch
            {
                ActivatedAbilityType.ActivatedAbility1 => abilityBindings.ActivatedAbility1,
                ActivatedAbilityType.ActivatedAbility2 => abilityBindings.ActivatedAbility2,
                ActivatedAbilityType.ActivatedAbility3 => abilityBindings.ActivatedAbility3,
                ActivatedAbilityType.ActivatedAbility4 => abilityBindings.ActivatedAbility4,
                ActivatedAbilityType.ActivatedAbility5 => abilityBindings.ActivatedAbility5,
                ActivatedAbilityType.ActivatedAbility6 => abilityBindings.ActivatedAbility6,
                ActivatedAbilityType.ActivatedAbility7 => abilityBindings.ActivatedAbility7,
                ActivatedAbilityType.ActivatedAbility8 => abilityBindings.ActivatedAbility8,
                ActivatedAbilityType.ActivatedAbility9 => abilityBindings.ActivatedAbility9,
                ActivatedAbilityType.ActivatedAbility10 => abilityBindings.ActivatedAbility10,
                ActivatedAbilityType.ActivatedAbility11 => abilityBindings.ActivatedAbility11,
                ActivatedAbilityType.ActivatedAbility12 => abilityBindings.ActivatedAbility12,
                _ => throw new InvalidOperationException($"Unsupported activated ability '{abilityType}'.")
            },
            $"activated ability hotkey for '{abilityType}'");
    }

    public static HotkeyBinding ResolveNextRoundHotkey(string nextRoundAction)
    {
        var generalBindings = ConfigurationService.Instance.Current.KeyBindings.General;

        return EnsureBound(
            nextRoundAction switch
            {
                "PlayFastForward" => generalBindings.PlayFastForward,
                "SendNextRound" => generalBindings.SendNextRound,
                _ => throw new InvalidOperationException($"Unsupported next round action '{nextRoundAction}'.")
            },
            $"next round hotkey for '{nextRoundAction}'");
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
