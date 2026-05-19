using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services.MyScripts;

public sealed class ScriptDocumentService
{
    private static readonly Lazy<ScriptDocumentService> InstanceHolder = new(() => new ScriptDocumentService());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private ScriptDocumentService()
    {
    }

    public static ScriptDocumentService Instance => InstanceHolder.Value;

    public ScriptDocumentLoadResult LoadCompatible(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (string.Equals(Path.GetExtension(filePath), LegacyScriptFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            var legacyDocument = LegacyScriptDocumentService.Instance.Load(filePath);
            var conversionResult = LegacyScriptConversionService.Instance.Convert(legacyDocument);

            return new ScriptDocumentLoadResult
            {
                Document = conversionResult.Document,
                SourceKind = ScriptDocumentSourceKind.LegacyBtd6,
                Warnings = conversionResult.Warnings
            };
        }

        return new ScriptDocumentLoadResult
        {
            Document = Load(filePath),
            SourceKind = ScriptDocumentSourceKind.Current,
            Warnings = []
        };
    }

    public void Save(string filePath, ScriptDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(document);

        var optimizedDocument = ScriptInstructionOptimizationService.Instance.OptimizeDocument(document);

        NormalizeDocument(optimizedDocument);
        ValidateDocument(optimizedDocument);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(optimizedDocument, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public ScriptDocument Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Script document was not found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        var document = JsonSerializer.Deserialize<ScriptDocument>(json, JsonOptions) ?? new ScriptDocument();

        NormalizeDocument(document);
        ValidateDocument(document);
        return document;
    }

    private static void NormalizeDocument(ScriptDocument document)
    {
        document.Schema = string.IsNullOrWhiteSpace(document.Schema)
            ? ScriptDocumentFormat.Schema
            : document.Schema.Trim();
        document.FormatVersion = document.FormatVersion <= 0
            ? ScriptDocumentFormat.CurrentFormatVersion
            : document.FormatVersion;
        document.Metadata ??= new ScriptMetadataDocument();
        document.MonkeyObjects ??= [];
        document.Instructions ??= [];

        document.Metadata.ScriptVersion = NormalizeScriptVersion(document.Metadata.ScriptVersion);
        document.Metadata.Description = document.Metadata.Description?.Trim() ?? string.Empty;
        document.Metadata.Map = NormalizeOrDefault(document.Metadata.Map, GameMapType.MonkeyMeadow.ToString());
        document.Metadata.Difficulty = NormalizeOrDefault(document.Metadata.Difficulty, StageDifficulty.Medium.ToString());
        document.Metadata.Mode = NormalizeOrDefault(document.Metadata.Mode, StageMode.Standard.ToString());
        document.Metadata.Hero = NormalizeOrDefault(document.Metadata.Hero, HeroType.Quincy.ToString());

        foreach (var monkeyObject in document.MonkeyObjects)
        {
            monkeyObject.BindingId = monkeyObject.BindingId?.Trim() ?? string.Empty;
            monkeyObject.ObjectId = monkeyObject.ObjectId?.Trim() ?? string.Empty;
            monkeyObject.SelectionCode = monkeyObject.SelectionCode?.Trim() ?? string.Empty;
        }

        foreach (var instruction in document.Instructions)
        {
            instruction.CommandType = instruction.CommandType?.Trim() ?? string.Empty;
            instruction.SelectedMonkeyTower = instruction.SelectedMonkeyTower?.Trim() ?? string.Empty;
            instruction.MonkeyBindingId = instruction.MonkeyBindingId?.Trim() ?? string.Empty;
            instruction.MonkeyObjectId = instruction.MonkeyObjectId?.Trim() ?? string.Empty;
            instruction.TargetMonkeyBindingId = instruction.TargetMonkeyBindingId?.Trim() ?? string.Empty;
            instruction.TargetMonkeyObjectId = instruction.TargetMonkeyObjectId?.Trim() ?? string.Empty;
            instruction.SelectedInventoryItem = instruction.SelectedInventoryItem?.Trim() ?? string.Empty;
            instruction.SelectedActivatedAbility = instruction.SelectedActivatedAbility?.Trim() ?? string.Empty;
            instruction.NextRoundAction = instruction.NextRoundAction?.Trim() ?? string.Empty;
            instruction.WaitMode = instruction.WaitMode?.Trim() ?? string.Empty;
            instruction.UpgradePath = instruction.UpgradePath?.Trim() ?? string.Empty;
            instruction.SwitchDirection = instruction.SwitchDirection?.Trim() ?? string.Empty;
            instruction.SelectedAbility = instruction.SelectedAbility?.Trim() ?? string.Empty;
            instruction.WaitColorHex = instruction.WaitColorHex?.Trim() ?? string.Empty;
            instruction.CommentContent ??= string.Empty;
            instruction.Notes ??= string.Empty;
        }
    }

    private static void ValidateDocument(ScriptDocument document)
    {
        if (!string.Equals(document.Schema, ScriptDocumentFormat.Schema, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported script schema '{document.Schema}'.");
        }

        if (document.FormatVersion > ScriptDocumentFormat.CurrentFormatVersion)
        {
            throw new InvalidDataException($"Unsupported script format version '{document.FormatVersion}'.");
        }

        var duplicateMonkeyBindingId = document.MonkeyObjects
            .Where(x => !string.IsNullOrWhiteSpace(x.BindingId))
            .GroupBy(x => x.BindingId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateMonkeyBindingId is not null)
        {
            throw new InvalidDataException($"Duplicate monkey binding id '{duplicateMonkeyBindingId.Key}' was found.");
        }

        for (var index = 0; index < document.Instructions.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(document.Instructions[index].CommandType))
            {
                throw new InvalidDataException($"Instruction at index {index} is missing commandType.");
            }
        }
    }

    private static string NormalizeScriptVersion(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? ScriptDocumentFormat.DefaultScriptVersion : value.Trim();
    }

    private static string NormalizeOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

public enum ScriptDocumentSourceKind
{
    Current,
    LegacyBtd6
}

public sealed class ScriptDocumentLoadResult
{
    public required ScriptDocument Document { get; init; }

    public required ScriptDocumentSourceKind SourceKind { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

