using System.Security.Cryptography;
using System.Text;
using MementoMori.Exporter.Android.Services;
using Xunit;

namespace Exporter.Android.Tests;

public sealed class MobileStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mm-export-tests-" + Guid.NewGuid().ToString("N"));
    private const string Valid = """{"AuthOption":{"Accounts":[{"UserId":123,"ClientKey":"fake-test-key","Name":"Test"}]}}""";

    [Fact]
    public async Task EmptySecureStorageDoesNotRequireRecovery()
    {
        var store = new SavedCredentials(() => Task.FromResult<string?>(null), _ => Task.CompletedTask, () => { });
        Assert.Null(await store.LoadAsync());
        Assert.False(store.NeedsRecovery);
    }

    [Fact]
    public async Task UnreadableSecureStorageIsRecoverableAndNeverSilentlyDeleted()
    {
        var removed = false;
        var store = new SavedCredentials(() => throw new CryptographicException("fake failure"),
            _ => Task.CompletedTask, () => removed = true);
        await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync());
        Assert.True(store.NeedsRecovery);
        Assert.False(removed);
        store.Clear();
        Assert.True(removed);
        Assert.False(store.NeedsRecovery);
    }

    [Fact]
    public async Task MalformedSavedConfigurationCanBeReplacedByValidImport()
    {
        string? saved = "not JSON";
        var removed = false;
        var store = new SavedCredentials(() => Task.FromResult(saved),
            value => { saved = value; return Task.CompletedTask; }, () => removed = true);
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
        Assert.True(store.NeedsRecovery);
        var imported = await store.SaveAsync(Valid);
        Assert.False(store.NeedsRecovery);
        Assert.False(removed);
        Assert.Equal(123L, Assert.Single(imported.Accounts).UserId);
        Assert.Equal(123L, Assert.Single((await store.LoadAsync())!.Accounts).UserId);
    }

    [Fact]
    public async Task InvalidImportDoesNotWriteSecureStorage()
    {
        var writes = 0;
        var store = new SavedCredentials(() => Task.FromResult<string?>(Valid),
            _ => { writes++; return Task.CompletedTask; }, () => { });
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync("{}"));
        Assert.Equal(0, writes);
        Assert.Equal(123L, Assert.Single((await store.LoadAsync())!.Accounts).UserId);
    }

    [Fact]
    public async Task FailedSecureWriteDoesNotReturnReplacementAccount()
    {
        var store = new SavedCredentials(() => Task.FromResult<string?>(Valid),
            _ => throw new IOException("fake failure"), () => { });
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Valid.Replace("123", "456")));
        Assert.Equal(123L, Assert.Single((await store.LoadAsync())!.Accounts).UserId);
    }

    [Fact]
    public async Task FailedClearDoesNotClaimRecoverySucceeded()
    {
        var store = new SavedCredentials(() => throw new CryptographicException(),
            _ => Task.CompletedTask, () => throw new IOException());
        await Assert.ThrowsAsync<CryptographicException>(() => store.LoadAsync());
        Assert.Throws<IOException>(() => store.Clear());
        Assert.True(store.NeedsRecovery);
    }

    [Fact]
    public async Task PersistedImportContainsNoDesktopAutomationSettings()
    {
        string? saved = null;
        var store = new SavedCredentials(() => Task.FromResult(saved),
            value => { saved = value; return Task.CompletedTask; }, () => { });
        await store.SaveAsync("""{"AuthOption":{"UserId":123,"ClientKey":"fake","AutoLogin":true},"GameConfig":{"ReportBattleLog":true}}""");
        Assert.DoesNotContain("GameConfig", saved!);
        Assert.False(Assert.Single((await store.LoadAsync())!.Accounts).AutoLogin);
    }

    [Theory]
    [InlineData(false, ".json", "application/json")]
    [InlineData(true, ".zip", "application/zip")]
    public async Task ExportIsPublishedCompletelyAndCopiedWithoutClosingDestination(bool zip, string extension, string mime)
    {
        var store = new ExportFileStore(root);
        var bytes = Encoding.UTF8.GetBytes("fake exported bytes 中文");
        var path = await store.WriteAsync(bytes, zip);
        Assert.EndsWith(extension, path);
        Assert.Equal(mime, ExportFileStore.MimeType(path));
        Assert.True(store.IsAvailable(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.partial"));
        using var destination = new MemoryStream();
        await store.CopyToAsync(path, destination);
        Assert.Equal(bytes, destination.ToArray());
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task CancelledGenerationDoesNotLeaveAFile()
    {
        var store = new ExportFileStore(root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync(new byte[32], false, cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(root, "sharing-root")));
    }

    [Theory]
    [InlineData("appsettings.user.json")]
    [InlineData("mementomori-account-fake.txt")]
    [InlineData("mementomori-account-fake.json.partial")]
    [InlineData("../mementomori-account-fake.json")]
    [InlineData("subfolder/mementomori-account-fake.json")]
    public async Task RejectsPrivateFilesIncompleteWritesAndPathsOutsideExportDirectory(string name)
    {
        var store = new ExportFileStore(root);
        var good = await store.WriteAsync(new byte[] { 1 }, false);
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(good)!, name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "fake private data");
        Assert.Throws<InvalidOperationException>(() => store.Validate(path));
        Assert.False(store.IsAvailable(path));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CopyToAsync(path, output));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task ExpiredExportsArePrunedWithoutDeletingRecentOrUnrelatedFiles()
    {
        var store = new ExportFileStore(root);
        var old = await store.WriteAsync(new byte[] { 1 }, false);
        var recent = await store.WriteAsync(new byte[] { 2 }, true);
        var unrelated = Path.Combine(Path.GetDirectoryName(old)!, "keep.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));
        await store.WriteAsync(new byte[] { 3 }, false);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task ClearingTemporaryExportsDoesNotDeleteOtherAppFilesOrSavedCopies()
    {
        var store = new ExportFileStore(root);
        var path = await store.WriteAsync(new byte[] { 1 }, false);
        var savedCopy = Path.Combine(root, "saved-copy.json");
        var privateFile = Path.Combine(root, "appsettings.user.json");
        File.Copy(path, savedCopy);
        await File.WriteAllTextAsync(privateFile, "fake private data");
        store.Clear();
        Assert.False(store.IsAvailable(path));
        Assert.True(File.Exists(savedCopy));
        Assert.True(File.Exists(privateFile));
        store.Clear(); // Idempotent even after cache eviction.
    }

    [Fact]
    public async Task DeletedTemporaryFileCannotBeSharedAgain()
    {
        var store = new ExportFileStore(root);
        var path = await store.WriteAsync(new byte[] { 1 }, false);
        File.Delete(path);
        Assert.False(store.IsAvailable(path));
        Assert.Throws<FileNotFoundException>(() => store.Validate(path));
    }

    [Fact]
    public async Task FailedDestinationDoesNotDamageOriginalExport()
    {
        var store = new ExportFileStore(root);
        var path = await store.WriteAsync(new byte[] { 1, 2, 3 }, false);
        using var readOnly = new MemoryStream(new byte[1], writable: false);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CopyToAsync(path, readOnly));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ConcurrentGenerationsNeverOverwriteEachOther()
    {
        var store = new ExportFileStore(root);
        var paths = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => store.WriteAsync(new[] { (byte)i }, false)));
        Assert.Equal(8, paths.Distinct().Count());
        for (var index = 0; index < paths.Length; index++)
            Assert.Equal(new[] { (byte)index }, await File.ReadAllBytesAsync(paths[index]));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
