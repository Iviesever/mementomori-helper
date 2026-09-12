using Microsoft.Maui.Storage;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MementoMori.Ortega.Share.Data.Auth;
using MementoMori.Funcs;
using MementoMori.Export;

namespace MementoMori.Exporter.Android.Services;

public sealed class MobileSession(IServiceProvider services, MemoryOptions<AuthOption> auth)
{
    private const string StorageKey = "mementomori.exporter.auth.v1";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SavedCredentials credentials = new(
        () => SecureStorage.Default.GetAsync(StorageKey),
        json => SecureStorage.Default.SetAsync(StorageKey, json),
        // Only login configuration is stored here, whether imported or obtained by transfer login.
        // Never clear unreadable encrypted preferences without an explicit user request.
        () => SecureStorage.Default.RemoveAll());
    private AccountManager Manager => services.GetRequiredService<AccountManager>();
    public ExportFileStore ExportFiles { get; } = new(FileSystem.CacheDirectory);
    public IReadOnlyList<AccountInfo> Accounts => auth.Value.Accounts;
    public bool NeedsCredentialRecovery => credentials.NeedsRecovery;
    public long SelectedUserId { get; private set; }
    public long? SelectedWorldId { get; private set; }
    public DateTimeOffset? FetchedAtUtc { get; private set; }
    public bool LastExportHasWarnings { get; private set; }
    public string Summary { get; private set; } = "尚未读取账号数据";

    public async Task LoadSavedAsync()
    {
        await gate.WaitAsync();
        try
        {
            var saved = await credentials.LoadAsync();
            if (saved != null) auth.Replace(saved);
        }
        finally { gate.Release(); }
    }

    public async Task<long> SignInAsync(string code, string password, string? accountName)
    {
        await gate.WaitAsync();
        try
        {
            var parsed = await TransferSignIn.SignInAsync(auth.Value, code, password, accountName,
                credentials, ExchangeClientKeyAsync);
            var userId = TransferSignIn.ParseTransferCode(code);
            try { DropAccounts(); }
            finally { auth.Replace(parsed); }
            return userId;
        }
        finally { gate.Release(); }
    }

    private Task<string> ExchangeClientKeyAsync(long userId, string password) => Task.Run(async () =>
    {
        // A standalone protocol session: never insert a placeholder account into AccountManager.
        // GetClientKey uses the existing createUser -> getComebackUserData -> comebackUser exchange.
        using var network = services.GetRequiredService<MementoNetworkManager>();
        using var funcs = services.GetRequiredService<MementoMoriFuncs>();
        network.UserId = userId;
        network.DisableAutoUpdateMasterData = true;
        funcs.UserId = userId;
        funcs.NetworkManager = network;
        try
        {
            await network.Initialize(_ => { });
            return await funcs.GetClientKey(password);
        }
        finally
        {
            // The inherited functions keep an in-memory message collection. Do not retain it.
            funcs.MesssageList.Clear();
        }
    });

    public async Task ImportAsync(string json)
    {
        await gate.WaitAsync();
        try
        {
            var parsed = await credentials.SaveAsync(json);
            try { DropAccounts(); }
            finally { auth.Replace(parsed); }
        }
        finally { gate.Release(); }
    }

    private void DropAccounts()
    {
        SelectedUserId = 0;
        SelectedWorldId = null;
        FetchedAtUtc = null;
        LastExportHasWarnings = false;
        Summary = "尚未读取账号数据";
        var configured = auth.Value.Accounts.ToList();
        try
        {
            foreach (var pair in Manager.GetAll())
            {
                try
                {
                    pair.Value.Funcs.LoginOk = false;
                    pair.Value.NetworkManager.Dispose();
                    pair.Value.Funcs.Dispose();
                }
                finally { Manager.RemoveAccount(pair.Key); }
            }
        }
        finally
        {
            auth.Update(value => value.Accounts = configured);
            Manager.CurrentUserId = 0;
        }
    }

    public async Task ForgetAsync()
    {
        await gate.WaitAsync();
        try
        {
            credentials.Clear();
            try { DropAccounts(); }
            finally
            {
                // A new transfer login must work immediately after clearing, without restarting.
                auth.Replace(new AuthOption
                {
                    Accounts = new List<AccountInfo>(), AuthUrl = CredentialImport.DefaultAuthUrl,
                    AppVersion = "3.2.2", DeviceToken = "", OSVersion = "Android", ModelName = "Android"
                });
            }
            ExportFiles.Clear();
        }
        finally { gate.Release(); }
    }

    public async Task ConnectAsync(long userId, Func<IReadOnlyList<PlayerDataInfo>, Task<long?>> chooseWorld, IProgress<string> progress)
    {
        await gate.WaitAsync();
        try
        {
            if (!Accounts.Any(a => a.UserId == userId)) throw new InvalidOperationException("请选择已保存的账号。");
            DropAccounts();
            Summary = "正在读取账号";
            Manager.CurrentUserId = userId;
            SelectedUserId = userId;
            var account = Manager.Current;
            account.Funcs.LoginOk = false;
            account.NetworkManager.DisableAutoUpdateMasterData = true;
            var worlds = await Task.Run(async () =>
            {
                progress.Report("连接游戏服务器…已保存的账号无需再输入密码");
                await account.NetworkManager.Initialize(_ => { });
                progress.Report("检查并下载基础数据…首次读取可能较久");
                await account.NetworkManager.DownloadMasterCatalog(_ => { });
                progress.Report("加载角色与装备资料…");
                account.NetworkManager.SetCultureInfo(new CultureInfo("zh-TW"));
                return await account.Funcs.GetPlayerDataInfo();
            });
            if (worlds.Count == 0) throw new InvalidOperationException("账号没有可用的区服。");
            var chosenId = await chooseWorld(worlds);
            if (chosenId == null) { Summary = "已取消选择区服。账号仍已保存在本机，可再次读取。"; return; }
            if (!worlds.Any(w => w.WorldId == chosenId.Value)) throw new InvalidOperationException("区服不匹配。");
            progress.Report("读取所选区服的账号快照…");
            await Task.Run(async () =>
            {
                // Never invoke the inherited automatic login/job-registration entry points.
                await account.NetworkManager.Login(chosenId.Value, _ => { });
                await account.Funcs.UserGetUserData();
            });
            var data = account.Funcs.UserSyncData;
            if (data.UserStatusDtoInfo == null) throw new InvalidOperationException("服务器没有返回有效账号资料。");
            account.Funcs.LoginOk = true;
            SelectedWorldId = chosenId.Value;
            FetchedAtUtc = DateTimeOffset.UtcNow;
            Summary = $"区服 {chosenId.Value} · {data.UserStatusDtoInfo.Name} · Rank {data.UserStatusDtoInfo.Rank} · {data.UserCharacterDtoInfos?.Count ?? 0} 个角色";
            progress.Report("账号读取完成，可以选择内容导出");
        }
        catch
        {
            FetchedAtUtc = null;
            SelectedWorldId = null;
            Summary = "读取未完成，请重试。已保存的账号仍会保留。";
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task<string> ExportAsync(long expectedUserId, string[] sections, bool splitZip, bool pretty)
    {
        await gate.WaitAsync();
        try
        {
            LastExportHasWarnings = false;
            if (FetchedAtUtc is not { } fetched || SelectedUserId != expectedUserId ||
                Manager.CurrentUserId != expectedUserId || !Manager.Current.Funcs.LoginOk)
                throw new InvalidOperationException("请先读取当前所选账号。");
            var applicationVersion = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;
            var snapshot = await Task.Run(() => MobileSnapshot.BuildAsync(Manager, sections, fetched, applicationVersion));
            var bytes = splitZip ? MobileSnapshot.Zip(snapshot, sections) : MobileSnapshot.SerializeSafe(snapshot, pretty);
            var hasWarnings = ExportMetadata.HasWarnings(snapshot);
            var path = await ExportFiles.WriteAsync(bytes, splitZip);
            LastExportHasWarnings = hasWarnings;
            return path;
        }
        finally { gate.Release(); }
    }
}
