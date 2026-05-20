using System.IO;
using System.Text.Json;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services.MyScripts;

public sealed class ManagedScriptLibraryService
{
    private static readonly Lazy<ManagedScriptLibraryService> InstanceHolder =
        new(() => new ManagedScriptLibraryService());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly string _assetsDirectory;
    private readonly string _manifestFilePath;
    private readonly ScriptDocumentService _scriptDocumentService;
    private readonly ManagedScriptSlotCatalogService _slotCatalogService;
    private readonly object _syncRoot = new();

    private ManagedScriptLibraryService()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BetterBTD",
                "MyScripts"),
            ScriptDocumentService.Instance,
            ManagedScriptSlotCatalogService.Instance)
    {
    }

    internal ManagedScriptLibraryService(
        string rootDirectory,
        ScriptDocumentService scriptDocumentService,
        ManagedScriptSlotCatalogService slotCatalogService)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        _assetsDirectory = Path.Combine(_rootDirectory, "Assets");
        _manifestFilePath = Path.Combine(_rootDirectory, "library.json");
        _scriptDocumentService = scriptDocumentService ?? throw new ArgumentNullException(nameof(scriptDocumentService));
        _slotCatalogService = slotCatalogService ?? throw new ArgumentNullException(nameof(slotCatalogService));
    }

    public static ManagedScriptLibraryService Instance => InstanceHolder.Value;

    public ManagedScriptLibrarySnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            var document = LoadManifest();
            RefreshCachedMetadata(document);
            SaveManifest(document);
            return BuildSnapshot(document);
        }
    }

    public ManagedScriptAssetEntry ImportScript(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        lock (_syncRoot)
        {
            EnsureStorage();

            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("Script file was not found.", sourceFilePath);
            }

            var scriptDocument = _scriptDocumentService.Load(sourceFilePath);
            var document = LoadManifest();

            var scriptId = Guid.NewGuid().ToString("N");
            var storedFileName = $"{scriptId}.btd";
            var storedFilePath = Path.Combine(_assetsDirectory, storedFileName);
            File.Copy(sourceFilePath, storedFilePath, overwrite: false);

            var now = DateTimeOffset.UtcNow;
            var record = new ManagedScriptAssetRecord
            {
                ScriptId = scriptId,
                DisplayName = Path.GetFileNameWithoutExtension(sourceFilePath),
                SourceFileName = Path.GetFileName(sourceFilePath),
                StoredFileName = storedFileName,
                Map = scriptDocument.Metadata.Map,
                Difficulty = scriptDocument.Metadata.Difficulty,
                Mode = scriptDocument.Metadata.Mode,
                Hero = scriptDocument.Metadata.Hero,
                Tags = [.. ScriptTagCatalog.NormalizeStoredTags(scriptDocument.Metadata.Tags)],
                ImportedAt = now,
                UpdatedAt = now
            };

            document.Scripts.Add(record);
            SaveManifest(document);

            return BuildSnapshot(document).Scripts.First(x => x.ScriptId == scriptId);
        }
    }

    public void ExportScript(string scriptId, string targetFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

        lock (_syncRoot)
        {
            var document = LoadManifest();
            var record = document.Scripts.FirstOrDefault(x => string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException("Managed script asset was not found.");
            var sourceFilePath = GetStoredFilePath(record);
            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("Managed script asset file was not found.", sourceFilePath);
            }

            var directory = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourceFilePath, targetFilePath, overwrite: true);
        }
    }

    public bool RemoveScript(string scriptId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptId);

        lock (_syncRoot)
        {
            var document = LoadManifest();
            var record = document.Scripts.FirstOrDefault(x => string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                return false;
            }

            document.Scripts.Remove(record);
            document.Bindings.RemoveAll(x => string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase));

            var storedFilePath = GetStoredFilePath(record);
            if (File.Exists(storedFilePath))
            {
                File.Delete(storedFilePath);
            }

            SaveManifest(document);
            return true;
        }
    }

    public void SetBinding(string slotId, string? scriptId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        lock (_syncRoot)
        {
            var document = LoadManifest();
            var binding = document.Bindings.FirstOrDefault(x => string.Equals(x.SlotId, slotId, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(scriptId))
            {
                if (binding is not null)
                {
                    document.Bindings.Remove(binding);
                    SaveManifest(document);
                }

                return;
            }

            if (!_slotCatalogService.TryGetById(slotId, out _))
            {
                throw new InvalidOperationException("Managed script slot was not found.");
            }

            if (document.Scripts.All(x => !string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Managed script asset was not found.");
            }

            if (binding is null)
            {
                document.Bindings.Add(new ManagedScriptSlotBindingRecord
                {
                    SlotId = slotId.Trim(),
                    ScriptId = scriptId.Trim(),
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                binding.ScriptId = scriptId.Trim();
                binding.UpdatedAt = DateTimeOffset.UtcNow;
            }

            SaveManifest(document);
        }
    }

    public bool TryGetManagedScriptFilePath(string scriptId, out string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptId);

        lock (_syncRoot)
        {
            var document = LoadManifest();
            var record = document.Scripts.FirstOrDefault(x => string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                filePath = string.Empty;
                return false;
            }

            filePath = GetStoredFilePath(record);
            return File.Exists(filePath);
        }
    }

    public bool TryResolveSlotBinding(string slotId, out string scriptId, out string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        lock (_syncRoot)
        {
            var document = LoadManifest();
            var binding = document.Bindings.FirstOrDefault(x => string.Equals(x.SlotId, slotId, StringComparison.OrdinalIgnoreCase));
            if (binding is null || string.IsNullOrWhiteSpace(binding.ScriptId))
            {
                scriptId = string.Empty;
                filePath = string.Empty;
                return false;
            }

            var record = document.Scripts.FirstOrDefault(x => string.Equals(x.ScriptId, binding.ScriptId, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                scriptId = binding.ScriptId;
                filePath = string.Empty;
                return false;
            }

            scriptId = record.ScriptId;
            filePath = GetStoredFilePath(record);
            return File.Exists(filePath);
        }
    }

    private ManagedScriptLibraryDocument LoadManifest()
    {
        EnsureStorage();

        if (!File.Exists(_manifestFilePath))
        {
            return new ManagedScriptLibraryDocument();
        }

        var json = File.ReadAllText(_manifestFilePath);
        var document = JsonSerializer.Deserialize<ManagedScriptLibraryDocument>(json, JsonOptions)
                       ?? new ManagedScriptLibraryDocument();
        document.Scripts ??= [];
        document.Bindings ??= [];

        foreach (var script in document.Scripts)
        {
            NormalizeRecord(script);
        }

        foreach (var binding in document.Bindings)
        {
            binding.SlotId = binding.SlotId?.Trim() ?? string.Empty;
            binding.ScriptId = binding.ScriptId?.Trim() ?? string.Empty;
        }

        document.Bindings = document.Bindings
            .Where(x => x.SlotId.Length > 0)
            .GroupBy(x => x.SlotId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(item => item.UpdatedAt).First())
            .ToList();

        return document;
    }

    private void SaveManifest(ManagedScriptLibraryDocument document)
    {
        EnsureStorage();
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(_manifestFilePath, json);
    }

    private void RefreshCachedMetadata(ManagedScriptLibraryDocument document)
    {
        foreach (var record in document.Scripts)
        {
            var storedFilePath = GetStoredFilePath(record);
            if (!File.Exists(storedFilePath))
            {
                continue;
            }

            var scriptDocument = _scriptDocumentService.Load(storedFilePath);
            record.Map = scriptDocument.Metadata.Map;
            record.Difficulty = scriptDocument.Metadata.Difficulty;
            record.Mode = scriptDocument.Metadata.Mode;
            record.Hero = scriptDocument.Metadata.Hero;
            record.Tags = [.. ScriptTagCatalog.NormalizeStoredTags(scriptDocument.Metadata.Tags)];
            record.UpdatedAt = File.GetLastWriteTimeUtc(storedFilePath);
        }
    }

    private ManagedScriptLibrarySnapshot BuildSnapshot(ManagedScriptLibraryDocument document)
    {
        var assets = document.Scripts
            .Select(record => BuildAssetEntry(record, document.Bindings))
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var assetsById = assets.ToDictionary(x => x.ScriptId, StringComparer.OrdinalIgnoreCase);
        var bindingsBySlotId = document.Bindings.ToDictionary(x => x.SlotId, StringComparer.OrdinalIgnoreCase);

        var slots = _slotCatalogService
            .GetAll()
            .Select(definition =>
            {
                bindingsBySlotId.TryGetValue(definition.SlotId, out var binding);
                ManagedScriptAssetEntry? boundScript = null;
                if (binding is not null && binding.ScriptId.Length > 0)
                {
                    assetsById.TryGetValue(binding.ScriptId, out boundScript);
                }

                return new ManagedScriptSlotEntry
                {
                    Definition = definition,
                    BoundScriptId = binding?.ScriptId ?? string.Empty,
                    BoundScript = boundScript
                };
            })
            .OrderBy(x => x.Definition.TaskKind)
            .ThenBy(x => x.Definition.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ManagedScriptLibrarySnapshot
        {
            Scripts = assets,
            Slots = slots
        };
    }

    private ManagedScriptAssetEntry BuildAssetEntry(
        ManagedScriptAssetRecord record,
        IReadOnlyList<ManagedScriptSlotBindingRecord> bindings)
    {
        var storedFilePath = GetStoredFilePath(record);
        var map = TryParseEnum(record.Map, GameMapType.MonkeyMeadow, out var parsedMap);
        var difficulty = TryParseEnum(record.Difficulty, StageDifficulty.Medium, out var parsedDifficulty);
        var mode = TryParseEnum(record.Mode, StageMode.Standard, out var parsedMode);
        var hero = TryParseEnum(record.Hero, HeroType.Quincy, out var parsedHero);

        return new ManagedScriptAssetEntry
        {
            ScriptId = record.ScriptId,
            DisplayName = record.DisplayName,
            SourceFileName = record.SourceFileName,
            StoredFilePath = storedFilePath,
            Map = parsedMap,
            Difficulty = parsedDifficulty,
            Mode = parsedMode,
            Hero = parsedHero,
            Tags = [.. ScriptTagCatalog.NormalizeStoredTags(record.Tags)],
            ImportedAt = record.ImportedAt,
            UpdatedAt = record.UpdatedAt,
            BindingCount = bindings.Count(x => string.Equals(x.ScriptId, record.ScriptId, StringComparison.OrdinalIgnoreCase)),
            HasMissingFile = !File.Exists(storedFilePath),
            HasMetadataIssue = !(map && difficulty && mode && hero)
        };
    }

    private string GetStoredFilePath(ManagedScriptAssetRecord record)
    {
        return Path.Combine(_assetsDirectory, record.StoredFileName);
    }

    private void EnsureStorage()
    {
        Directory.CreateDirectory(_rootDirectory);
        Directory.CreateDirectory(_assetsDirectory);
    }

    private static void NormalizeRecord(ManagedScriptAssetRecord record)
    {
        record.ScriptId = string.IsNullOrWhiteSpace(record.ScriptId)
            ? Guid.NewGuid().ToString("N")
            : record.ScriptId.Trim();
        record.DisplayName = record.DisplayName?.Trim() ?? string.Empty;
        record.SourceFileName = record.SourceFileName?.Trim() ?? string.Empty;
        record.StoredFileName = record.StoredFileName?.Trim() ?? string.Empty;
        record.Map = record.Map?.Trim() ?? GameMapType.MonkeyMeadow.ToString();
        record.Difficulty = record.Difficulty?.Trim() ?? StageDifficulty.Medium.ToString();
        record.Mode = record.Mode?.Trim() ?? StageMode.Standard.ToString();
        record.Hero = record.Hero?.Trim() ?? HeroType.Quincy.ToString();
        record.Tags = [.. ScriptTagCatalog.NormalizeStoredTags(record.Tags)];
    }

    private static bool TryParseEnum<TEnum>(string? value, TEnum fallback, out TEnum result)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out result))
        {
            return true;
        }

        result = fallback;
        return false;
    }
}
