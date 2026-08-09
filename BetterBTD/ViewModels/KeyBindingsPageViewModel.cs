using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using BetterBTD.Core.Config;
using BetterBTD.Models;
using BetterBTD.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterBTD.ViewModels;

public class KeyBindingsSettingsPageViewModel : ObservableObject
{
    private readonly ConfigurationService _configurationService;
    private readonly LocalizationService _localizationService;
    private readonly KeyBindingsConfig _config;

    public KeyBindingsSettingsPageViewModel()
    {
        _configurationService = ConfigurationService.Instance;
        _localizationService = LocalizationService.Instance;
        _config = _configurationService.Current.KeyBindings;

        KeyBindingSettingModels = new();
        ResetDefaultKeyBindingsCommand = new RelayCommand(ResetDefaultKeyBindings);

        BuildKeyBindingsList();
        SubscribeToLeafNodes();

        _config.PropertyChanged += OnConfigPropertyChanged;
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    public KeyBindingsConfig Config => _config;

    public ObservableCollection<KeyBindingSettingItem> KeyBindingSettingModels { get; }

    public string WindowTitle => _localizationService.T("Settings.KeyBindings.WindowTitle");

    public string PageTitle => _localizationService.T("Settings.KeyBindings.WindowTitle");

    public string PageSubtitle => _localizationService.T("Settings.KeyBindings.PageSubtitle");

    public string ResetDefaultTitle => _localizationService.T("Settings.KeyBindings.ResetDefaultTitle");

    public string ResetDefaultSubtitle => _localizationService.T("Settings.KeyBindings.ResetDefaultSubtitle");

    public string ResetDefaultText => _localizationService.T("Settings.KeyBindings.ResetDefaultText");

    public string ActionHeaderText => _localizationService.T("Settings.KeyBindings.ActionHeader");

    public string KeyBindingHeaderText => _localizationService.T("Settings.KeyBindings.KeyBindingHeader");

    public IRelayCommand ResetDefaultKeyBindingsCommand { get; }

    private void BuildKeyBindingsList()
    {
        KeyBindingSettingModels.Clear();
        KeyBindingSettingModels.Add(CreateTowerPlacementGroup());
        KeyBindingSettingModels.Add(CreateAbilityGroup());
        KeyBindingSettingModels.Add(CreateHeroInventoryGroup());
        KeyBindingSettingModels.Add(CreateGeneralActionGroup());
    }

    private void ResetDefaultKeyBindings()
    {
        _config.TowerPlacement = new TowerPlacementBindings();
        _config.Abilities = new AbilityBindings();
        _config.HeroInventory = new HeroInventoryBindings();
        _config.General = new GeneralActionBindings();

        BuildKeyBindingsList();
        SubscribeToLeafNodes();
        _configurationService.Save(_configurationService.Current);
        OnPropertyChanged(nameof(KeyBindingSettingModels));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        BuildKeyBindingsList();
        SubscribeToLeafNodes();
        RaiseLocalizedProperties();
        OnPropertyChanged(nameof(KeyBindingSettingModels));
    }

    private KeyBindingSettingItem CreateTowerPlacementGroup()
    {
        var group = new KeyBindingSettingItem(_localizationService.T("Settings.KeyBindings.Group.TowerPlacement"));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.DartMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.DartMonkey)}", _config.TowerPlacement.DartMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.BoomerangMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.BoomerangMonkey)}", _config.TowerPlacement.BoomerangMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.BombShooter"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.BombShooter)}", _config.TowerPlacement.BombShooter));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.TackShooter"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.TackShooter)}", _config.TowerPlacement.TackShooter));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.IceMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.IceMonkey)}", _config.TowerPlacement.IceMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.GlueGunner"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.GlueGunner)}", _config.TowerPlacement.GlueGunner));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Desperado"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.Desperado)}", _config.TowerPlacement.Desperado));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.SniperMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.SniperMonkey)}", _config.TowerPlacement.SniperMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MonkeySub"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.MonkeySub)}", _config.TowerPlacement.MonkeySub));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MonkeyBuccaneer"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.MonkeyBuccaneer)}", _config.TowerPlacement.MonkeyBuccaneer));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MonkeyAce"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.MonkeyAce)}", _config.TowerPlacement.MonkeyAce));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.HeliPilot"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.HeliPilot)}", _config.TowerPlacement.HeliPilot));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MortarMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.MortarMonkey)}", _config.TowerPlacement.MortarMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.DartlingGunner"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.DartlingGunner)}", _config.TowerPlacement.DartlingGunner));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.WizardMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.WizardMonkey)}", _config.TowerPlacement.WizardMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.SuperMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.SuperMonkey)}", _config.TowerPlacement.SuperMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.NinjaMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.NinjaMonkey)}", _config.TowerPlacement.NinjaMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Alchemist"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.Alchemist)}", _config.TowerPlacement.Alchemist));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Druid"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.Druid)}", _config.TowerPlacement.Druid));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MerMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.MerMonkey)}", _config.TowerPlacement.MerMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Skywarden"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.Skywarden)}", _config.TowerPlacement.Skywarden));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.BananaFarm"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.BananaFarm)}", _config.TowerPlacement.BananaFarm));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.SpikeFactory"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.SpikeFactory)}", _config.TowerPlacement.SpikeFactory));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MonkeyVillage"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.MonkeyVillage)}", _config.TowerPlacement.MonkeyVillage));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.EngineerMonkey"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.EngineerMonkey)}", _config.TowerPlacement.EngineerMonkey));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.BeastHandler"), $"{nameof(KeyBindingsConfig.TowerPlacement)}.{nameof(TowerPlacementBindings.BeastHandler)}", _config.TowerPlacement.BeastHandler));
        return group;
    }

    private KeyBindingSettingItem CreateAbilityGroup()
    {
        var group = new KeyBindingSettingItem(_localizationService.T("Settings.KeyBindings.Group.Abilities"));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility1"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility1)}", _config.Abilities.ActivatedAbility1));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility2"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility2)}", _config.Abilities.ActivatedAbility2));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility3"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility3)}", _config.Abilities.ActivatedAbility3));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility4"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility4)}", _config.Abilities.ActivatedAbility4));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility5"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility5)}", _config.Abilities.ActivatedAbility5));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility6"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility6)}", _config.Abilities.ActivatedAbility6));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility7"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility7)}", _config.Abilities.ActivatedAbility7));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility8"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility8)}", _config.Abilities.ActivatedAbility8));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility9"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility9)}", _config.Abilities.ActivatedAbility9));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility10"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility10)}", _config.Abilities.ActivatedAbility10));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility11"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility11)}", _config.Abilities.ActivatedAbility11));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivatedAbility12"), $"{nameof(KeyBindingsConfig.Abilities)}.{nameof(AbilityBindings.ActivatedAbility12)}", _config.Abilities.ActivatedAbility12));
        return group;
    }

    private KeyBindingSettingItem CreateHeroInventoryGroup()
    {
        var group = new KeyBindingSettingItem(_localizationService.T("Settings.KeyBindings.Group.HeroInventory"));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory1"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory1)}", _config.HeroInventory.Inventory1));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory2"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory2)}", _config.HeroInventory.Inventory2));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory3"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory3)}", _config.HeroInventory.Inventory3));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory4"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory4)}", _config.HeroInventory.Inventory4));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory5"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory5)}", _config.HeroInventory.Inventory5));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory6"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory6)}", _config.HeroInventory.Inventory6));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory7"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory7)}", _config.HeroInventory.Inventory7));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory8"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory8)}", _config.HeroInventory.Inventory8));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory9"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory9)}", _config.HeroInventory.Inventory9));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory10"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory10)}", _config.HeroInventory.Inventory10));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory11"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory11)}", _config.HeroInventory.Inventory11));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory12"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory12)}", _config.HeroInventory.Inventory12));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory13"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory13)}", _config.HeroInventory.Inventory13));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory14"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory14)}", _config.HeroInventory.Inventory14));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory15"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory15)}", _config.HeroInventory.Inventory15));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Inventory16"), $"{nameof(KeyBindingsConfig.HeroInventory)}.{nameof(HeroInventoryBindings.Inventory16)}", _config.HeroInventory.Inventory16));
        return group;
    }

    private KeyBindingSettingItem CreateGeneralActionGroup()
    {
        var group = new KeyBindingSettingItem(_localizationService.T("Settings.KeyBindings.Group.General"));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Hero"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.Hero)}", _config.General.Hero));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.Sell"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.Sell)}", _config.General.Sell));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.UpgradePath1"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.UpgradePath1)}", _config.General.UpgradePath1));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.UpgradePath2"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.UpgradePath2)}", _config.General.UpgradePath2));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.UpgradePath3"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.UpgradePath3)}", _config.General.UpgradePath3));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ChangeTargeting"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.ChangeTargeting)}", _config.General.ChangeTargeting));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ReverseChangeTargeting"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.ReverseChangeTargeting)}", _config.General.ReverseChangeTargeting));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MonkeySpecial1"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.MonkeySpecial)}", _config.General.MonkeySpecial));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MonkeySpecial2"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.MonkeySpecial2)}", _config.General.MonkeySpecial2));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.PlayFastForward"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.PlayFastForward)}", _config.General.PlayFastForward));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.SendNextRound"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.SendNextRound)}", _config.General.SendNextRound));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.MergeBeast"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.MergeBeast)}", _config.General.MergeBeast));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.QuickRestart"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.QuickRestart)}", _config.General.QuickRestart));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivateSelectedTowerAbility1"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.ActivateSelectedTowerAbility1)}", _config.General.ActivateSelectedTowerAbility1));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivateSelectedTowerAbility2"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.ActivateSelectedTowerAbility2)}", _config.General.ActivateSelectedTowerAbility2));
        group.Children.Add(CreateLeaf(_localizationService.T("Settings.KeyBindings.Item.ActivateSelectedTowerAbility3"), $"{nameof(KeyBindingsConfig.General)}.{nameof(GeneralActionBindings.ActivateSelectedTowerAbility3)}", _config.General.ActivateSelectedTowerAbility3));
        return group;
    }

    private KeyBindingSettingItem CreateLeaf(string actionName, string configPropertyName, HotkeyBinding keyValue)
    {
        return new KeyBindingSettingItem(actionName, configPropertyName, CloneBinding(keyValue));
    }

    private static HotkeyBinding CloneBinding(HotkeyBinding binding)
    {
        return new HotkeyBinding
        {
            Modifiers = binding.Modifiers,
            Key = binding.Key
        };
    }

    private void SubscribeToLeafNodes()
    {
        foreach (var item in KeyBindingSettingModels.SelectMany(EnumerateItems))
        {
            if (!item.IsDirectory)
            {
                item.PropertyChanged -= OnBindingItemPropertyChanged;
                item.PropertyChanged += OnBindingItemPropertyChanged;
            }
        }
    }

    private static IEnumerable<KeyBindingSettingItem> EnumerateItems(KeyBindingSettingItem item)
    {
        yield return item;

        foreach (var child in item.Children)
        {
            foreach (var nested in EnumerateItems(child))
            {
                yield return nested;
            }
        }
    }

    private void OnBindingItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not KeyBindingSettingItem item || item.IsDirectory || e.PropertyName != nameof(KeyBindingSettingItem.KeyValue))
        {
            return;
        }

        SetConfigValue(item.ConfigPropertyName, item.KeyValue);
        _configurationService.Save(_configurationService.Current);
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyBindingsConfig.GlobalKeyMappingEnabled))
        {
            _configurationService.Save(_configurationService.Current);
        }
    }

    private void SetConfigValue(string propertyPath, HotkeyBinding value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);

        ArgumentNullException.ThrowIfNull(value);

        var parts = propertyPath.Split('.');
        object? current = _config;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var property = current?.GetType().GetProperty(parts[i], BindingFlags.Instance | BindingFlags.Public);
            current = property?.GetValue(current);
            if (current is null)
            {
                return;
            }
        }

        var lastProperty = current?.GetType().GetProperty(parts[^1], BindingFlags.Instance | BindingFlags.Public);
        if (lastProperty is null || !lastProperty.CanWrite)
        {
            return;
        }

        lastProperty.SetValue(current, CloneBinding(value));
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(ResetDefaultTitle));
        OnPropertyChanged(nameof(ResetDefaultSubtitle));
        OnPropertyChanged(nameof(ResetDefaultText));
        OnPropertyChanged(nameof(ActionHeaderText));
        OnPropertyChanged(nameof(KeyBindingHeaderText));
    }
}

public sealed class KeyBindingsPageViewModel : KeyBindingsSettingsPageViewModel
{
}
