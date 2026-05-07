using System.Windows;
using System.Windows.Controls;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Views.Controls.ScriptEditor;

public sealed class ScriptInstructionEditorTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PlaceMonkeyTemplate { get; set; }

    public DataTemplate? UpgradeMonkeyTemplate { get; set; }

    public DataTemplate? SwitchMonkeyTargetTemplate { get; set; }

    public DataTemplate? SetMonkeyAbilityTemplate { get; set; }

    public DataTemplate? SellMonkeyTemplate { get; set; }

    public DataTemplate? PlaceHeroInventoryTemplate { get; set; }

    public DataTemplate? ActivateAbilityTemplate { get; set; }

    public DataTemplate? MouseClickTemplate { get; set; }

    public DataTemplate? NextRoundTemplate { get; set; }

    public DataTemplate? WaitTemplate { get; set; }

    public DataTemplate? ModifyMonkeyCoordinateTemplate { get; set; }

    public DataTemplate? CommentTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ScriptInstructionInstance instruction)
        {
            return base.SelectTemplate(item, container);
        }

        return instruction.Type switch
        {
            ScriptCommandType.PlaceMonkey => PlaceMonkeyTemplate,
            ScriptCommandType.UpgradeMonkey => UpgradeMonkeyTemplate,
            ScriptCommandType.SwitchMonkeyTarget => SwitchMonkeyTargetTemplate,
            ScriptCommandType.SetMonkeyAbility => SetMonkeyAbilityTemplate,
            ScriptCommandType.SellMonkey => SellMonkeyTemplate,
            ScriptCommandType.PlaceHeroInventory => PlaceHeroInventoryTemplate,
            ScriptCommandType.ActivateAbility => ActivateAbilityTemplate,
            ScriptCommandType.MouseClick => MouseClickTemplate,
            ScriptCommandType.NextRound => NextRoundTemplate,
            ScriptCommandType.Wait => WaitTemplate,
            ScriptCommandType.ModifyMonkeyCoordinate => ModifyMonkeyCoordinateTemplate,
            ScriptCommandType.Comment => CommentTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}
