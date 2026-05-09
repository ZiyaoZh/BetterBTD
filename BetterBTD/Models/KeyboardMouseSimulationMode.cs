namespace BetterBTD.Models;

public enum KeyboardMouseSimulationMode
{
    Standard,
    Hardware
}

public static class KeyboardMouseSimulationModeExtensions
{
    public const string StandardConfigurationValue = "Standard";
    public const string HardwareConfigurationValue = "Hardware";

    public static KeyboardMouseSimulationMode Parse(string? value)
    {
        return value?.Trim() switch
        {
            HardwareConfigurationValue => KeyboardMouseSimulationMode.Hardware,
            _ => KeyboardMouseSimulationMode.Standard
        };
    }

    public static string ToConfigurationValue(this KeyboardMouseSimulationMode mode)
    {
        return mode switch
        {
            KeyboardMouseSimulationMode.Hardware => HardwareConfigurationValue,
            _ => StandardConfigurationValue
        };
    }
}
