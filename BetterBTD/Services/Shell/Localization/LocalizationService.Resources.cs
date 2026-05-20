using System;
using System.Collections.Generic;

namespace BetterBTD.Services.Shell.Localization;

public sealed partial class LocalizationService
{
    private static Dictionary<string, string> BuildZhCnResources() => MergeResources(
        BuildZhCnShellResources(),
        BuildZhCnStartResources(),
        BuildZhCnCaptureTestResources(),
        BuildZhCnTasksResources(),
        BuildZhCnLibraryResources(),
        BuildZhCnEditorResources(),
        BuildZhCnEditorPlacementResources(),
        BuildZhCnGameElementsResources());

    private static Dictionary<string, string> BuildEnUsResources() => MergeResources(
        BuildEnUsShellResources(),
        BuildEnUsStartResources(),
        BuildEnUsCaptureTestResources(),
        BuildEnUsTasksResources(),
        BuildEnUsLibraryResources(),
        BuildEnUsEditorResources(),
        BuildEnUsEditorPlacementResources(),
        BuildEnUsGameElementsResources());

    private static Dictionary<string, string> MergeResources(params Dictionary<string, string>[] groups)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            foreach (var pair in group)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }
}
