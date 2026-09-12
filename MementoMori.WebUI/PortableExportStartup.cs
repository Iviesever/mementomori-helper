using MementoMori.Exporter.Android.Services;
using MementoMori.Option;

namespace MementoMori.WebUI;

internal static class PortableExportStartup
{
    public static bool Enabled { get; private set; }
    public static bool OfflineCheck { get; private set; }

    public static void Prepare(string[] args)
    {
        OfflineCheck = args.Contains("--exporter-offline-check", StringComparer.Ordinal);
        Enabled = OfflineCheck || args.Contains("--exporter-portable", StringComparer.Ordinal);
        if (!Enabled) return;
        var folder = Environment.GetEnvironmentVariable("MEMENTOMORI_EXPORTER_DATA_DIR");
        if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MementoMoriExporter");
        Directory.CreateDirectory(folder);
        Directory.SetCurrentDirectory(folder);
        // Core network errors can contain request bodies. The portable exporter never logs those.
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
    }

    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        // A portable export session must not inherit host endpoints or reporting settings.
        builder.Configuration.Sources.Clear();
        var rawPort = Environment.GetEnvironmentVariable("MEMENTOMORI_EXPORTER_PORT") ?? "5001";
        if (!int.TryParse(rawPort, out var port) || port is < 1024 or > 65535)
            throw new InvalidDataException("Invalid local exporter port.");
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        AuthOption auth;
        if (OfflineCheck)
        {
            // Do not even read the user's configuration during an offline installation check.
            auth = new AuthOption { AuthUrl = CredentialImport.DefaultAuthUrl, Accounts = new() };
        }
        else
        {
            try
            {
                var file = new FileInfo("appsettings.user.json");
                if (!file.Exists || file.Length > CredentialImport.MaxBytes) throw new InvalidDataException();
                auth = CredentialImport.Parse(File.ReadAllText(file.FullName));
            }
            catch { throw new InvalidDataException("A valid private exporter login configuration is required."); }
        }
        builder.Services.AddSingleton<IWritableOptions<AuthOption>>(new MemoryOptions<AuthOption>(auth));
        builder.Services.AddSingleton<IWritableOptions<GameConfig>>(new MemoryOptions<GameConfig>(new GameConfig
        {
            AutoJob = new GameConfig.AutoJobModel { DisableAll = true },
            AutoRequestDelay = 200, RecordBattleLog = false, ReportBattleLog = false
        }));
        builder.Services.AddSingleton<IWritableOptions<PlayersOption>>(new MemoryOptions<PlayersOption>(new PlayersOption()));
    }
}
