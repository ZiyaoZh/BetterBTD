using System.IO;
using System.Text.Json;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services;

public sealed class LegacyScriptDocumentService
{
    private static readonly Lazy<LegacyScriptDocumentService> InstanceHolder = new(() => new LegacyScriptDocumentService());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private LegacyScriptDocumentService()
    {
    }

    public static LegacyScriptDocumentService Instance => InstanceHolder.Value;

    public LegacyScriptModel Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Legacy script document was not found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        return LoadFromJson(json);
    }

    public LegacyScriptModel LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var document = JsonSerializer.Deserialize<LegacyScriptModel>(json, JsonOptions) ?? new LegacyScriptModel();
        NormalizeDocument(document);
        ValidateDocument(document);
        return document;
    }

    public IReadOnlyList<LegacyScriptInstruction> GetInstructions(LegacyScriptModel document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var instructions = new List<LegacyScriptInstruction>(document.InstructionsList.Count);
        for (var index = 0; index < document.InstructionsList.Count; index++)
        {
            instructions.Add(LegacyScriptInstruction.FromSlots(index, document.InstructionsList[index]));
        }

        return instructions;
    }

    private static void NormalizeDocument(LegacyScriptModel document)
    {
        document.Metadata ??= new LegacyScriptMetadata();
        document.Metadata.Version = document.Metadata.Version?.Trim() ?? string.Empty;
        document.Metadata.ScriptName = document.Metadata.ScriptName?.Trim() ?? string.Empty;
        document.Metadata.AnchorCoords ??= new LegacyAnchorCoordinates();
        document.InstructionsList ??= [];
        document.MonkeyCounts ??= [];
        document.MonkeyIds ??= [];
    }

    private static void ValidateDocument(LegacyScriptModel document)
    {
        for (var index = 0; index < document.InstructionsList.Count; index++)
        {
            _ = LegacyScriptInstruction.FromSlots(index, document.InstructionsList[index]);
        }
    }
}
