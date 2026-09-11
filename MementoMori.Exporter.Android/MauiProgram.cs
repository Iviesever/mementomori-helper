using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using MementoMori.Apis;
using MementoMori.Exporter.Android.Services;
using MementoMori.Option;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Refit;

namespace MementoMori.Exporter.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // The inherited error logger can print raw game requests. Never send them to logcat.
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        Directory.SetCurrentDirectory(FileSystem.AppDataDirectory);
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Logging.ClearProviders();
        builder.Services.AddMementoMori();
        builder.Services.AddMementoMoriBlazorShared();
        builder.Services.AddHttpClient();
        builder.Services.AddOptions();
        builder.Services.AddSingleton(new MemoryOptions<AuthOption>(new AuthOption
        {
            AuthUrl = CredentialImport.DefaultAuthUrl, AppVersion = "3.2.2", DeviceToken = "",
            OSVersion = "Android", ModelName = "Android"
        }));
        builder.Services.AddSingleton<IWritableOptions<AuthOption>>(sp => sp.GetRequiredService<MemoryOptions<AuthOption>>());
        builder.Services.AddSingleton<IWritableOptions<GameConfig>>(new MemoryOptions<GameConfig>(new GameConfig
        {
            AutoJob = new GameConfig.AutoJobModel { DisableAll = true },
            AutoRequestDelay = 200, RecordBattleLog = false, ReportBattleLog = false
        }));
        builder.Services.AddSingleton<IWritableOptions<PlayersOption>>(new MemoryOptions<PlayersOption>(new PlayersOption()));
        // Required by the inherited service graph; no reporting calls are made by this app.
        builder.Services.AddSingleton(RestService.For<IMemeMoriServerApi>("https://github.com"));
        builder.Services.AddQuartz();
        // Intentionally NO AddQuartzHostedService and NO automatic login/job registration.
        builder.Services.AddSingleton<MobileSession>();
        builder.Services.AddSingleton<MainPage>();
        var app = builder.Build();
        MementoMori.Common.Services.Setup(app.Services);
        return app;
    }
}
