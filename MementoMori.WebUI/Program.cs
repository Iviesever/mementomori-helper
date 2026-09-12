using System.Globalization;
using MementoMori;
using MementoMori.Apis;
using MementoMori.Common;
using MementoMori.Jobs;
using MementoMori.Option;
using MementoMori.WebUI.ViewModels;
using MudBlazor.Services;
using MementoMori.WebUI.Extensions;
using Quartz;
using ReactiveUI;
using MementoMori.WebUI;
using MementoMori.WebUI.UI;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Index = MementoMori.BlazorShared.Pages.Index;
using Ortega.Common.Manager;
using MudBlazor;
using Refit;
using MagicOnion;
using Microsoft.Extensions.FileProviders;

internal class Program
{
    private static readonly TaskCompletionSource<bool> SafeExportInitialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task Main(string[] args)
    {
        // Offline validation exits through its own host before constructing game services.
        if (args.Any(a => a is "--exporter-offline-check" or "--exporter-offline-check=true"))
        {
            await OfflineExportCheck.RunAsync();
            return;
        }
        PortableExportStartup.Prepare(args);
        PlatformRegistrationManager.SetRegistrationNamespaces(RegistrationNamespace.Blazor);
        var builder = WebApplication.CreateBuilder(args);
        IFileProvider physicalProvider = new PhysicalFileProvider(Directory.GetCurrentDirectory());
        builder.Services.AddSingleton(physicalProvider);
        if (!PortableExportStartup.Enabled)
        {
            builder.Configuration.AddJsonFile(physicalProvider, "appsettings.other.json", true, true);
            builder.Configuration.AddJsonFile(physicalProvider, "appsettings.user.json", true, true);
        }
        builder.Services.AddMudServices();
        builder.Services.AddMudMarkdownServices();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMementoMori();
        builder.Services.AddMementoMoriBlazorShared();
        builder.Services.AddMementoMoriWebUI();
        builder.Services.AddHttpClient();
        builder.Services.AddOptions();
        if (PortableExportStartup.Enabled) PortableExportStartup.Configure(builder);
        else
        {
            builder.Services.ConfigureWritable<AuthOption>(builder.Configuration.GetSection("AuthOption"), "appsettings.user.json");
            builder.Services.ConfigureWritable<GameConfig>(builder.Configuration.GetSection("GameConfig"), "appsettings.user.json");
            builder.Services.ConfigureWritable<PlayersOption>(builder.Configuration.GetSection("PlayersOption"), "appsettings.user.json");
        }
        builder.Services.Configure<StaticFileOptions>(opt =>
        {
            opt.HttpsCompression = HttpsCompressionMode.Compress;
            opt.OnPrepareResponse = ctx =>
            {
                var typedHeaders = ctx.Context.Response.GetTypedHeaders();
                typedHeaders.CacheControl = new CacheControlHeaderValue()
                {
                    Public = true,
                    MaxAge = TimeSpan.FromDays(1)
                };
            };
        });
        builder.Services.AddSingleton(sp =>
        {
            var serverUrl = sp.GetRequiredService<IWritableOptions<GameConfig>>().Value.ServerUrl;
            if (string.IsNullOrEmpty(serverUrl)) serverUrl = "https://github.com";
            return RestService.For<IMemeMoriServerApi>(serverUrl);
        });
        builder.Services.AddQuartz();
        if (!PortableExportStartup.Enabled)
            builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
        var app = builder.Build();
        Services.Setup(app.Services);
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
        if (!PortableExportStartup.Enabled) app.UseStaticFiles();
        app.UseAntiforgery();
        var earlySafeExportStart = PortableExportStartup.Enabled || string.Equals(
            Environment.GetEnvironmentVariable("MEMENTOMORI_SAFE_EXPORT_EARLY_START"), "1", StringComparison.Ordinal);
        if (earlySafeExportStart)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/")
                {
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync("safe-export-ui-starting");
                    return;
                }
                if (string.Equals(context.Request.Path.Value, "/safe-export", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await SafeExportInitialization.Task.WaitAsync(TimeSpan.FromMinutes(3), context.RequestAborted);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (TimeoutException)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsJsonAsync(new { error = "initialization_timeout", message = "The helper did not finish initialization within three minutes." });
                        return;
                    }
                    catch (Exception e)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsJsonAsync(new { error = "initialization_failed", errorType = e.GetBaseException().GetType().Name });
                        return;
                    }
                }
                await next();
            });
        }
        SafeExport.Map(app);
        if (!PortableExportStartup.Enabled)
            app.MapRazorComponents<App>().AddAdditionalAssemblies(typeof(Index).Assembly).AddInteractiveServerRenderMode();
        if (!earlySafeExportStart)
        {
            await InitializeAsync(app.Services);
            await app.RunAsync();
            return;
        }
        await app.StartAsync();
        try
        {
            await InitializeAsync(app.Services);
            SafeExportInitialization.TrySetResult(true);
        }
        catch (Exception e) { SafeExportInitialization.TrySetException(e); }
        await app.WaitForShutdownAsync();
    }

    private static async Task InitializeAsync(IServiceProvider sp)
    {
        var accountManager = sp.GetRequiredService<AccountManager>();
        var networkManager = sp.GetRequiredService<MementoNetworkManager>();
        accountManager.MigrateToAccountArray();
        accountManager.CurrentCulture = CultureInfo.CurrentCulture;
        await networkManager.Initialize();
        await networkManager.DownloadMasterCatalog();
        networkManager.SetCultureInfo(CultureInfo.CurrentCulture);
        if (!PortableExportStartup.Enabled) await accountManager.AutoLogin();
    }
}
