using System.Globalization;

namespace MementoMori.WebUI;

// Packaging/startup diagnostic, NOT a successful account login or an alternate export endpoint.
// The real entry point takes this branch before constructing any game services.
internal static class OfflineExportCheck
{
    public static async Task RunAsync()
    {
        var rawPort = Environment.GetEnvironmentVariable("MEMENTOMORI_OFFLINE_CHECK_PORT") ?? "5001";
        if (!int.TryParse(rawPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1024 or > 65535)
            throw new ArgumentException("Invalid offline-check port.");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(), ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration.Sources.Clear();
        // UseUrls writes host configuration. Retain a writable provider without loading
        // appsettings, user secrets or arbitrary ASPNETCORE_URLS/Kestrel environment values.
        builder.Configuration.AddInMemoryCollection();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-MementoMori-Validation"] = "offline-no-account";
            await next();
        });
        SafeExportUi.Map(app);
        app.MapGet("/", () => Results.Text("offline-no-account"));
        app.MapGet("/safe-export", () => Results.Json(new { error = "offline_validation_mode" }, statusCode: 503));
        await app.RunAsync();
    }
}
