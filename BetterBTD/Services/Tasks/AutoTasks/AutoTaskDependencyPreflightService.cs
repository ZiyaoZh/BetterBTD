using System.IO;
using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.MyScripts;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed record AutoTaskKeyBindingPreflightIssue(
    string ScriptFilePath,
    string ScriptDisplayName,
    ScriptKeyBindingPreflightIssue KeyBindingIssue);

public sealed record AutoTaskScriptDependency(string FilePath, string DisplayName);

public sealed class AutoTaskDependencyPreflightService
{
    private static readonly Lazy<AutoTaskDependencyPreflightService> InstanceHolder =
        new(() => new AutoTaskDependencyPreflightService());

    private readonly Func<string, ScriptTaskFlow> _loadTaskFlow;
    private readonly ManagedScriptLibraryService _managedScriptLibraryService;

    private AutoTaskDependencyPreflightService()
        : this(ScriptTaskFlowService.Instance.LoadFromFile, ManagedScriptLibraryService.Instance)
    {
    }

    internal AutoTaskDependencyPreflightService(
        Func<string, ScriptTaskFlow> loadTaskFlow,
        ManagedScriptLibraryService? managedScriptLibraryService = null)
    {
        _loadTaskFlow = loadTaskFlow ?? throw new ArgumentNullException(nameof(loadTaskFlow));
        _managedScriptLibraryService = managedScriptLibraryService ?? ManagedScriptLibraryService.Instance;
    }

    public static AutoTaskDependencyPreflightService Instance => InstanceHolder.Value;

    public IReadOnlyList<AutoTaskKeyBindingPreflightIssue> ValidateKeyBindings(
        IEnumerable<string> scriptFilePaths,
        KeyBindingsConfig keyBindings)
    {
        ArgumentNullException.ThrowIfNull(scriptFilePaths);
        ArgumentNullException.ThrowIfNull(keyBindings);
        return ValidateKeyBindings(
            CreateDependencies(scriptFilePaths),
            keyBindings,
            CancellationToken.None);
    }

    public Task<IReadOnlyList<AutoTaskKeyBindingPreflightIssue>> ValidateKeyBindingsAsync(
        AutoTaskRequest request,
        KeyBindingsConfig keyBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(keyBindings);

        return Task.Run(
            () => ValidateKeyBindings(ResolveScriptDependencies(request), keyBindings, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<AutoTaskKeyBindingPreflightIssue>> ValidateKeyBindingsAsync(
        IEnumerable<string> scriptFilePaths,
        KeyBindingsConfig keyBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scriptFilePaths);
        ArgumentNullException.ThrowIfNull(keyBindings);

        var dependencies = CreateDependencies(scriptFilePaths).ToArray();
        return Task.Run(() => ValidateKeyBindings(dependencies, keyBindings, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<AutoTaskScriptDependency> ResolveScriptDependencies(AutoTaskRequest request)
    {
        if (request.PreferredScriptPaths.Count > 0)
        {
            return ResolvePreferredScriptDependencies(request.PreferredScriptPaths);
        }

        if (!string.IsNullOrWhiteSpace(request.PreferredScriptPath))
        {
            return ResolvePreferredScriptDependencies([request.PreferredScriptPath]);
        }

        if (request.RequiredScriptSlotIds.Count == 0)
        {
            return [];
        }

        var requiredSlotIds = request.RequiredScriptSlotIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _managedScriptLibraryService.GetSnapshot().Slots
            .Where(slot => requiredSlotIds.Contains(slot.Definition.SlotId))
            .Select(slot => slot.BoundScript)
            .Where(script => script is not null && !script.HasMissingFile)
            .Select(script => new AutoTaskScriptDependency(script!.StoredFilePath, script.DisplayName))
            .DistinctBy(dependency => dependency.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<AutoTaskScriptDependency> ResolvePreferredScriptDependencies(
        IEnumerable<string> scriptFilePaths)
    {
        var displayNamesByPath = _managedScriptLibraryService.GetSnapshot().Scripts
            .Where(script => !script.HasMissingFile)
            .GroupBy(script => Path.GetFullPath(script.StoredFilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().DisplayName,
                StringComparer.OrdinalIgnoreCase);

        return CreateDependencies(scriptFilePaths)
            .Select(dependency => displayNamesByPath.TryGetValue(
                    Path.GetFullPath(dependency.FilePath),
                    out var displayName)
                ? dependency with { DisplayName = displayName }
                : dependency)
            .ToList();
    }

    private IReadOnlyList<AutoTaskKeyBindingPreflightIssue> ValidateKeyBindings(
        IEnumerable<AutoTaskScriptDependency> dependencies,
        KeyBindingsConfig keyBindings,
        CancellationToken cancellationToken)
    {
        var issues = new List<AutoTaskKeyBindingPreflightIssue>();
        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = dependency.FilePath?.Trim() ?? string.Empty;
            if (filePath.Length == 0 || !visitedPaths.Add(Path.GetFullPath(filePath)))
            {
                continue;
            }

            var taskFlow = _loadTaskFlow(filePath);
            foreach (var keyBindingIssue in ScriptKeyBindingPreflightValidator.Validate(taskFlow, keyBindings))
            {
                issues.Add(new AutoTaskKeyBindingPreflightIssue(
                    filePath,
                    dependency.DisplayName,
                    keyBindingIssue));
            }
        }

        return issues;
    }

    private static IEnumerable<AutoTaskScriptDependency> CreateDependencies(IEnumerable<string> scriptFilePaths)
    {
        foreach (var filePath in scriptFilePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            yield return new AutoTaskScriptDependency(
                filePath,
                Path.GetFileNameWithoutExtension(filePath));
        }
    }
}
