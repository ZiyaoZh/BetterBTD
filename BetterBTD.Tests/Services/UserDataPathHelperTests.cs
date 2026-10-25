using System.Security.AccessControl;
using System.Security.Principal;
using BetterBTD.Helpers;
using BetterBTD.Services.MyScripts;

namespace BetterBTD.Tests.Services;

public sealed class UserDataPathHelperTests
{
    [Fact]
    public void FreshInstall_CreatesUserDirectoryAndUsableEmptyScriptLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"betterbtd-paths-{Guid.NewGuid():N}");
        try
        {
            var installedUser = Path.Combine(root, "install", "User");
            var localUser = Path.Combine(root, "local", "User");
            var selectedRoot = UserDataPathHelper.ResolveUserRootDirectory(installedUser, localUser);
            Assert.Equal(installedUser, selectedRoot);
            Assert.False(Directory.Exists(localUser));
            Assert.Empty(Directory.EnumerateFileSystemEntries(selectedRoot));

            AssertUsableScriptLibrary(selectedRoot);
            var rulesPath = UserDataPathHelper.ResolveFilePath(selectedRoot, "AutoTasks", "rules.json");
            File.WriteAllText(rulesPath, "{}");
            Assert.True(File.Exists(rulesPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedInstall_UsesLocalDataAndPreservesExistingLibrary(bool hasExistingData)
    {
        var root = Path.Combine(Path.GetTempPath(), $"betterbtd-paths-{Guid.NewGuid():N}");
        var installation = Directory.CreateDirectory(Path.Combine(root, "install"));
        var originalSecurity = installation.GetAccessControl();
        var installedUser = Path.Combine(installation.FullName, "User");
        var localUser = Path.Combine(root, "local", "User");
        var scriptId = string.Empty;
        try
        {
            if (hasExistingData)
            {
                var service = CreateLibrary(installedUser);
                var sourceFile = Path.Combine(root, "example.btd");
                ScriptDocumentService.Instance.Save(sourceFile, new());
                scriptId = service.ImportScript(sourceFile).ScriptId;
                File.WriteAllText(Path.Combine(installedUser, "MyScripts", "marker.txt"), "original");
            }

            var identity = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("Current Windows user has no SID.");
            var security = installation.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.Write,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Deny));
            installation.SetAccessControl(security);

            // Verify the test really exercises the same access failure as a protected install.
            Assert.Throws<UnauthorizedAccessException>(() =>
                UserDataPathHelper.ResolveDirectory(installedUser, "write-denied"));
            Assert.Equal(localUser, UserDataPathHelper.ResolveUserRootDirectory(installedUser, localUser));
            AssertUsableScriptLibrary(localUser);
            if (hasExistingData)
            {
                var migrated = CreateLibrary(localUser).GetSnapshot();
                Assert.Contains(migrated.Scripts, asset => asset.ScriptId == scriptId);
                Assert.True(File.Exists(Path.Combine(installedUser, "MyScripts", "library.json")));

                // Later launches must not overwrite edited local data with the old installation copy.
                var marker = Path.Combine(localUser, "MyScripts", "marker.txt");
                File.WriteAllText(marker, "keep");
                Assert.Equal(localUser, UserDataPathHelper.ResolveUserRootDirectory(installedUser, localUser));
                Assert.Equal("keep", File.ReadAllText(marker));
            }

            // An elevated child session or a later permission change must not select the old library.
            installation.SetAccessControl(originalSecurity);
            Assert.Equal(localUser, UserDataPathHelper.ResolveUserRootDirectory(installedUser, localUser));
            AssertUsableScriptLibrary(localUser);
        }
        finally
        {
            installation.SetAccessControl(originalSecurity);
            Directory.Delete(root, recursive: true);
        }
    }

    private static ManagedScriptLibraryService CreateLibrary(string userRoot) => new(
        UserDataPathHelper.ResolveDirectory(userRoot, "MyScripts"),
        ScriptDocumentService.Instance,
        ManagedScriptSlotCatalogService.Instance);

    private static void AssertUsableScriptLibrary(string userRoot)
    {
        var library = CreateLibrary(userRoot);
        library.GetSnapshot();
        Assert.True(File.Exists(Path.Combine(userRoot, "MyScripts", "library.json")));
        Assert.True(Directory.Exists(Path.Combine(userRoot, "MyScripts", "Assets")));
        Assert.True(Directory.Exists(Path.Combine(userRoot, "MyScripts", "Bindings")));
    }
}
