using System.Globalization;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Services.Shell.Localization;

public sealed partial class LocalizationService
{
    public string FormatScriptFailure(ScriptExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var failure = result.Failure;
        var reason = failure is not null && !string.IsNullOrWhiteSpace(failure.MessageKey)
            ? FormatScriptMessage(failure.MessageKey, failure.MessageArguments)
            : FormatScriptError(result.Exception);
        if (failure is null || failure.StepIndex < 0)
        {
            return string.Format(T("Editor.Runtime.FailedSimple"), reason);
        }

        var text = string.Format(
            T("Editor.Runtime.FailedStep"),
            failure.StepIndex + 1,
            LocalizeScriptCommand(failure.CommandType),
            reason);
        return failure.Attempt > 0
            ? text + Environment.NewLine + string.Format(T("Editor.Runtime.FailureAttempts"), failure.Attempt)
            : text;
    }

    public string FormatScriptError(Exception? exception)
    {
        if (exception is ScriptExecutionException scriptException &&
            !string.IsNullOrWhiteSpace(scriptException.MessageKey))
        {
            return FormatScriptMessage(scriptException.MessageKey, scriptException.MessageArguments);
        }

        if (exception?.Data["LocalizationKey"] is string key)
        {
            return FormatScriptMessage(key, exception.Data["LocalizationArguments"] as object?[] ?? []);
        }

        return T("Editor.Runtime.Error.Unexpected");
    }

    public string LocalizeScriptCommand(string commandType)
    {
        return Enum.TryParse<ScriptCommandType>(commandType, out var command) && Enum.IsDefined(command)
            ? T($"Editor.Command.{command}.Title")
            : T("Editor.Runtime.UnknownInstruction");
    }

    private string FormatScriptMessage(string key, object?[] arguments)
    {
        // Unknown metadata must not leak resource keys into a user-facing error.
        if (!_resources.TryGetValue(LanguageCode, out var resources) || !resources.TryGetValue(key, out var template))
        {
            return T("Editor.Runtime.Error.Unexpected");
        }

        var localizedArguments = arguments.ToArray();
        if (localizedArguments.Length > 0 && localizedArguments[0] is string value)
        {
            var argumentKey = key switch
            {
                "Editor.Runtime.Error.UpgradeLevelCap" or
                "Editor.Runtime.Error.UnsupportedUpgradePath" or
                "Editor.Runtime.Error.UpgradeLevelUnreadable" => $"Editor.Property.UpgradePath.{value}",
                "Editor.Runtime.Error.UnsupportedSwitchDirection" => $"Editor.Property.SwitchDirection.{value}",
                "Editor.Runtime.Error.UnsupportedWaitMode" => $"Editor.Property.WaitMode.{value}",
                "Editor.Runtime.Error.UnsupportedMonkeyAbility" => value switch
                {
                    "Ability1" => "Editor.Property.Ability1",
                    "Ability2" => "Editor.Property.Ability2",
                    _ => string.Empty
                },
                "Editor.Runtime.Error.PlacementOffsetFailed" or
                "Editor.Runtime.Error.PlacementFailed" or
                "Editor.Runtime.Error.UnsupportedPlacementSelection"
                    when value.StartsWith("Tower:", StringComparison.Ordinal) =>
                    $"GameElements.MonkeyTower.{value[6..]}",
                "Editor.Runtime.Error.PlacementOffsetFailed" or
                "Editor.Runtime.Error.PlacementFailed" or
                "Editor.Runtime.Error.HeroPlacementActivationFailed" or
                "Editor.Runtime.Error.UnsupportedPlacementSelection"
                    when value.StartsWith("Hero:", StringComparison.Ordinal) =>
                    $"GameElements.Hero.{value[5..]}",
                _ => string.Empty
            };
            if (resources.TryGetValue(argumentKey, out var displayName))
            {
                localizedArguments[0] = displayName;
            }
        }

        return string.Format(
            CultureInfo.GetCultureInfo(LanguageCode), template, localizedArguments);
    }
}
