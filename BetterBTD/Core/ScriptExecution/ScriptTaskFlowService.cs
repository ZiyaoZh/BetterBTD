using System.IO;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;

namespace BetterBTD.Core.ScriptExecution;

public sealed class ScriptTaskFlowService
{
    private static readonly Lazy<ScriptTaskFlowService> InstanceHolder = new(() => new ScriptTaskFlowService());

    private readonly ScriptDocumentService _scriptDocumentService;

    private ScriptTaskFlowService()
    {
        _scriptDocumentService = ScriptDocumentService.Instance;
    }

    public static ScriptTaskFlowService Instance => InstanceHolder.Value;

    public ScriptTaskFlow LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var document = _scriptDocumentService.Load(filePath);
        return Build(document, filePath);
    }

    public ScriptTaskFlow Build(ScriptDocument document, string? sourceFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var monkeyObjectsByBindingId = document.MonkeyObjects
            .Where(x => !string.IsNullOrWhiteSpace(x.BindingId))
            .ToDictionary(x => x.BindingId, StringComparer.OrdinalIgnoreCase);

        var steps = new List<ScriptTaskFlowStep>(document.Instructions.Count);
        for (var index = 0; index < document.Instructions.Count; index++)
        {
            var instruction = document.Instructions[index];
            if (!Enum.TryParse<ScriptCommandType>(instruction.CommandType, true, out var commandType))
            {
                throw ScriptExecutionException.WithLocalization(
                    new InvalidDataException($"Unsupported script command type '{instruction.CommandType}' at index {index}."),
                    "Editor.Runtime.Error.UnsupportedCommand",
                    instruction.CommandType,
                    index + 1);
            }

            steps.Add(new ScriptTaskFlowStep
            {
                Index = index,
                CommandType = commandType,
                Instruction = instruction
            });
        }

        return new ScriptTaskFlow
        {
            SourceFilePath = sourceFilePath ?? string.Empty,
            Document = document,
            Steps = steps,
            MonkeyObjectsByBindingId = monkeyObjectsByBindingId
        };
    }
}
