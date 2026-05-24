using System.IO;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.MyScripts;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class ManagedAutoTaskScriptResolver : IAutoTaskScriptResolver
{
    private static readonly Lazy<ManagedAutoTaskScriptResolver> InstanceHolder =
        new(() => new ManagedAutoTaskScriptResolver());

    private readonly ManagedScriptLibraryService _managedScriptLibraryService;

    private ManagedAutoTaskScriptResolver()
        : this(ManagedScriptLibraryService.Instance)
    {
    }

    internal ManagedAutoTaskScriptResolver(ManagedScriptLibraryService managedScriptLibraryService)
    {
        _managedScriptLibraryService = managedScriptLibraryService ?? throw new ArgumentNullException(nameof(managedScriptLibraryService));
    }

    public static ManagedAutoTaskScriptResolver Instance => InstanceHolder.Value;

    public Task<AutoTaskScriptResolution> ResolveAsync(
        AutoTaskScriptQuery query,
        AutoTaskRuntimeState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(state);

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(query.PreferredFilePath) && File.Exists(query.PreferredFilePath))
        {
            return Task.FromResult(new AutoTaskScriptResolution
            {
                IsResolved = true,
                Query = query,
                FilePath = query.PreferredFilePath,
                Message = "Resolved by preferred script path."
            });
        }

        var resolvedSlotId = ResolveSlotId(query);
        if (resolvedSlotId.Length > 0 &&
            _managedScriptLibraryService.TryResolveSlotBinding(resolvedSlotId, out _, out var managedFilePath))
        {
            return Task.FromResult(new AutoTaskScriptResolution
            {
                IsResolved = true,
                Query = query,
                FilePath = managedFilePath,
                Message = $"Resolved by managed script slot '{resolvedSlotId}'."
            });
        }

        return Task.FromResult(new AutoTaskScriptResolution
        {
            IsResolved = false,
            Query = query,
            Message = BuildUnresolvedMessage(query, resolvedSlotId)
        });
    }

    private static string ResolveSlotId(AutoTaskScriptQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SlotId))
        {
            return query.SlotId.Trim();
        }

        return query.Kind switch
        {
            AutoTaskKind.Custom => ManagedScriptSlotIdFactory.CreateCustomDefaultSlotId(),
            AutoTaskKind.Collection when query.StageTarget is not null &&
                                         ManagedScriptCollectionModeCatalog.TryNormalizeKey(query.VariantKey, out var variantKey) =>
                ManagedScriptSlotIdFactory.CreateCollectionSlotId(variantKey, query.StageTarget.Map),
            AutoTaskKind.BlackBorder when query.StageTarget is not null => ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                query.StageTarget.Map,
                query.StageTarget.Difficulty,
                query.StageTarget.Mode),
            AutoTaskKind.Race => ManagedScriptSlotIdFactory.CreateRaceCurrentSlotId(),
            _ => string.Empty
        };
    }

    private static string BuildUnresolvedMessage(AutoTaskScriptQuery query, string slotId)
    {
        if (!string.IsNullOrWhiteSpace(query.PreferredFilePath))
        {
            return $"Preferred script path '{query.PreferredFilePath}' does not exist, and no managed binding was found.";
        }

        if (slotId.Length > 0)
        {
            return $"Managed script slot '{slotId}' is not configured.";
        }

        if (query.Kind == AutoTaskKind.Collection)
        {
            return "Collection task variant key is not configured.";
        }

        return "Auto-task script resolution could not determine a managed script slot yet.";
    }
}

public sealed class ScriptTaskFlowAutoTaskScriptExecutorAdapter : IAutoTaskScriptExecutor
{
    private static readonly Lazy<ScriptTaskFlowAutoTaskScriptExecutorAdapter> InstanceHolder =
        new(() => new ScriptTaskFlowAutoTaskScriptExecutorAdapter());

    private readonly ScriptTaskFlowExecutor _scriptTaskFlowExecutor;

    private ScriptTaskFlowAutoTaskScriptExecutorAdapter()
        : this(ScriptTaskFlowExecutor.Instance)
    {
    }

    internal ScriptTaskFlowAutoTaskScriptExecutorAdapter(ScriptTaskFlowExecutor scriptTaskFlowExecutor)
    {
        _scriptTaskFlowExecutor = scriptTaskFlowExecutor ?? throw new ArgumentNullException(nameof(scriptTaskFlowExecutor));
    }

    public static ScriptTaskFlowAutoTaskScriptExecutorAdapter Instance => InstanceHolder.Value;

    public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged
    {
        add => _scriptTaskFlowExecutor.ProgressChanged += value;
        remove => _scriptTaskFlowExecutor.ProgressChanged -= value;
    }

    public bool IsRunning => _scriptTaskFlowExecutor.IsRunning;

    public bool RequestPause()
    {
        return _scriptTaskFlowExecutor.RequestPause();
    }

    public bool Resume()
    {
        return _scriptTaskFlowExecutor.Resume();
    }

    public Task<ScriptExecutionResult> ExecuteAsync(
        string filePath,
        ScriptExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        return _scriptTaskFlowExecutor.ExecuteAsync(filePath, options, cancellationToken);
    }
}

public static class AutoTaskRuntimeServiceFactory
{
    public static AutoTaskRuntimeServices CreateDefault()
    {
        return new AutoTaskRuntimeServices
        {
            GameUiState = GameUiStateService.Instance,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = GameUiActionExecutor.Instance,
            ScriptResolver = ManagedAutoTaskScriptResolver.Instance,
            ScriptExecutor = ScriptTaskFlowAutoTaskScriptExecutorAdapter.Instance
        };
    }
}
