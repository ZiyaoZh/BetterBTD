namespace BetterBTD.Services.ChildSession;

internal enum BetterBtdInstanceRole
{
    Primary,
    ChildSession
}

internal sealed record InstanceLaunchOptions(
    BetterBtdInstanceRole Role,
    uint? RootSessionId,
    string? ControlPipeName)
{
    public bool IsPrimary => Role == BetterBtdInstanceRole.Primary;

    public static InstanceLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var role = BetterBtdInstanceRole.Primary;
        var rootSessionId = ParseOptionalUInt(arguments, "--root-session-id");
        var controlPipeName = GetOptionValue(arguments, "--child-session-pipe");
        var instanceName = GetOptionValue(arguments, "--instance");

        if (!string.IsNullOrWhiteSpace(instanceName) &&
            string.Equals(instanceName, "child-session", StringComparison.OrdinalIgnoreCase))
        {
            role = BetterBtdInstanceRole.ChildSession;
        }
        else if (!string.IsNullOrWhiteSpace(instanceName) &&
                 !string.Equals(instanceName, "primary", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Unknown BetterBTD instance role '{instanceName}'. Use 'primary' or 'child-session'.");
        }

        if (role == BetterBtdInstanceRole.ChildSession && rootSessionId is null)
        {
            throw new ArgumentException("A child-session instance requires --root-session-id.");
        }

        return new InstanceLaunchOptions(role, rootSessionId, controlPipeName);
    }

    private static uint? ParseOptionalUInt(IReadOnlyList<string> arguments, string optionName)
    {
        var value = GetOptionValue(arguments, optionName);
        if (value is null)
        {
            return null;
        }

        if (!uint.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Option '{optionName}' must be an unsigned integer.");
        }

        return parsed;
    }

    private static string? GetOptionValue(IReadOnlyList<string> arguments, string optionName)
    {
        string? value = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (value is not null)
            {
                throw new ArgumentException($"Option '{optionName}' can only be specified once.");
            }

            if (index + 1 >= arguments.Count ||
                string.IsNullOrWhiteSpace(arguments[index + 1]) ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{optionName}' requires a value.");
            }

            value = arguments[++index];
        }

        return value;
    }
}
