#if EXPORTER_DEVICE_TESTS
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace MementoMori.Exporter.Android.Services;

// Compiled only into the separate *.devicetest Debug package. Never uses a game account.
internal static class DeviceSmoke
{
    private const string Key = "exporter.synthetic-smoke.v1";
    private const string FakeConfig = """{"AuthOption":{"UserId":123,"ClientKey":"synthetic-device-test-only"}}""";
    public static async Task RunAsync(MainActivity activity, string phase)
    {
        var checks = new List<string>();
        var resultPath = Path.Combine(FileSystem.AppDataDirectory, "device-smoke.json");
        try
        {
            if (!AppInfo.Current.PackageName.EndsWith(".devicetest", StringComparison.Ordinal)) throw new InvalidOperationException();
            if (ExportSelection.DefaultSections().Length != 8) throw new InvalidOperationException();
            checks.Add("all-eight-default-sections");
            var credentials = new SavedCredentials(() => SecureStorage.Default.GetAsync(Key),
                json => SecureStorage.Default.SetAsync(Key, json), () => SecureStorage.Default.Remove(Key));
            var files = new ExportFileStore(FileSystem.CacheDirectory);
            var pathRecord = Path.Combine(FileSystem.AppDataDirectory, "synthetic-export-path.txt");
            if (phase == "write")
            {
                credentials.Clear();
                await credentials.SaveAsync(FakeConfig);
                var path = await files.WriteAsync(Encoding.UTF8.GetBytes("{\"synthetic\":true}"), false);
                await File.WriteAllTextAsync(pathRecord, path);
                checks.Add("real-android-secure-storage-write");
                checks.Add("real-private-export-write");
            }
            else if (phase is "restore" or "upgrade-restore")
            {
                var saved = await credentials.LoadAsync();
                if (saved?.Accounts.Single().UserId != 123) throw new InvalidOperationException();
                var path = await File.ReadAllTextAsync(pathRecord);
                using var copy = new MemoryStream();
                await files.CopyToAsync(path, copy);
                if (Encoding.UTF8.GetString(copy.ToArray()) != "{\"synthetic\":true}") throw new InvalidOperationException();
                credentials.Clear();
                if (await credentials.LoadAsync() != null) throw new InvalidOperationException();
                files.Clear();
                File.Delete(pathRecord);
                checks.Add(phase == "upgrade-restore" ? "secure-storage-survives-package-upgrade" : "secure-storage-survives-process-restart");
                if (phase == "upgrade-restore" && AppInfo.Current.BuildString != "1002") throw new InvalidOperationException();
                checks.Add("export-copy-and-explicit-clear");
            }
            else if (phase == "picker-cancel")
            {
                await File.WriteAllTextAsync(resultPath, "{\"state\":\"waiting-for-picker-cancel\"}");
                var destination = await activity.ChooseExportDestinationAsync("synthetic-export.json", "application/json");
                if (destination != null) throw new InvalidOperationException();
                checks.Add("real-document-picker-cancel-releases-waiter");
            }
            else throw new ArgumentException();
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = true, phase, checks, versionCode = AppInfo.Current.BuildString, realGameLogin = false }));
        }
        catch (Exception e)
        {
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = false, phase, errorType = e.GetType().Name }));
        }
    }
}
#endif
