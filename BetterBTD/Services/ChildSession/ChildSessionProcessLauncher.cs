using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BetterBTD.Services.ChildSession;

internal static class ChildSessionProcessLauncher
{
    private const int TaskActionExecute = 0;
    private const int TaskCreate = 2;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunLevelHighest = 1;
    private const int TaskRunUseSessionId = 4;

    public static Task LaunchAsync(
        uint childSessionId,
        uint rootSessionId,
        string pipeName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => LaunchWithTemporaryTask(childSessionId, rootSessionId, pipeName),
            cancellationToken);
    }

    private static void LaunchWithTemporaryTask(uint childSessionId, uint rootSessionId, string pipeName)
    {
        var actualChildSessionId = ChildSessionNativeMethods.TryGetChildSessionId();
        if (actualChildSessionId != childSessionId)
        {
            throw new InvalidOperationException(
                $"The requested Child Session {childSessionId} is no longer active " +
                $"(current session: {actualChildSessionId?.ToString() ?? "none"}).");
        }

        var processInfo = CreateProcessInfo(rootSessionId, pipeName);
        var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Task Scheduler COM service is unavailable.");
        var taskName = $"BetterBTD-ChildSession-{Guid.NewGuid():N}";
        using var identity = WindowsIdentity.GetCurrent();
        var accountName = identity.Name;

        object? schedulerObject = null;
        object? rootFolderObject = null;
        object? definitionObject = null;
        object? actionObject = null;
        object? registeredTaskObject = null;
        var taskRegistered = false;

        try
        {
            schedulerObject = Activator.CreateInstance(schedulerType)
                ?? throw new InvalidOperationException("Unable to create Task Scheduler COM service.");
            dynamic scheduler = schedulerObject;
            scheduler.Connect();

            rootFolderObject = scheduler.GetFolder("\\");
            dynamic rootFolder = rootFolderObject;
            definitionObject = scheduler.NewTask(0);
            dynamic definition = definitionObject;
            definition.RegistrationInfo.Author = "BetterBTD";
            definition.RegistrationInfo.Description =
                $"Launch BetterBTD into Child Session {childSessionId}";
            definition.Settings.Enabled = true;
            definition.Settings.Hidden = true;
            definition.Settings.AllowDemandStart = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT0S";
            definition.Principal.UserId = accountName;
            definition.Principal.LogonType = TaskLogonInteractiveToken;
            definition.Principal.RunLevel = TaskRunLevelHighest;

            actionObject = definition.Actions.Create(TaskActionExecute);
            dynamic action = actionObject;
            action.Path = processInfo.ExecutablePath;
            action.Arguments = processInfo.Arguments;
            action.WorkingDirectory = processInfo.WorkingDirectory;

            registeredTaskObject = rootFolder.RegisterTaskDefinition(
                taskName,
                definition,
                TaskCreate,
                accountName,
                null,
                TaskLogonInteractiveToken,
                null);
            taskRegistered = true;
            dynamic registeredTask = registeredTaskObject;
            object? runningTask = registeredTask.RunEx(
                null,
                TaskRunUseSessionId,
                checked((int)childSessionId),
                null);
            ReleaseComObject(runningTask);
        }
        finally
        {
            if (taskRegistered && rootFolderObject is not null)
            {
                try
                {
                    dynamic rootFolder = rootFolderObject;
                    rootFolder.DeleteTask(taskName, 0);
                }
                catch (COMException)
                {
                }
            }

            ReleaseComObject(registeredTaskObject);
            ReleaseComObject(actionObject);
            ReleaseComObject(definitionObject);
            ReleaseComObject(rootFolderObject);
            ReleaseComObject(schedulerObject);
        }
    }

    private static ProcessLaunchInfo CreateProcessInfo(uint rootSessionId, string pipeName)
    {
        var currentProcessPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to resolve the BetterBTD executable path.");
        var fullProcessPath = Path.GetFullPath(currentProcessPath);
        var arguments = $"--instance child-session --root-session-id {rootSessionId} " +
                        $"--child-session-pipe {QuoteArgument(pipeName)}";

        if (string.Equals(Path.GetFileNameWithoutExtension(fullProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("Unable to resolve the BetterBTD entry assembly.");
            return new ProcessLaunchInfo(
                fullProcessPath,
                $"{QuoteArgument(Path.GetFullPath(entryAssemblyPath))} {arguments}",
                AppContext.BaseDirectory);
        }

        if (!File.Exists(fullProcessPath))
        {
            throw new FileNotFoundException("The BetterBTD executable does not exist.", fullProcessPath);
        }

        return new ProcessLaunchInfo(fullProcessPath, arguments, AppContext.BaseDirectory);
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private sealed record ProcessLaunchInfo(
        string ExecutablePath,
        string Arguments,
        string WorkingDirectory);
}
