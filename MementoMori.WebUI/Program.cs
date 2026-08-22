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
        PlatformRegistrationManager.SetRegistrationNamespaces(RegistrationNamespace.Blazor);
        var builder = WebApplication.CreateBuilder(args);

        IFileProvider physicalProvider = new PhysicalFileProvider(Directory.GetCurrentDirectory());
        builder.Services.AddSingleton(physicalProvider);

        builder.Configuration.AddJsonFile(physicalProvider, "appsettings.other.json", true, true);
        builder.Configuration.AddJsonFile(physicalProvider, "appsettings.user.json", true, true);

        builder.Services.AddMudServices();
        builder.Services.AddMudMarkdownServices();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddMementoMori();
        builder.Services.AddMementoMoriBlazorShared();
        builder.Services.AddMementoMoriWebUI();
        builder.Services.AddHttpClient();

        builder.Services.AddOptions();
        builder.Services.ConfigureWritable<AuthOption>(builder.Configuration.GetSection("AuthOption"), "appsettings.user.json");
        builder.Services.ConfigureWritable<GameConfig>(builder.Configuration.GetSection("GameConfig"), "appsettings.user.json");
        builder.Services.ConfigureWritable<PlayersOption>(builder.Configuration.GetSection("PlayersOption"), "appsettings.user.json");
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
        builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
        var app = builder.Build();
        Services.Setup(app.Services);

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");

        app.UseStaticFiles();
        app.UseAntiforgery();

        var earlySafeExportStart = string.Equals(
            Environment.GetEnvironmentVariable("MEMENTOMORI_SAFE_EXPORT_EARLY_START"),
            "1",
            StringComparison.Ordinal);

        if (earlySafeExportStart)
        {
            // Interactive safe-export mode starts Kestrel before the helper finishes
            // network/master-data/login initialization. This lets the local export UI
            // appear immediately. A real /safe-export request waits here until the
            // normal initialization sequence has completed, preventing a second login
            // flow from racing the startup initialization.
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.Equals("/safe-export", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await SafeExportInitialization.Task.WaitAsync(
                            TimeSpan.FromMinutes(3),
                            context.RequestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (TimeoutException)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "initialization_timeout",
                            message = "The helper did not finish initialization within three minutes."
                        });
                        return;
                    }
                    catch (Exception e)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "initialization_failed",
                            errorType = e.GetBaseException().GetType().Name
                        });
                        return;
                    }
                }

                await next();
            });
        }

        SafeExport.Map(app);
        app.MapRazorComponents<App>()
            .AddAdditionalAssemblies(typeof(Index).Assembly)
            .AddInteractiveServerRenderMode();

        if (!earlySafeExportStart)
        {
            await InitializeAsync(app.Services);
            await app.RunAsync();
            return;
        }

        // In interactive export mode, start listening first so the browser UI can
        // open while master-data refresh and account login continue in parallel.
        await app.StartAsync();

        try
        {
            await InitializeAsync(app.Services);
            SafeExportInitialization.TrySetResult(true);
        }
        catch (Exception e)
        {
            SafeExportInitialization.TrySetException(e);
        }

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
        await accountManager.AutoLogin();
    }
}
