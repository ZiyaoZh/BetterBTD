using System.IO;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class AutoTaskRuntimeScriptPreviewService
{
    private static readonly Lazy<AutoTaskRuntimeScriptPreviewService> InstanceHolder =
        new(() => new AutoTaskRuntimeScriptPreviewService());

    private readonly ScriptDocumentService _scriptDocumentService;
    private readonly ScriptEditorInstructionService _instructionService;
    private readonly ScriptEditorSequenceService _sequenceService;
    private readonly ScriptEditorOptionService _optionService;
    private readonly LocalizationService _localizationService;

    private AutoTaskRuntimeScriptPreviewService()
        : this(
            ScriptDocumentService.Instance,
            ScriptEditorInstructionService.Instance,
            ScriptEditorSequenceService.Instance,
            ScriptEditorOptionService.Instance,
            LocalizationService.Instance)
    {
    }

    internal AutoTaskRuntimeScriptPreviewService(
        ScriptDocumentService scriptDocumentService,
        ScriptEditorInstructionService instructionService,
        ScriptEditorSequenceService sequenceService,
        ScriptEditorOptionService optionService,
        LocalizationService localizationService)
    {
        _scriptDocumentService = scriptDocumentService ?? throw new ArgumentNullException(nameof(scriptDocumentService));
        _instructionService = instructionService ?? throw new ArgumentNullException(nameof(instructionService));
        _sequenceService = sequenceService ?? throw new ArgumentNullException(nameof(sequenceService));
        _optionService = optionService ?? throw new ArgumentNullException(nameof(optionService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public static AutoTaskRuntimeScriptPreviewService Instance => InstanceHolder.Value;

    public AutoTaskRuntimeScriptPreview Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var loadResult = _scriptDocumentService.LoadCompatible(filePath);
        var parameterOptions = _optionService.CreateParameterOptions(_localizationService);
        var instructionLibrary = _instructionService.CreateInstructionLibrary().ToList();
        var templatesByType = instructionLibrary.ToDictionary(x => x.Type);
        var monkeyObjectsByBindingId = loadResult.Document.MonkeyObjects
            .Where(x => !string.IsNullOrWhiteSpace(x.BindingId))
            .GroupBy(x => x.BindingId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var instructions = loadResult.Document.Instructions
            .Select(instruction => _instructionService.CreateInstructionInstanceFromDocument(
                instruction,
                monkeyObjectsByBindingId,
                templatesByType,
                string.Empty,
                parameterOptions.InventoryOptions.FirstOrDefault()?.Code ?? string.Empty,
                parameterOptions.ActivatedAbilityOptions.FirstOrDefault()?.Code ?? string.Empty))
            .ToList();

        _sequenceService.UpdateInstructionLocalization(instructionLibrary, instructions, _localizationService);

        return new AutoTaskRuntimeScriptPreview
        {
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            Steps = instructions
                .Select(instruction => string.IsNullOrWhiteSpace(instruction.DisplayName)
                    ? instruction.Type.ToString()
                    : instruction.DisplayName)
                .ToList()
        };
    }
}

public sealed class AutoTaskRuntimeScriptPreview
{
    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
}
