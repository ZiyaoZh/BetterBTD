using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services.MyScripts;
using System.IO.Compression;
using System.Text.Json;

namespace BetterBTD.Tests.Services;

public sealed class ManagedScriptLibraryServiceTests
{
    [Fact]
    public void ImportBindExportFlow_StoresManagedScriptAndResolvesSlotBinding()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "sample-script.btd");
        var exportFilePath = Path.Combine(rootDirectory, "export", "sample-script-copy.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            ScriptDocumentService.Instance.Save(sourceFilePath, CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border", "custom-tag"]));

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var imported = service.ImportScript(sourceFilePath);
            var blackBorderSlotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);

            service.SetBinding(blackBorderSlotId, imported.ScriptId);
            service.ExportScript(imported.ScriptId, exportFilePath);

            var snapshot = service.GetSnapshot();
            var script = Assert.Single(snapshot.Scripts);
            var slot = snapshot.Slots.First(x => x.Definition.SlotId == blackBorderSlotId);

            Assert.Equal("sample-script", script.DisplayName);
            Assert.Equal(GameMapType.MonkeyMeadow, script.Map);
            Assert.Equal(StageDifficulty.Easy, script.Difficulty);
            Assert.Equal(StageMode.Standard, script.Mode);
            Assert.Equal(1, script.BindingCount);
            Assert.False(script.HasMissingFile);
            Assert.Equal(imported.ScriptId, slot.BoundScriptId);
            Assert.NotNull(slot.BoundScript);
            Assert.True(File.Exists(exportFilePath));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void RemoveScript_ClearsExistingBindings()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "custom-script.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            ScriptDocumentService.Instance.Save(sourceFilePath, CreateDocument(
                GameMapType.DarkCastle,
                StageDifficulty.Hard,
                StageMode.CHIMPS,
                ["black-border"]));

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var imported = service.ImportScript(sourceFilePath);
            var slotId = ManagedScriptSlotIdFactory.CreateCustomDefaultSlotId();
            service.SetBinding(slotId, imported.ScriptId);

            var removed = service.RemoveScript(imported.ScriptId);
            var snapshot = service.GetSnapshot();
            var slot = snapshot.Slots.First(x => x.Definition.SlotId == slotId);

            Assert.True(removed);
            Assert.Empty(snapshot.Scripts);
            Assert.False(slot.HasBinding);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ImportLegacyScriptCollection_ConvertsAndImportsEachLegacyScript()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var packageFilePath = Path.Combine(rootDirectory, "source", "legacy-package.btd6s");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(packageFilePath)!);
            File.WriteAllText(packageFilePath, JsonSerializer.Serialize(new[]
            {
                CreateLegacyDocument("legacy-standard", GameMapType.MonkeyMeadow, StageDifficulty.Easy, StageMode.Standard),
                CreateLegacyDocument("legacy-chimps", GameMapType.DarkCastle, StageDifficulty.Hard, StageMode.CHIMPS)
            }));

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var imported = service.ImportLegacyScriptCollection(packageFilePath);
            var snapshot = service.GetSnapshot();

            Assert.Equal(2, imported.Count);
            Assert.Equal(2, snapshot.Scripts.Count);
            Assert.All(snapshot.Scripts, script => Assert.Equal("legacy-package.btd6s", script.SourceFileName));
            Assert.Contains(snapshot.Scripts, script => script.DisplayName == "legacy-standard" &&
                                                        script.Map == GameMapType.MonkeyMeadow &&
                                                        script.Difficulty == StageDifficulty.Easy &&
                                                        script.Mode == StageMode.Standard);
            Assert.Contains(snapshot.Scripts, script => script.DisplayName == "legacy-chimps" &&
                                                        script.Map == GameMapType.DarkCastle &&
                                                        script.Difficulty == StageDifficulty.Hard &&
                                                        script.Mode == StageMode.CHIMPS);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ImportScript_SkipsDuplicateWhenFingerprintMatches()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var firstFilePath = Path.Combine(rootDirectory, "source", "first-script.btd");
        var secondFilePath = Path.Combine(rootDirectory, "source", "second-script.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(firstFilePath)!);
            var document = CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border"]);
            ScriptDocumentService.Instance.Save(firstFilePath, document);
            ScriptDocumentService.Instance.Save(secondFilePath, document);

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var firstImported = service.ImportScript(firstFilePath);
            var secondImported = service.ImportScript(secondFilePath);
            var snapshot = service.GetSnapshot();

            Assert.Single(snapshot.Scripts);
            Assert.Equal(firstImported.ScriptId, secondImported.ScriptId);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ImportLegacyScriptCollection_SkipsDuplicateWhenFingerprintMatches()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var packageFilePath = Path.Combine(rootDirectory, "source", "legacy-package.btd6s");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(packageFilePath)!);
            var duplicateScript = CreateLegacyDocument("legacy-standard", GameMapType.MonkeyMeadow, StageDifficulty.Easy, StageMode.Standard);
            File.WriteAllText(packageFilePath, JsonSerializer.Serialize(new[]
            {
                duplicateScript,
                duplicateScript
            }));

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var imported = service.ImportLegacyScriptCollection(packageFilePath);
            var snapshot = service.GetSnapshot();

            Assert.Single(imported);
            Assert.Single(snapshot.Scripts);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ImportScript_BackfillsMissingFingerprintAndStillSkipsDuplicate()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var firstFilePath = Path.Combine(rootDirectory, "source", "first-script.btd");
        var secondFilePath = Path.Combine(rootDirectory, "source", "second-script.btd");
        var managedRootDirectory = Path.Combine(rootDirectory, "managed");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(firstFilePath)!);
            var document = CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border"]);
            ScriptDocumentService.Instance.Save(firstFilePath, document);
            ScriptDocumentService.Instance.Save(secondFilePath, document);

            var service = new ManagedScriptLibraryService(
                managedRootDirectory,
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            _ = service.ImportScript(firstFilePath);

            var manifestFilePath = Path.Combine(managedRootDirectory, "library.json");
            var manifest = JsonSerializer.Deserialize<ManagedScriptLibraryDocument>(File.ReadAllText(manifestFilePath));
            Assert.NotNull(manifest);
            Assert.Single(manifest.Scripts);
            manifest.Scripts[0].Fingerprint = string.Empty;
            File.WriteAllText(manifestFilePath, JsonSerializer.Serialize(manifest));

            var duplicateImported = service.ImportScript(secondFilePath);
            var snapshot = service.GetSnapshot();
            var updatedManifest = JsonSerializer.Deserialize<ManagedScriptLibraryDocument>(File.ReadAllText(manifestFilePath));

            Assert.Single(snapshot.Scripts);
            Assert.NotNull(updatedManifest);
            Assert.Single(updatedManifest.Scripts);
            Assert.False(string.IsNullOrWhiteSpace(updatedManifest.Scripts[0].Fingerprint));
            Assert.Equal(snapshot.Scripts[0].ScriptId, duplicateImported.ScriptId);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExportScript_PreservesScriptId()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "script-id-script.btd");
        var exportFilePath = Path.Combine(rootDirectory, "export", "script-id-script-copy.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            var document = CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["collection"]);
            ScriptDocumentService.Instance.Save(sourceFilePath, document);

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var imported = service.ImportScript(sourceFilePath);
            service.ExportScript(imported.ScriptId, exportFilePath);

            var exported = ScriptDocumentService.Instance.Load(exportFilePath);
            Assert.Equal(imported.ScriptId, exported.Metadata.ScriptId);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ImportScript_ReusesManagedRecordWhenScriptIdMatches()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var firstFilePath = Path.Combine(rootDirectory, "source", "script-id-first.btd");
        var secondFilePath = Path.Combine(rootDirectory, "source", "script-id-second.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(firstFilePath)!);
            var document = CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["collection"]);
            ScriptDocumentService.Instance.Save(firstFilePath, document);

            var mutated = CreateDocument(
                GameMapType.DarkCastle,
                StageDifficulty.Hard,
                StageMode.CHIMPS,
                ["collection", "updated"]);
            mutated.Metadata.ScriptId = document.Metadata.ScriptId;
            ScriptDocumentService.Instance.Save(secondFilePath, mutated);

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var firstImported = service.ImportScript(firstFilePath);
            var secondImported = service.ImportScript(secondFilePath);
            var snapshot = service.GetSnapshot();
            var script = Assert.Single(snapshot.Scripts);

            Assert.Equal(firstImported.ScriptId, secondImported.ScriptId);
            Assert.Equal(document.Metadata.ScriptId, script.ScriptId);
            Assert.Equal(GameMapType.DarkCastle, script.Map);
            Assert.Equal(StageDifficulty.Hard, script.Difficulty);
            Assert.Equal(StageMode.CHIMPS, script.Mode);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void CollectionSubscription_ExportImport_RestoresBindingsByScriptId()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");
        var exportPackagePath = Path.Combine(rootDirectory, "export", "collection.btdsub");

        try
        {
            Directory.CreateDirectory(sourceDirectory);

            var sourceScriptPath = Path.Combine(sourceDirectory, "collection-script.btd");
            var sourceDocument = CreateDocument(
                GameMapType.DarkCastle,
                StageDifficulty.Hard,
                StageMode.CHIMPS,
                ["collection"]);
            ScriptDocumentService.Instance.Save(sourceScriptPath, sourceDocument);

            var sourceService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-source"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var sourceImported = sourceService.ImportScript(sourceScriptPath);
            var slotId = ManagedScriptSlotIdFactory.CreateCollectionSlotId("simple", GameMapType.DarkCastle);
            sourceService.SetBinding(slotId, sourceImported.ScriptId);

            var subscriptionService = new CollectionScriptSubscriptionService(
                sourceService,
                ManagedScriptSlotCatalogService.Instance);
            subscriptionService.Export(exportPackagePath);

            var targetService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-target"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var targetSubscriptionService = new CollectionScriptSubscriptionService(
                targetService,
                ManagedScriptSlotCatalogService.Instance);
            targetSubscriptionService.Import(exportPackagePath);

            var resolved = targetService.TryResolveSlotBinding(slotId, out var resolvedScriptId, out var resolvedFilePath);
            Assert.True(resolved);
            Assert.False(string.IsNullOrWhiteSpace(resolvedScriptId));
            Assert.True(File.Exists(resolvedFilePath));

            var importedDocument = ScriptDocumentService.Instance.Load(resolvedFilePath);
            Assert.Equal(sourceDocument.Metadata.ScriptId, importedDocument.Metadata.ScriptId);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void CollectionSubscription_Export_OverwritesExistingFile()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");
        var exportPackagePath = Path.Combine(rootDirectory, "export", "collection.btdsub");

        try
        {
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(exportPackagePath)!);
            File.WriteAllText(exportPackagePath, "placeholder");

            var sourceScriptPath = Path.Combine(sourceDirectory, "collection-script.btd");
            var sourceDocument = CreateDocument(
                GameMapType.DarkCastle,
                StageDifficulty.Hard,
                StageMode.CHIMPS,
                ["collection"]);
            ScriptDocumentService.Instance.Save(sourceScriptPath, sourceDocument);

            var sourceService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-source"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var sourceImported = sourceService.ImportScript(sourceScriptPath);
            var slotId = ManagedScriptSlotIdFactory.CreateCollectionSlotId("simple", GameMapType.DarkCastle);
            sourceService.SetBinding(slotId, sourceImported.ScriptId);

            var subscriptionService = new CollectionScriptSubscriptionService(
                sourceService,
                ManagedScriptSlotCatalogService.Instance);

            subscriptionService.Export(exportPackagePath);

            Assert.True(File.Exists(exportPackagePath));
            Assert.NotEqual("placeholder", File.ReadAllText(exportPackagePath));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void CollectionSubscription_Import_IntoEmptyLibrary_CreatesCompleteBindingTemplate()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");
        var exportPackagePath = Path.Combine(rootDirectory, "export", "collection-complete-template.btdsub");

        try
        {
            Directory.CreateDirectory(sourceDirectory);

            var sourceScriptPath = Path.Combine(sourceDirectory, "collection-script.btd");
            var sourceDocument = CreateDocument(
                GameMapType.DarkCastle,
                StageDifficulty.Hard,
                StageMode.CHIMPS,
                ["collection"]);
            ScriptDocumentService.Instance.Save(sourceScriptPath, sourceDocument);

            var boundSlotId = ManagedScriptSlotIdFactory.CreateCollectionSlotId("simple", GameMapType.DarkCastle);

            var sourceService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-source"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var sourceImported = sourceService.ImportScript(sourceScriptPath);
            sourceService.SetBinding(boundSlotId, sourceImported.ScriptId);

            var sourceSubscriptionService = new CollectionScriptSubscriptionService(
                sourceService,
                ManagedScriptSlotCatalogService.Instance);
            sourceSubscriptionService.Export(exportPackagePath);

            var targetService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-target"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var targetSubscriptionService = new CollectionScriptSubscriptionService(
                targetService,
                ManagedScriptSlotCatalogService.Instance);
            targetSubscriptionService.Import(exportPackagePath);

            var bindingFilePath = targetService.GetTaskBindingFilePath(AutoTaskKind.Collection);
            var bindingDocument = JsonSerializer.Deserialize<ManagedScriptTaskBindingDocument>(File.ReadAllText(bindingFilePath));
            Assert.NotNull(bindingDocument);

            var expectedSlotIds = ManagedScriptSlotCatalogService.Instance
                .GetByTaskKind(AutoTaskKind.Collection)
                .Select(slot => slot.SlotId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualSlotIds = bindingDocument.Bindings.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Equal(expectedSlotIds.Count, bindingDocument.Bindings.Count);
            Assert.True(expectedSlotIds.SetEquals(actualSlotIds));
            Assert.Equal(sourceDocument.Metadata.ScriptId, bindingDocument.Bindings[boundSlotId]);

            var unboundSlotId = expectedSlotIds.First(slotId => !string.Equals(slotId, boundSlotId, StringComparison.OrdinalIgnoreCase));
            Assert.True(bindingDocument.Bindings.ContainsKey(unboundSlotId));
            Assert.Equal(string.Empty, bindingDocument.Bindings[unboundSlotId]);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BlackBorderSubscription_Export_OverwritesExistingFile()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");
        var exportPackagePath = Path.Combine(rootDirectory, "export", "blackborder.btdsub");

        try
        {
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(exportPackagePath)!);
            File.WriteAllText(exportPackagePath, "placeholder");

            var sourceScriptPath = Path.Combine(sourceDirectory, "blackborder-script.btd");
            var sourceDocument = CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border"]);
            ScriptDocumentService.Instance.Save(sourceScriptPath, sourceDocument);

            var sourceService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-source"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var sourceImported = sourceService.ImportScript(sourceScriptPath);
            var slotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);
            sourceService.SetBinding(slotId, sourceImported.ScriptId);

            var subscriptionService = new BlackBorderScriptSubscriptionService(
                sourceService,
                ManagedScriptSlotCatalogService.Instance);

            subscriptionService.Export(
                exportPackagePath,
                new BlackBorderSubscriptionDescriptor
                {
                    ExportType = BlackBorderSubscriptionExportType.SingleMap,
                    Map = GameMapType.MonkeyMeadow
                });

            Assert.True(File.Exists(exportPackagePath));
            Assert.NotEqual("placeholder", File.ReadAllText(exportPackagePath));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BlackBorderSubscription_ExportImport_RestoresBindingsByScriptId()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");
        var exportPackagePath = Path.Combine(rootDirectory, "export", "blackborder.btdsub");

        try
        {
            Directory.CreateDirectory(sourceDirectory);

            var sourceScriptPath = Path.Combine(sourceDirectory, "blackborder-script.btd");
            var sourceDocument = CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border"]);
            ScriptDocumentService.Instance.Save(sourceScriptPath, sourceDocument);

            var slotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);

            var sourceService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-source"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var sourceImported = sourceService.ImportScript(sourceScriptPath);
            sourceService.SetBinding(slotId, sourceImported.ScriptId);

            var subscriptionService = new BlackBorderScriptSubscriptionService(
                sourceService,
                ManagedScriptSlotCatalogService.Instance);
            subscriptionService.Export(
                exportPackagePath,
                new BlackBorderSubscriptionDescriptor
                {
                    ExportType = BlackBorderSubscriptionExportType.SingleMap,
                    Map = GameMapType.MonkeyMeadow
                });

            var targetService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-target"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var targetSubscriptionService = new BlackBorderScriptSubscriptionService(
                targetService,
                ManagedScriptSlotCatalogService.Instance);
            targetSubscriptionService.Import(exportPackagePath);

            var resolved = targetService.TryResolveSlotBinding(slotId, out var resolvedScriptId, out var resolvedFilePath);
            Assert.True(resolved);
            Assert.False(string.IsNullOrWhiteSpace(resolvedScriptId));
            Assert.True(File.Exists(resolvedFilePath));

            var importedDocument = ScriptDocumentService.Instance.Load(resolvedFilePath);
            Assert.Equal(sourceDocument.Metadata.ScriptId, importedDocument.Metadata.ScriptId);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BlackBorderSubscription_Import_IntoEmptyLibrary_CreatesCompleteBindingTemplate()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");
        var exportPackagePath = Path.Combine(rootDirectory, "export", "blackborder-complete-template.btdsub");

        try
        {
            Directory.CreateDirectory(sourceDirectory);

            var sourceScriptPath = Path.Combine(sourceDirectory, "blackborder-script.btd");
            var sourceDocument = CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border"]);
            ScriptDocumentService.Instance.Save(sourceScriptPath, sourceDocument);

            var boundSlotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);

            var sourceService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-source"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var sourceImported = sourceService.ImportScript(sourceScriptPath);
            sourceService.SetBinding(boundSlotId, sourceImported.ScriptId);

            var sourceSubscriptionService = new BlackBorderScriptSubscriptionService(
                sourceService,
                ManagedScriptSlotCatalogService.Instance);
            sourceSubscriptionService.Export(
                exportPackagePath,
                new BlackBorderSubscriptionDescriptor
                {
                    ExportType = BlackBorderSubscriptionExportType.SingleMap,
                    Map = GameMapType.MonkeyMeadow
                });

            var targetService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-target"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var targetSubscriptionService = new BlackBorderScriptSubscriptionService(
                targetService,
                ManagedScriptSlotCatalogService.Instance);
            targetSubscriptionService.Import(exportPackagePath);

            var bindingFilePath = targetService.GetTaskBindingFilePath(AutoTaskKind.BlackBorder);
            var bindingDocument = JsonSerializer.Deserialize<ManagedScriptTaskBindingDocument>(File.ReadAllText(bindingFilePath));
            Assert.NotNull(bindingDocument);

            var expectedSlotIds = ManagedScriptSlotCatalogService.Instance
                .GetByTaskKind(AutoTaskKind.BlackBorder)
                .Select(slot => slot.SlotId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualSlotIds = bindingDocument.Bindings.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Equal(expectedSlotIds.Count, bindingDocument.Bindings.Count);
            Assert.True(expectedSlotIds.SetEquals(actualSlotIds));
            Assert.Equal(sourceDocument.Metadata.ScriptId, bindingDocument.Bindings[boundSlotId]);

            var unboundSlotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.DarkCastle,
                StageDifficulty.Hard,
                StageMode.CHIMPS);
            Assert.True(bindingDocument.Bindings.ContainsKey(unboundSlotId));
            Assert.Equal(string.Empty, bindingDocument.Bindings[unboundSlotId]);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BlackBorderBindingFile_UsesFlatBindingsShape()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "blackborder-flat.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            ScriptDocumentService.Instance.Save(sourceFilePath, CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border"]));

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var imported = service.ImportScript(sourceFilePath);
            var slotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);
            service.SetBinding(slotId, imported.ScriptId);

            var bindingFilePath = service.GetTaskBindingFilePath(AutoTaskKind.BlackBorder);
            var bindingJson = File.ReadAllText(bindingFilePath);
            var bindingDocument = JsonSerializer.Deserialize<ManagedScriptTaskBindingDocument>(File.ReadAllText(bindingFilePath));

            Assert.NotNull(bindingDocument);
            Assert.True(bindingDocument.Bindings.TryGetValue(slotId, out var boundScriptId));
            Assert.Equal(imported.ScriptId, boundScriptId);
            Assert.Empty(bindingDocument.StageBindings);
            Assert.DoesNotContain("StageBindings", bindingJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BlackBorderBindingFile_LegacyStageBindingsRemainReadable()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "blackborder-legacy.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            var sourceDocument = CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border"]);
            ScriptDocumentService.Instance.Save(sourceFilePath, sourceDocument);

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var imported = service.ImportScript(sourceFilePath);

            var slotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);
            var bindingFilePath = service.GetTaskBindingFilePath(AutoTaskKind.BlackBorder);
            Directory.CreateDirectory(Path.GetDirectoryName(bindingFilePath)!);
            File.WriteAllText(bindingFilePath, $$"""
            {
              "Version": 2,
              "StageBindings": {
                "MonkeyMeadow": {
                  "Easy": {
                    "Standard": "{{imported.ScriptId}}"
                  }
                }
              }
            }
            """);

            var resolved = service.TryResolveSlotBinding(slotId, out var resolvedScriptId, out var resolvedFilePath);

            Assert.True(resolved);
            Assert.Equal(imported.ScriptId, resolvedScriptId);
            Assert.True(File.Exists(resolvedFilePath));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BlackBorderSubscription_Export_UsesFlatBindingsShape()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");
        var exportPackagePath = Path.Combine(rootDirectory, "export", "blackborder-flat.btdsub");

        try
        {
            Directory.CreateDirectory(sourceDirectory);

            var sourceScriptPath = Path.Combine(sourceDirectory, "blackborder-script.btd");
            ScriptDocumentService.Instance.Save(sourceScriptPath, CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border"]));

            var sourceService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed-source"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var sourceImported = sourceService.ImportScript(sourceScriptPath);
            var slotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);
            sourceService.SetBinding(slotId, sourceImported.ScriptId);

            var subscriptionService = new BlackBorderScriptSubscriptionService(
                sourceService,
                ManagedScriptSlotCatalogService.Instance);
            subscriptionService.Export(
                exportPackagePath,
                new BlackBorderSubscriptionDescriptor
                {
                    ExportType = BlackBorderSubscriptionExportType.SingleMap,
                    Map = GameMapType.MonkeyMeadow
                });

            using var archive = ZipFile.OpenRead(exportPackagePath);
            var manifestEntry = archive.GetEntry("blackborder-subscription.json");
            Assert.NotNull(manifestEntry);
            using var reader = new StreamReader(manifestEntry.Open());
            var manifestJson = reader.ReadToEnd();

            Assert.Contains(slotId, manifestJson, StringComparison.Ordinal);
            Assert.DoesNotContain("StageBindings", manifestJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void SlotCatalog_ContainsExpectedFrameworkSlots()
    {
        var catalog = ManagedScriptSlotCatalogService.Instance;
        var slots = catalog.GetAll();

        var blackBorderCount = GameElementCatalog.Maps.Count * 14;
        var collectionCount = ManagedScriptCollectionModeCatalog.Modes.Count *
                              GameElementCatalog.Maps.Count(map => map.Tier == MapDifficultyTier.Expert);
        var goldBalloonCount = GameElementCatalog.Maps.Count(map => map.Tier == MapDifficultyTier.Beginner);
        var expectedCount = blackBorderCount + collectionCount + goldBalloonCount + 2;

        Assert.Equal(expectedCount, slots.Count);
        Assert.Contains(slots, x => x.SlotId == ManagedScriptSlotIdFactory.CreateCustomDefaultSlotId());
        Assert.Contains(slots, x => x.SlotId == ManagedScriptSlotIdFactory.CreateRaceCurrentSlotId());
        Assert.Contains(slots, x => x.SlotId == ManagedScriptSlotIdFactory.CreateGoldBalloonSlotId(GameMapType.MonkeyMeadow));
        Assert.Contains(GameElementCatalog.Maps, x => x.Type == GameMapType.SkullTweak && x.Tier == MapDifficultyTier.Beginner);
        Assert.Contains(slots, x => x.SlotId == ManagedScriptSlotIdFactory.CreateGoldBalloonSlotId(GameMapType.SkullTweak));
        Assert.Contains(slots, x => x.SlotId == ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
            GameMapType.MonkeyMeadow,
            StageDifficulty.Easy,
            StageMode.Standard));
    }

    private static ScriptDocument CreateDocument(
        GameMapType map,
        StageDifficulty difficulty,
        StageMode mode,
        IReadOnlyList<string> tags)
    {
        return new ScriptDocument
        {
            Metadata = new ScriptMetadataDocument
            {
                Map = map.ToString(),
                Difficulty = difficulty.ToString(),
                Mode = mode.ToString(),
                Hero = HeroType.Quincy.ToString(),
                Tags = [.. tags]
            }
        };
    }

    private static LegacyScriptModel CreateLegacyDocument(
        string scriptName,
        GameMapType map,
        StageDifficulty difficulty,
        StageMode mode)
    {
        return new LegacyScriptModel
        {
            Metadata = new LegacyScriptMetadata
            {
                Version = "1.1",
                ScriptName = scriptName,
                SelectedMap = (int)Enum.Parse<LegacyMapType>(map.ToString()),
                SelectedDifficulty = (int)Enum.Parse<LegacyLevelDifficulty>(difficulty.ToString()),
                SelectedMode = mode == StageMode.MagicOnly
                    ? (int)LegacyLevelMode.MagicMonkeysOnly
                    : (int)Enum.Parse<LegacyLevelMode>(mode.ToString()),
                SelectedHero = (int)LegacyHeroType.Quincy
            },
            InstructionsList = [],
            MonkeyCounts = [],
            MonkeyIds = []
        };
    }
}
