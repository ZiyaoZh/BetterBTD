using System.IO;
using System.Text.Json;
using BetterBTD.Helpers;
using BetterBTD.Helpers.Security;
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
    private readonly string _bindingsDirectory;
    private readonly string _manifestFilePath;
    private readonly string _blackBorderBindingsFilePath;
    private readonly string _collectionBindingsFilePath;
    private readonly string _goldBalloonBindingsFilePath;
    private readonly ScriptDocumentService _scriptDocumentService;
    private readonly ManagedScriptSlotCatalogService _slotCatalogService;
    private readonly object _syncRoot = new();

    private ManagedScriptLibraryService()
        : this(
            UserDataPathHelper.ResolveUserDataDirectory("MyScripts"),
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
        _bindingsDirectory = Path.Combine(_rootDirectory, "Bindings");
        _manifestFilePath = Path.Combine(_rootDirectory, "library.json");
        _blackBorderBindingsFilePath = Path.Combine(_bindingsDirectory, "blackborder.json");
        _collectionBindingsFilePath = Path.Combine(_bindingsDirectory, "collection.json");
        _goldBalloonBindingsFilePath = Path.Combine(_bindingsDirectory, "goldballoon.json");
        _scriptDocumentService = scriptDocumentService ?? throw new ArgumentNullException(nameof(scriptDocumentService));
        _slotCatalogService = slotCatalogService ?? throw new ArgumentNullException(nameof(slotCatalogService));
    }

    public static ManagedScriptLibraryService Instance => InstanceHolder.Value;

    public string GetTaskBindingFilePath(AutoTaskKind taskKind)
    {
        if (TryGetDedicatedBindingFilePath(taskKind, out var filePath))
        {
            return filePath;
        }

        throw new InvalidOperationException($"Task kind '{taskKind}' does not use a dedicated binding file.");
    }

    public string EnsureTaskBindingTemplate(AutoTaskKind taskKind)
    {
        if (!TryGetDedicatedBindingFilePath(taskKind, out var filePath))
        {
            throw new InvalidOperationException($"Task kind '{taskKind}' does not use a dedicated binding file.");
        }

        lock (_syncRoot)
        {
            EnsureStorage();

            if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(File.ReadAllText(filePath)))
            {
                var template = BuildTaskBindingTemplate(taskKind);
                SaveTaskBindingDocument(filePath, template);
            }
            else
            {
                var existingDocument = LoadTaskBindingDocument(filePath);
                SaveTaskBindingDocument(filePath, existingDocument);
            }

            return filePath;
        }
    }

    public ManagedScriptLibrarySnapshot GetSnapshot(IProgress<ManagedScriptLibraryLoadProgress>? progress = null)
    {
        lock (_syncRoot)
        {
            var document = LoadManifest();
            MigrateDedicatedBindings(document);
            RefreshCachedMetadata(document, progress);
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

            var loadResult = _scriptDocumentService.LoadCompatible(sourceFilePath);
            var document = LoadManifest();
            MigrateDedicatedBindings(document);
            var currentBindings = LoadCurrentBindings(document);
            var targetFingerprint = BuildDocumentFingerprint(loadResult.Document);
            var scriptId = loadResult.Document.Metadata.ScriptId;
            var existingRecord = FindRecordById(document, scriptId);
            if (existingRecord is not null)
            {
                var storedFilePath = GetStoredFilePath(existingRecord);
                _scriptDocumentService.Save(storedFilePath, loadResult.Document);
                UpdateRecordFromDocument(existingRecord, loadResult.Document, sourceFilePath, null, targetFingerprint, preserveImportedAt: true);
                SaveManifest(document);
                return BuildAssetEntry(existingRecord, currentBindings);
            }

            var recordsByFingerprint = BuildFingerprintIndex(document);
            if (recordsByFingerprint.TryGetValue(targetFingerprint, out existingRecord))
            {
                if (!string.Equals(existingRecord.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase))
                {
                    RenameRecordScriptId(document, existingRecord, scriptId);
                }

                var storedFilePath = GetStoredFilePath(existingRecord);
                _scriptDocumentService.Save(storedFilePath, loadResult.Document);
                UpdateRecordFromDocument(existingRecord, loadResult.Document, sourceFilePath, null, targetFingerprint, preserveImportedAt: true);
                SaveManifest(document);
                return BuildAssetEntry(existingRecord, currentBindings);
            }

            var record = CreateManagedScriptRecord(
                loadResult.Document,
                Path.GetFileNameWithoutExtension(sourceFilePath),
                Path.GetFileName(sourceFilePath),
                targetFingerprint);
            document.Scripts.Add(record);
            SaveManifest(document);

            return BuildAssetEntry(record, currentBindings);
        }
    }

    public IReadOnlyList<ManagedScriptAssetEntry> ImportLegacyScriptCollection(
        string sourceFilePath,
        IProgress<int>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        lock (_syncRoot)
        {
            EnsureStorage();

            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("Legacy script package was not found.", sourceFilePath);
            }

            var document = LoadManifest();
            MigrateDedicatedBindings(document);
            var currentBindings = LoadCurrentBindings(document);
            var recordsByFingerprint = BuildFingerprintIndex(document);
            var importedEntries = new List<ManagedScriptAssetEntry>();
            using var stream = File.OpenRead(sourceFilePath);
            var enumerator = JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream).GetAsyncEnumerator();
            var sourceFileName = Path.GetFileName(sourceFilePath);
            var packageDisplayName = Path.GetFileNameWithoutExtension(sourceFilePath);
            var index = 0;
            var processedCount = 0;
            progress?.Report(processedCount);

            try
            {
                while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                {
                    var element = enumerator.Current;
                    if (element.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidDataException($"Legacy script package item at index {index} must be a JSON object.");
                    }

                    var legacyDocument = LegacyScriptDocumentService.Instance.LoadFromJson(element.GetRawText());
                    var conversionResult = LegacyScriptConversionService.Instance.Convert(legacyDocument);
                    var displayName = ResolveLegacyPackageScriptDisplayName(
                        legacyDocument,
                        packageDisplayName,
                        index);
                    var targetFingerprint = BuildDocumentFingerprint(conversionResult.Document);
                    var scriptId = conversionResult.Document.Metadata.ScriptId;
                    processedCount++;
                    progress?.Report(processedCount);
                    var existingRecord = FindRecordById(document, scriptId);
                    if (existingRecord is not null)
                    {
                        var storedFilePath = GetStoredFilePath(existingRecord);
                        _scriptDocumentService.Save(storedFilePath, conversionResult.Document);
                        UpdateRecordFromDocument(existingRecord, conversionResult.Document, sourceFilePath, displayName, targetFingerprint, preserveImportedAt: true);
                        importedEntries.Add(BuildAssetEntry(existingRecord, currentBindings));
                        index++;
                        continue;
                    }

                    if (recordsByFingerprint.ContainsKey(targetFingerprint))
                    {
                        index++;
                        continue;
                    }

                    var record = CreateManagedScriptRecord(
                        conversionResult.Document,
                        displayName,
                        sourceFileName,
                        targetFingerprint);
                    document.Scripts.Add(record);
                    recordsByFingerprint[targetFingerprint] = record;
                    importedEntries.Add(BuildAssetEntry(record, currentBindings));
                    index++;
                }
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            if (processedCount == 0)
            {
                throw new InvalidDataException("Legacy script package does not contain any importable scripts.");
            }

            SaveManifest(document);
            return importedEntries;
        }
    }

    public ManagedScriptAssetEntry UpsertScript(
        string sourceFilePath,
        string? scriptId = null,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        lock (_syncRoot)
        {
            EnsureStorage();

            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("Script file was not found.", sourceFilePath);
            }

            var loadResult = _scriptDocumentService.LoadCompatible(sourceFilePath);
            var scriptDocument = loadResult.Document;
            var scriptFingerprint = BuildDocumentFingerprint(scriptDocument);
            var documentScriptId = scriptDocument.Metadata.ScriptId;
            var document = LoadManifest();
            MigrateDedicatedBindings(document);
            var currentBindings = LoadCurrentBindings(document);
            var record = FindRecordById(document, scriptId)
                         ?? FindRecordByStoredFilePath(document, sourceFilePath);
            var now = DateTimeOffset.UtcNow;

            if (record is null)
            {
                var storedFileName = $"{documentScriptId}.btd";
                var storedFilePath = Path.Combine(_assetsDirectory, storedFileName);
                _scriptDocumentService.Save(storedFilePath, scriptDocument);

                record = new ManagedScriptAssetRecord
                {
                    ScriptId = documentScriptId,
                    DisplayName = ResolveDisplayName(displayName, sourceFilePath),
                    SourceFileName = Path.GetFileName(sourceFilePath),
                    StoredFileName = storedFileName,
                    Fingerprint = scriptFingerprint,
                    Description = scriptDocument.Metadata.Description,
                    Map = scriptDocument.Metadata.Map,
                    Difficulty = scriptDocument.Metadata.Difficulty,
                    Mode = scriptDocument.Metadata.Mode,
                    Hero = scriptDocument.Metadata.Hero,
                    Tags = [.. ScriptTagCatalog.NormalizeStoredTags(scriptDocument.Metadata.Tags)],
                    ImportedAt = now,
                    UpdatedAt = now
                };

                document.Scripts.Add(record);
            }
            else
            {
                if (!string.Equals(record.ScriptId, documentScriptId, StringComparison.OrdinalIgnoreCase))
                {
                    RenameRecordScriptId(document, record, documentScriptId);
                }

                var storedFilePath = GetStoredFilePath(record);
                var isManagedSourcePath = AreSameFilePath(sourceFilePath, storedFilePath);

                if (!isManagedSourcePath)
                {
                    _scriptDocumentService.Save(storedFilePath, scriptDocument);
                    record.SourceFileName = Path.GetFileName(sourceFilePath);
                }

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    record.DisplayName = displayName.Trim();
                }
                else if (string.IsNullOrWhiteSpace(record.DisplayName))
                {
                    record.DisplayName = ResolveDisplayName(displayName, sourceFilePath);
                }

                record.Description = scriptDocument.Metadata.Description;
                record.Map = scriptDocument.Metadata.Map;
                record.Difficulty = scriptDocument.Metadata.Difficulty;
                record.Mode = scriptDocument.Metadata.Mode;
                record.Hero = scriptDocument.Metadata.Hero;
                record.Tags = [.. ScriptTagCatalog.NormalizeStoredTags(scriptDocument.Metadata.Tags)];
                record.Fingerprint = scriptFingerprint;
                record.UpdatedAt = now;
            }

            SaveManifest(document);
            return BuildAssetEntry(record, currentBindings);
        }
    }

    public void ExportScript(string scriptId, string targetFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

        lock (_syncRoot)
        {
            var document = LoadManifest();
            MigrateDedicatedBindings(document);
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
            MigrateDedicatedBindings(document);
            var record = document.Scripts.FirstOrDefault(x => string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                return false;
            }

            document.Scripts.Remove(record);
            document.Bindings.RemoveAll(x => string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase));
            RemoveScriptBindingsFromDedicatedFiles(scriptId);

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
            MigrateDedicatedBindings(document);

            if (!_slotCatalogService.TryGetById(slotId, out var slot))
            {
                throw new InvalidOperationException("Managed script slot was not found.");
            }

            var trimmedSlotId = slotId.Trim();

            if (string.IsNullOrWhiteSpace(scriptId))
            {
                if (TryGetDedicatedBindingFilePath(slot.TaskKind, out var dedicatedBindingFilePath))
                {
                    var dedicatedBindings = LoadTaskBindingDocument(dedicatedBindingFilePath);
                    if (dedicatedBindings.Bindings.Remove(trimmedSlotId))
                    {
                        SaveTaskBindingDocument(dedicatedBindingFilePath, dedicatedBindings);
                    }

                    return;
                }

                var binding = document.Bindings.FirstOrDefault(x => string.Equals(x.SlotId, trimmedSlotId, StringComparison.OrdinalIgnoreCase));
                if (binding is not null)
                {
                    document.Bindings.Remove(binding);
                    SaveManifest(document);
                }

                return;
            }

            if (document.Scripts.All(x => !string.Equals(x.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Managed script asset was not found.");
            }

            var trimmedScriptId = scriptId.Trim();

            if (TryGetDedicatedBindingFilePath(slot.TaskKind, out var bindingFilePath))
            {
                var dedicatedBindings = LoadTaskBindingDocument(bindingFilePath);
                dedicatedBindings.Bindings[trimmedSlotId] = trimmedScriptId;
                SaveTaskBindingDocument(bindingFilePath, dedicatedBindings);
            }
            else
            {
                var binding = document.Bindings.FirstOrDefault(x => string.Equals(x.SlotId, trimmedSlotId, StringComparison.OrdinalIgnoreCase));
                if (binding is null)
                {
                    document.Bindings.Add(new ManagedScriptSlotBindingRecord
                    {
                        SlotId = trimmedSlotId,
                        ScriptId = trimmedScriptId,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    binding.ScriptId = trimmedScriptId;
                    binding.UpdatedAt = DateTimeOffset.UtcNow;
                }

                SaveManifest(document);
            }
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

    public bool TryGetManagedScriptByStoredFilePath(string storedFilePath, out ManagedScriptAssetEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedFilePath);

        lock (_syncRoot)
        {
            var document = LoadManifest();
            MigrateDedicatedBindings(document);
            var record = FindRecordByStoredFilePath(document, storedFilePath);
            if (record is null)
            {
                entry = null!;
                return false;
            }

            entry = BuildAssetEntry(record, LoadCurrentBindings(document));
            return true;
        }
    }

    public bool TryResolveSlotBinding(string slotId, out string scriptId, out string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        lock (_syncRoot)
        {
            var document = LoadManifest();
            MigrateDedicatedBindings(document);
            var bindings = LoadCurrentBindings(document);
            var binding = bindings.FirstOrDefault(x => string.Equals(x.SlotId, slotId, StringComparison.OrdinalIgnoreCase));
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

    private ManagedScriptTaskBindingDocument LoadTaskBindingDocument(string filePath)
    {
        EnsureStorage();

        if (!File.Exists(filePath))
        {
            return new ManagedScriptTaskBindingDocument();
        }

        var json = File.ReadAllText(filePath);
        var document = JsonSerializer.Deserialize<ManagedScriptTaskBindingDocument>(json, JsonOptions)
                       ?? new ManagedScriptTaskBindingDocument();
        NormalizeTaskBindingDocument(filePath, document);
        return document;
    }

    private void SaveTaskBindingDocument(string filePath, ManagedScriptTaskBindingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EnsureStorage();
        NormalizeTaskBindingDocument(filePath, document);

        var persistedVersion = IsBlackBorderBindingFile(filePath)
            ? Math.Max(document.Version, 2)
            : document.Version;
        var json = JsonSerializer.Serialize(new
        {
            Version = persistedVersion,
            Bindings = new Dictionary<string, string>(document.Bindings, StringComparer.OrdinalIgnoreCase)
        }, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    private void RefreshCachedMetadata(
        ManagedScriptLibraryDocument document,
        IProgress<ManagedScriptLibraryLoadProgress>? progress = null)
    {
        var totalScriptCount = document.Scripts.Count;
        var processedScriptCount = 0;
        progress?.Report(new ManagedScriptLibraryLoadProgress
        {
            TotalScriptCount = totalScriptCount,
            ProcessedScriptCount = processedScriptCount
        });

        foreach (var record in document.Scripts)
        {
            var storedFilePath = GetStoredFilePath(record);
            if (!File.Exists(storedFilePath))
            {
                processedScriptCount++;
                progress?.Report(new ManagedScriptLibraryLoadProgress
                {
                    TotalScriptCount = totalScriptCount,
                    ProcessedScriptCount = processedScriptCount
                });
                continue;
            }

            var scriptDocument = _scriptDocumentService.LoadCompatible(storedFilePath).Document;
            record.Fingerprint = BuildDocumentFingerprint(scriptDocument);
            record.Description = scriptDocument.Metadata.Description;
            record.Map = scriptDocument.Metadata.Map;
            record.Difficulty = scriptDocument.Metadata.Difficulty;
            record.Mode = scriptDocument.Metadata.Mode;
            record.Hero = scriptDocument.Metadata.Hero;
            record.Tags = [.. ScriptTagCatalog.NormalizeStoredTags(scriptDocument.Metadata.Tags)];
            record.UpdatedAt = File.GetLastWriteTimeUtc(storedFilePath);
            processedScriptCount++;
            progress?.Report(new ManagedScriptLibraryLoadProgress
            {
                TotalScriptCount = totalScriptCount,
                ProcessedScriptCount = processedScriptCount
            });
        }
    }

    private ManagedScriptLibrarySnapshot BuildSnapshot(ManagedScriptLibraryDocument document)
    {
        var currentBindings = LoadCurrentBindings(document);
        var assets = document.Scripts
            .Select(record => BuildAssetEntry(record, currentBindings))
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var assetsById = assets.ToDictionary(x => x.ScriptId, StringComparer.OrdinalIgnoreCase);
        var bindingsBySlotId = currentBindings.ToDictionary(x => x.SlotId, StringComparer.OrdinalIgnoreCase);

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
            Description = record.Description,
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

    private ManagedScriptAssetRecord CreateManagedScriptRecord(
        ScriptDocument scriptDocument,
        string displayName,
        string sourceFileName,
        string? fingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(scriptDocument);

        var scriptId = scriptDocument.Metadata.ScriptId;
        var storedFileName = $"{scriptId}.btd";
        var storedFilePath = Path.Combine(_assetsDirectory, storedFileName);
        _scriptDocumentService.Save(storedFilePath, scriptDocument);

        var now = DateTimeOffset.UtcNow;
        return new ManagedScriptAssetRecord
        {
            ScriptId = scriptId,
            DisplayName = displayName.Trim(),
            SourceFileName = sourceFileName.Trim(),
            StoredFileName = storedFileName,
            Fingerprint = string.IsNullOrWhiteSpace(fingerprint)
                ? BuildDocumentFingerprint(scriptDocument)
                : fingerprint.Trim(),
            Description = scriptDocument.Metadata.Description,
            Map = scriptDocument.Metadata.Map,
            Difficulty = scriptDocument.Metadata.Difficulty,
            Mode = scriptDocument.Metadata.Mode,
            Hero = scriptDocument.Metadata.Hero,
            Tags = [.. ScriptTagCatalog.NormalizeStoredTags(scriptDocument.Metadata.Tags)],
            ImportedAt = now,
            UpdatedAt = now
        };
    }

    private ManagedScriptAssetRecord? FindRecordById(ManagedScriptLibraryDocument document, string? scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
        {
            return null;
        }

        return document.Scripts.FirstOrDefault(x => string.Equals(x.ScriptId, scriptId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private ManagedScriptAssetRecord? FindRecordByStoredFilePath(ManagedScriptLibraryDocument document, string sourceFilePath)
    {
        return document.Scripts.FirstOrDefault(record => AreSameFilePath(GetStoredFilePath(record), sourceFilePath));
    }

    private Dictionary<string, ManagedScriptAssetRecord> BuildFingerprintIndex(ManagedScriptLibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = new Dictionary<string, ManagedScriptAssetRecord>(StringComparer.Ordinal);
        foreach (var record in document.Scripts)
        {
            var fingerprint = EnsureRecordFingerprint(record);
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                continue;
            }

            _ = index.TryAdd(fingerprint, record);
        }

        return index;
    }

    private string EnsureRecordFingerprint(ManagedScriptAssetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!string.IsNullOrWhiteSpace(record.Fingerprint))
        {
            return record.Fingerprint;
        }

        var storedFilePath = GetStoredFilePath(record);
        if (!File.Exists(storedFilePath))
        {
            return string.Empty;
        }

        var scriptDocument = _scriptDocumentService.LoadCompatible(storedFilePath).Document;
        record.Fingerprint = BuildDocumentFingerprint(scriptDocument);
        return record.Fingerprint;
    }

    private static void UpdateRecordFromDocument(
        ManagedScriptAssetRecord record,
        ScriptDocument scriptDocument,
        string sourceFilePath,
        string? displayName,
        string scriptFingerprint,
        bool preserveImportedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(scriptDocument);

        var now = DateTimeOffset.UtcNow;
        record.ScriptId = scriptDocument.Metadata.ScriptId;
        record.DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(record.DisplayName) ? ResolveDisplayName(displayName, sourceFilePath) : record.DisplayName)
            : displayName.Trim();
        record.SourceFileName = Path.GetFileName(sourceFilePath);
        record.Description = scriptDocument.Metadata.Description;
        record.Map = scriptDocument.Metadata.Map;
        record.Difficulty = scriptDocument.Metadata.Difficulty;
        record.Mode = scriptDocument.Metadata.Mode;
        record.Hero = scriptDocument.Metadata.Hero;
        record.Tags = [.. ScriptTagCatalog.NormalizeStoredTags(scriptDocument.Metadata.Tags)];
        record.Fingerprint = scriptFingerprint;
        record.UpdatedAt = now;
        if (!preserveImportedAt)
        {
            record.ImportedAt = now;
        }
    }

    private void EnsureStorage()
    {
        Directory.CreateDirectory(_rootDirectory);
        Directory.CreateDirectory(_assetsDirectory);
        Directory.CreateDirectory(_bindingsDirectory);
    }

    private void RenameRecordScriptId(
        ManagedScriptLibraryDocument document,
        ManagedScriptAssetRecord record,
        string newScriptId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(newScriptId);

        var trimmedScriptId = newScriptId.Trim();
        if (string.Equals(record.ScriptId, trimmedScriptId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var oldScriptId = record.ScriptId;
        var oldFilePath = GetStoredFilePath(record);
        record.ScriptId = trimmedScriptId;
        record.StoredFileName = $"{trimmedScriptId}.btd";
        var newFilePath = GetStoredFilePath(record);

        foreach (var binding in document.Bindings.Where(binding => string.Equals(binding.ScriptId, oldScriptId, StringComparison.OrdinalIgnoreCase)))
        {
            binding.ScriptId = trimmedScriptId;
        }

        RewriteDedicatedBindingFile(_blackBorderBindingsFilePath, oldScriptId, trimmedScriptId);
        RewriteDedicatedBindingFile(_collectionBindingsFilePath, oldScriptId, trimmedScriptId);
        RewriteDedicatedBindingFile(_goldBalloonBindingsFilePath, oldScriptId, trimmedScriptId);

        if (File.Exists(oldFilePath) && !string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(newFilePath))
            {
                File.Delete(newFilePath);
            }

            File.Move(oldFilePath, newFilePath);
        }
    }

    private void RewriteDedicatedBindingFile(string filePath, string oldScriptId, string newScriptId)
    {
        var document = LoadTaskBindingDocument(filePath);
        var changed = false;
        foreach (var key in document.Bindings.Keys.ToList())
        {
            if (!string.Equals(document.Bindings[key], oldScriptId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            document.Bindings[key] = newScriptId;
            changed = true;
        }

        if (changed)
        {
            SaveTaskBindingDocument(filePath, document);
        }
    }

    private IReadOnlyList<ManagedScriptSlotBindingRecord> LoadCurrentBindings(ManagedScriptLibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var bindings = document.Bindings
            .Where(binding => binding.SlotId.Length > 0 &&
                              _slotCatalogService.TryGetById(binding.SlotId, out var slot) &&
                              !TryGetDedicatedBindingFilePath(slot.TaskKind, out _))
            .Select(binding => new ManagedScriptSlotBindingRecord
            {
                SlotId = binding.SlotId,
                ScriptId = binding.ScriptId,
                UpdatedAt = binding.UpdatedAt
            })
            .ToList();

        bindings.AddRange(LoadDedicatedBindingRecords(_blackBorderBindingsFilePath));
        bindings.AddRange(LoadDedicatedBindingRecords(_collectionBindingsFilePath));
        bindings.AddRange(LoadDedicatedBindingRecords(_goldBalloonBindingsFilePath));
        return bindings;
    }

    private IReadOnlyList<ManagedScriptSlotBindingRecord> LoadDedicatedBindingRecords(string filePath)
    {
        return LoadTaskBindingDocument(filePath)
            .Bindings
            .Select(binding => new ManagedScriptSlotBindingRecord
            {
                SlotId = binding.Key,
                ScriptId = binding.Value,
                UpdatedAt = DateTimeOffset.UtcNow
            })
            .ToList();
    }

    private void MigrateDedicatedBindings(ManagedScriptLibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var blackBorderBindings = LoadTaskBindingDocument(_blackBorderBindingsFilePath);
        var collectionBindings = LoadTaskBindingDocument(_collectionBindingsFilePath);
        var goldBalloonBindings = LoadTaskBindingDocument(_goldBalloonBindingsFilePath);
        var manifestBindingsToRemove = new List<ManagedScriptSlotBindingRecord>();

        foreach (var binding in document.Bindings)
        {
            if (!_slotCatalogService.TryGetById(binding.SlotId, out var slot) ||
                !TryGetDedicatedBindingFilePath(slot.TaskKind, out var filePath))
            {
                continue;
            }

            var targetDocument = GetDedicatedBindingDocument(
                filePath,
                blackBorderBindings,
                collectionBindings,
                goldBalloonBindings);

            if (!targetDocument.Bindings.ContainsKey(binding.SlotId))
            {
                targetDocument.Bindings[binding.SlotId] = binding.ScriptId;
            }

            manifestBindingsToRemove.Add(binding);
        }

        if (manifestBindingsToRemove.Count == 0)
        {
            return;
        }

        document.Bindings.RemoveAll(binding => manifestBindingsToRemove.Contains(binding));
        SaveTaskBindingDocument(_blackBorderBindingsFilePath, blackBorderBindings);
        SaveTaskBindingDocument(_collectionBindingsFilePath, collectionBindings);
        SaveTaskBindingDocument(_goldBalloonBindingsFilePath, goldBalloonBindings);
        SaveManifest(document);
    }

    private void RemoveScriptBindingsFromDedicatedFiles(string scriptId)
    {
        RemoveScriptBindingsFromDedicatedFile(_blackBorderBindingsFilePath, scriptId);
        RemoveScriptBindingsFromDedicatedFile(_collectionBindingsFilePath, scriptId);
        RemoveScriptBindingsFromDedicatedFile(_goldBalloonBindingsFilePath, scriptId);
    }

    private void RemoveScriptBindingsFromDedicatedFile(string filePath, string scriptId)
    {
        var document = LoadTaskBindingDocument(filePath);
        var keysToRemove = document.Bindings
            .Where(binding => string.Equals(binding.Value, scriptId, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.Key)
            .ToList();

        if (keysToRemove.Count == 0)
        {
            return;
        }

        foreach (var key in keysToRemove)
        {
            document.Bindings.Remove(key);
        }

        SaveTaskBindingDocument(filePath, document);
    }

    private ManagedScriptTaskBindingDocument BuildTaskBindingTemplate(AutoTaskKind taskKind)
    {
        var bindings = _slotCatalogService
            .GetByTaskKind(taskKind)
            .OrderBy(slot => slot.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(slot => slot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                slot => slot.SlotId,
                static _ => string.Empty,
                StringComparer.OrdinalIgnoreCase);

        return new ManagedScriptTaskBindingDocument
        {
            Version = taskKind == AutoTaskKind.BlackBorder ? 2 : 1,
            Bindings = bindings
        };
    }

    private ManagedScriptTaskBindingDocument GetDedicatedBindingDocument(
        string filePath,
        ManagedScriptTaskBindingDocument blackBorderBindings,
        ManagedScriptTaskBindingDocument collectionBindings,
        ManagedScriptTaskBindingDocument goldBalloonBindings)
    {
        if (string.Equals(filePath, _blackBorderBindingsFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return blackBorderBindings;
        }

        if (string.Equals(filePath, _collectionBindingsFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return collectionBindings;
        }

        if (string.Equals(filePath, _goldBalloonBindingsFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return goldBalloonBindings;
        }

        throw new InvalidOperationException($"Unsupported dedicated binding file '{filePath}'.");
    }

    private bool IsBlackBorderBindingFile(string filePath)
    {
        return string.Equals(
            Path.GetFullPath(filePath),
            Path.GetFullPath(_blackBorderBindingsFilePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private void NormalizeTaskBindingDocument(string filePath, ManagedScriptTaskBindingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Bindings = NormalizeBindingDictionary(document.Bindings);
        document.StageBindings = NormalizeStageBindingDictionary(document.StageBindings);

        if (TryGetTaskKindForBindingFilePath(filePath, out var taskKind))
        {
            var templateBindings = BuildTaskBindingTemplate(taskKind).Bindings;
            foreach (var binding in document.Bindings)
            {
                templateBindings[binding.Key] = binding.Value;
            }

            document.Bindings = templateBindings;
        }

        if (!IsBlackBorderBindingFile(filePath))
        {
            return;
        }

        var mergedBindings = new Dictionary<string, string>(document.Bindings, StringComparer.OrdinalIgnoreCase);
        foreach (var binding in FlattenBlackBorderStageBindings(document.StageBindings))
        {
            mergedBindings[binding.Key] = binding.Value;
        }

        document.Bindings = mergedBindings;
        document.StageBindings = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> NormalizeBindingDictionary(Dictionary<string, string>? bindings)
    {
        return (bindings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> NormalizeStageBindingDictionary(
        Dictionary<string, Dictionary<string, Dictionary<string, string>>>? stageBindings)
    {
        var normalized = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        if (stageBindings is null)
        {
            return normalized;
        }

        foreach (var mapEntry in stageBindings)
        {
            var mapKey = mapEntry.Key?.Trim();
            if (string.IsNullOrWhiteSpace(mapKey))
            {
                continue;
            }

            var normalizedDifficulties = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (mapEntry.Value is not null)
            {
                foreach (var difficultyEntry in mapEntry.Value)
                {
                    var difficultyKey = difficultyEntry.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(difficultyKey))
                    {
                        continue;
                    }

                    var normalizedModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (difficultyEntry.Value is not null)
                    {
                        foreach (var modeEntry in difficultyEntry.Value)
                        {
                            var modeKey = modeEntry.Key?.Trim();
                            if (string.IsNullOrWhiteSpace(modeKey))
                            {
                                continue;
                            }

                            normalizedModes[modeKey] = modeEntry.Value?.Trim() ?? string.Empty;
                        }
                    }

                    normalizedDifficulties[difficultyKey] = normalizedModes;
                }
            }

            normalized[mapKey] = normalizedDifficulties;
        }

        return normalized;
    }

    private Dictionary<string, string> FlattenBlackBorderStageBindings(
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, string>>> stageBindings)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapEntry in stageBindings)
        {
            if (!Enum.TryParse<GameMapType>(mapEntry.Key, ignoreCase: true, out var map))
            {
                continue;
            }

            foreach (var difficultyEntry in mapEntry.Value)
            {
                if (!Enum.TryParse<StageDifficulty>(difficultyEntry.Key, ignoreCase: true, out var difficulty))
                {
                    continue;
                }

                foreach (var modeEntry in difficultyEntry.Value)
                {
                    if (!Enum.TryParse<StageMode>(modeEntry.Key, ignoreCase: true, out var mode))
                    {
                        continue;
                    }

                    var slotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(map, difficulty, mode);
                    bindings[slotId] = modeEntry.Value?.Trim() ?? string.Empty;
                }
            }
        }

        return bindings;
    }

    private bool TryGetDedicatedBindingFilePath(AutoTaskKind taskKind, out string filePath)
    {
        switch (taskKind)
        {
            case AutoTaskKind.BlackBorder:
                filePath = _blackBorderBindingsFilePath;
                return true;
            case AutoTaskKind.Collection:
                filePath = _collectionBindingsFilePath;
                return true;
            case AutoTaskKind.GoldBalloon:
                filePath = _goldBalloonBindingsFilePath;
                return true;
            default:
                filePath = string.Empty;
                return false;
        }
    }

    private bool TryGetTaskKindForBindingFilePath(string filePath, out AutoTaskKind taskKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (string.Equals(
                Path.GetFullPath(filePath),
                Path.GetFullPath(_blackBorderBindingsFilePath),
                StringComparison.OrdinalIgnoreCase))
        {
            taskKind = AutoTaskKind.BlackBorder;
            return true;
        }

        if (string.Equals(
                Path.GetFullPath(filePath),
                Path.GetFullPath(_collectionBindingsFilePath),
                StringComparison.OrdinalIgnoreCase))
        {
            taskKind = AutoTaskKind.Collection;
            return true;
        }

        if (string.Equals(
                Path.GetFullPath(filePath),
                Path.GetFullPath(_goldBalloonBindingsFilePath),
                StringComparison.OrdinalIgnoreCase))
        {
            taskKind = AutoTaskKind.GoldBalloon;
            return true;
        }

        taskKind = default;
        return false;
    }

    private static void NormalizeRecord(ManagedScriptAssetRecord record)
    {
        record.ScriptId = string.IsNullOrWhiteSpace(record.ScriptId)
            ? Guid.NewGuid().ToString("N")
            : record.ScriptId.Trim();
        record.DisplayName = record.DisplayName?.Trim() ?? string.Empty;
        record.SourceFileName = record.SourceFileName?.Trim() ?? string.Empty;
        record.StoredFileName = record.StoredFileName?.Trim() ?? string.Empty;
        record.Fingerprint = record.Fingerprint?.Trim() ?? string.Empty;
        record.Description = record.Description?.Trim() ?? string.Empty;
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

    private static string ResolveDisplayName(string? displayName, string sourceFilePath)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(sourceFilePath)
            : displayName.Trim();
    }

    private static bool AreSameFilePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDocumentFingerprint(ScriptDocument scriptDocument)
    {
        ArgumentNullException.ThrowIfNull(scriptDocument);

        var fingerprintDocument = new ScriptDocument
        {
            Schema = scriptDocument.Schema,
            FormatVersion = scriptDocument.FormatVersion,
            Metadata = new ScriptMetadataDocument
            {
                ScriptId = string.Empty,
                ScriptVersion = scriptDocument.Metadata.ScriptVersion,
                Description = scriptDocument.Metadata.Description,
                Map = scriptDocument.Metadata.Map,
                Difficulty = scriptDocument.Metadata.Difficulty,
                Mode = scriptDocument.Metadata.Mode,
                Hero = scriptDocument.Metadata.Hero,
                Tags = [.. scriptDocument.Metadata.Tags]
            },
            MonkeyObjects = scriptDocument.MonkeyObjects
                .Select(monkeyObject => new ScriptMonkeyObjectDocument
                {
                    BindingId = monkeyObject.BindingId,
                    ObjectId = monkeyObject.ObjectId,
                    SelectionCode = monkeyObject.SelectionCode,
                    PlacementOrder = monkeyObject.PlacementOrder
                })
                .ToList(),
            Instructions = scriptDocument.Instructions
                .Select(instruction => new ScriptInstructionDocument
                {
                    CommandType = instruction.CommandType,
                    SelectedMonkeyTower = instruction.SelectedMonkeyTower,
                    MonkeyBindingId = instruction.MonkeyBindingId,
                    MonkeyObjectId = instruction.MonkeyObjectId,
                    TargetMonkeyBindingId = instruction.TargetMonkeyBindingId,
                    TargetMonkeyObjectId = instruction.TargetMonkeyObjectId,
                    SelectedInventoryItem = instruction.SelectedInventoryItem,
                    SelectedActivatedAbility = instruction.SelectedActivatedAbility,
                    NextRoundAction = instruction.NextRoundAction,
                    WaitMode = instruction.WaitMode,
                    ClickCount = instruction.ClickCount,
                    ClickIntervalMilliseconds = instruction.ClickIntervalMilliseconds,
                    NextRoundSendCount = instruction.NextRoundSendCount,
                    NextRoundOperationIntervalMilliseconds = instruction.NextRoundOperationIntervalMilliseconds,
                    WaitTimeMilliseconds = instruction.WaitTimeMilliseconds,
                    PlacementDetectionEnabled = instruction.PlacementDetectionEnabled,
                    PlacementFailureAdjustmentEnabled = instruction.PlacementFailureAdjustmentEnabled,
                    PlacementAttemptIntervalMilliseconds = instruction.PlacementAttemptIntervalMilliseconds,
                    PlacementAdjustmentAttemptIntervalMilliseconds = instruction.PlacementAdjustmentAttemptIntervalMilliseconds,
                    UpgradeDetectionEnabled = instruction.UpgradeDetectionEnabled,
                    UpgradeOperationIntervalMilliseconds = instruction.UpgradeOperationIntervalMilliseconds,
                    MonkeyPanelDetectionEnabled = instruction.MonkeyPanelDetectionEnabled,
                    MonkeyPanelOperationIntervalMilliseconds = instruction.MonkeyPanelOperationIntervalMilliseconds,
                    SellDetectionEnabled = instruction.SellDetectionEnabled,
                    WaitGoldAmount = instruction.WaitGoldAmount,
                    WaitRoundCount = instruction.WaitRoundCount,
                    PositionX = instruction.PositionX,
                    PositionY = instruction.PositionY,
                    WaitColorCoordinateX = instruction.WaitColorCoordinateX,
                    WaitColorCoordinateY = instruction.WaitColorCoordinateY,
                    UpgradePath = instruction.UpgradePath,
                    UpgradeCount = instruction.UpgradeCount,
                    SwitchDirection = instruction.SwitchDirection,
                    SwitchCount = instruction.SwitchCount,
                    SelectedAbility = instruction.SelectedAbility,
                    RequiresAbilityCoordinate = instruction.RequiresAbilityCoordinate,
                    AbilityCoordinateX = instruction.AbilityCoordinateX,
                    AbilityCoordinateY = instruction.AbilityCoordinateY,
                    WaitColorHex = instruction.WaitColorHex,
                    WaitColorTolerance = instruction.WaitColorTolerance,
                    CommentContent = instruction.CommentContent,
                    Notes = instruction.Notes,
                    IntervalToNextInstructionMs = instruction.IntervalToNextInstructionMs
                })
                .ToList()
        };

        return MD5Helper.ComputeMD5(JsonSerializer.Serialize(fingerprintDocument));
    }

    private static string ResolveLegacyPackageScriptDisplayName(
        LegacyScriptModel legacyDocument,
        string packageDisplayName,
        int index)
    {
        ArgumentNullException.ThrowIfNull(legacyDocument);

        if (!string.IsNullOrWhiteSpace(legacyDocument.Metadata.ScriptName))
        {
            return legacyDocument.Metadata.ScriptName.Trim();
        }

        return $"{packageDisplayName} #{index + 1:000}";
    }
}
