using System.IO;
using System.IO.Compression;
using System.Text.Json;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;

namespace BetterBTD.Services.MyScripts;

public sealed class BlackBorderScriptSubscriptionService
{
    private const string ManifestEntryName = "blackborder-subscription.json";
    private const string ScriptsDirectoryName = "scripts";

    private static readonly Lazy<BlackBorderScriptSubscriptionService> InstanceHolder =
        new(() => new BlackBorderScriptSubscriptionService(
            ManagedScriptLibraryService.Instance,
            ManagedScriptSlotCatalogService.Instance));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ManagedScriptLibraryService _managedScriptLibraryService;
    private readonly ManagedScriptSlotCatalogService _slotCatalogService;

    internal BlackBorderScriptSubscriptionService(
        ManagedScriptLibraryService managedScriptLibraryService,
        ManagedScriptSlotCatalogService slotCatalogService)
    {
        _managedScriptLibraryService = managedScriptLibraryService ?? throw new ArgumentNullException(nameof(managedScriptLibraryService));
        _slotCatalogService = slotCatalogService ?? throw new ArgumentNullException(nameof(slotCatalogService));
    }

    public static BlackBorderScriptSubscriptionService Instance => InstanceHolder.Value;

    public void Export(string targetFilePath, BlackBorderSubscriptionDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        ArgumentNullException.ThrowIfNull(descriptor);

        var snapshot = _managedScriptLibraryService.GetSnapshot();
        var scopedSlots = ResolveScopedSlots(snapshot, descriptor);
        if (scopedSlots.Count == 0)
        {
            throw new InvalidOperationException("No black border slots matched the selected subscription scope.");
        }

        var uniqueScripts = scopedSlots
            .Where(slot => slot.BoundScript is not null)
            .Select(slot => slot.BoundScript!)
            .GroupBy(script => script.ScriptId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(script => script.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var fileNamesByScriptId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var directory = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
        foreach (var script in uniqueScripts)
        {
            var safeFileName = SanitizeFileName(script.DisplayName);
            var entryFileName = $"{safeFileName}-{script.ScriptId}.btd";
            var archiveEntryName = $"{ScriptsDirectoryName}/{entryFileName}";
            archive.CreateEntryFromFile(script.StoredFilePath, archiveEntryName);
            fileNamesByScriptId[script.ScriptId] = entryFileName;
        }

        var manifest = new BlackBorderScriptSubscriptionDocument
        {
            SubscriptionType = descriptor.ExportType.ToString(),
            MapCategory = descriptor.Category?.ToString() ?? string.Empty,
            Map = descriptor.Map?.ToString() ?? string.Empty,
            Scripts = uniqueScripts
                .Select(script => new CollectionScriptSubscriptionScriptDocument
                {
                    ScriptId = script.ScriptId,
                    DisplayName = script.DisplayName,
                    FileName = fileNamesByScriptId[script.ScriptId]
                })
                .ToList(),
            Bindings = scopedSlots.ToDictionary(
                slot => slot.Definition.SlotId,
                slot => slot.BoundScriptId,
                StringComparer.OrdinalIgnoreCase)
        };

        var manifestEntry = archive.CreateEntry(ManifestEntryName);
        using var writer = new StreamWriter(manifestEntry.Open());
        writer.Write(JsonSerializer.Serialize(new
        {
            manifest.Kind,
            manifest.Version,
            manifest.SubscriptionType,
            manifest.MapCategory,
            manifest.Map,
            manifest.Scripts,
            manifest.Bindings
        }, JsonOptions));
    }

    public void Import(string sourceFilePath, IProgress<SubscriptionImportProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Black border subscription package was not found.", sourceFilePath);
        }

        using var archive = ZipFile.OpenRead(sourceFilePath);
        var manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("Black border subscription manifest was not found.");
        BlackBorderScriptSubscriptionDocument manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
        {
            manifest = JsonSerializer.Deserialize<BlackBorderScriptSubscriptionDocument>(reader.ReadToEnd(), JsonOptions)
                       ?? throw new InvalidDataException("Black border subscription manifest is invalid.");
        }

        ValidateManifest(manifest);
        progress?.Report(new SubscriptionImportProgress
        {
            ProcessedScriptCount = 0,
            TotalScriptCount = manifest.Scripts.Count
        });

        var importedScriptIdsByScriptId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"betterbtd-blackborder-subscription-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            foreach (var script in manifest.Scripts)
            {
                var archiveEntry = archive.GetEntry($"{ScriptsDirectoryName}/{script.FileName}")
                    ?? throw new InvalidDataException($"Script payload '{script.FileName}' was not found.");
                var extractedFilePath = Path.Combine(tempRoot, script.FileName);
                archiveEntry.ExtractToFile(extractedFilePath, overwrite: true);

                var imported = _managedScriptLibraryService.UpsertScript(
                    extractedFilePath,
                    scriptId: script.ScriptId,
                    displayName: script.DisplayName);
                if (!string.Equals(imported.ScriptId, script.ScriptId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Script '{script.FileName}' script ID does not match manifest.");
                }

                importedScriptIdsByScriptId[script.ScriptId] = imported.ScriptId;
                progress?.Report(new SubscriptionImportProgress
                {
                    ProcessedScriptCount = importedScriptIdsByScriptId.Count,
                    TotalScriptCount = manifest.Scripts.Count
                });
            }

            foreach (var binding in manifest.Bindings)
            {
                if (!_slotCatalogService.TryGetById(binding.Key, out var slot) || slot.TaskKind != AutoTaskKind.BlackBorder)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(binding.Value))
                {
                    _managedScriptLibraryService.SetBinding(binding.Key, null);
                    continue;
                }

                if (!importedScriptIdsByScriptId.TryGetValue(binding.Value, out var localScriptId))
                {
                    throw new InvalidDataException($"Binding target script ID '{binding.Value}' was not imported.");
                }

                _managedScriptLibraryService.SetBinding(binding.Key, localScriptId);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    public static bool IsBlackBorderSubscriptionPackage(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(sourceFilePath);
            return archive.GetEntry(ManifestEntryName) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static List<ManagedScriptSlotEntry> ResolveScopedSlots(
        ManagedScriptLibrarySnapshot snapshot,
        BlackBorderSubscriptionDescriptor descriptor)
    {
        var blackBorderSlots = snapshot.Slots
            .Where(slot => slot.Definition.TaskKind == AutoTaskKind.BlackBorder && slot.Definition.StageTarget is not null)
            .ToList();

        return descriptor.ExportType switch
        {
            BlackBorderSubscriptionExportType.BeginnerMaps =>
                blackBorderSlots.Where(slot => GetMapTier(slot) == MapDifficultyTier.Beginner).ToList(),
            BlackBorderSubscriptionExportType.IntermediateMaps =>
                blackBorderSlots.Where(slot => GetMapTier(slot) == MapDifficultyTier.Intermediate).ToList(),
            BlackBorderSubscriptionExportType.AdvancedMaps =>
                blackBorderSlots.Where(slot => GetMapTier(slot) == MapDifficultyTier.Advanced).ToList(),
            BlackBorderSubscriptionExportType.ExpertMaps =>
                blackBorderSlots.Where(slot => GetMapTier(slot) == MapDifficultyTier.Expert).ToList(),
            BlackBorderSubscriptionExportType.SingleMap when descriptor.Map is not null =>
                blackBorderSlots.Where(slot => slot.Definition.StageTarget!.Map == descriptor.Map.Value).ToList(),
            BlackBorderSubscriptionExportType.SingleMap =>
                throw new InvalidOperationException("A map must be selected for single-map subscription export."),
            _ => throw new InvalidOperationException($"Unsupported black border subscription export type '{descriptor.ExportType}'.")
        };
    }

    private static MapDifficultyTier GetMapTier(ManagedScriptSlotEntry slot)
    {
        var mapType = slot.Definition.StageTarget!.Map;
        return GameElementCatalog.Maps.First(map => map.Type == mapType).Tier;
    }

    private static void ValidateManifest(BlackBorderScriptSubscriptionDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!string.Equals(manifest.Kind, "better-btd/blackborder-subscription", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported black border subscription kind '{manifest.Kind}'.");
        }

        manifest.Scripts ??= [];
        manifest.Bindings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Enum.TryParse<BlackBorderSubscriptionExportType>(manifest.SubscriptionType, ignoreCase: true, out var exportType))
        {
            throw new InvalidDataException($"Unsupported black border subscription type '{manifest.SubscriptionType}'.");
        }

        if (exportType == BlackBorderSubscriptionExportType.SingleMap &&
            !Enum.TryParse<GameMapType>(manifest.Map, ignoreCase: true, out _))
        {
            throw new InvalidDataException("Single-map black border subscription does not specify a valid map.");
        }

        foreach (var script in manifest.Scripts)
        {
            if (string.IsNullOrWhiteSpace(script.ScriptId) || string.IsNullOrWhiteSpace(script.FileName))
            {
                throw new InvalidDataException("Black border subscription contains an invalid script entry.");
            }
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string((value ?? string.Empty)
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "script" : sanitized;
    }
}
