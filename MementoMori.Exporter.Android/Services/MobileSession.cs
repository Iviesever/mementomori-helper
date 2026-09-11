using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MementoMori.Ortega.Share.Data.Auth;

namespace MementoMori.Exporter.Android.Services;

public sealed class MobileSession(IServiceProvider services, MemoryOptions<AuthOption> auth)
{
    private const string StorageKey = "mementomori.exporter.auth.v1";
    private readonly SemaphoreSlim gate = new(1, 1);
    private AccountManager Manager => services.GetRequiredService<AccountManager>();
    public IReadOnlyList<AccountInfo> Accounts => auth.Value.Accounts;
    public long SelectedUserId { get; private set; }
    public DateTimeOffset? FetchedAtUtc { get; private set; }
    public string Summary { get; private set; } = "尚未读取账号数据";

    public async Task LoadSavedAsync()
    {
        var saved = await SecureStorage.Default.GetAsync(StorageKey);
        if (!string.IsNullOrWhiteSpace(saved)) auth.Replace(CredentialImport.Parse(saved));
    }
    public async Task ImportAsync(string json)
    {
        var parsed = CredentialImport.Parse(json);
        await gate.WaitAsync();
        try
        {
            // Persist before replacing memory. A secure-storage failure keeps the old account usable.
            await SecureStorage.Default.SetAsync(StorageKey, CredentialImport.Serialize(parsed));
            DropAccounts();
            auth.Replace(parsed);
        }
        finally { gate.Release(); }
    }
    private void DropAccounts()
    {
        foreach (var pair in Manager.GetAll())
        {
            pair.Value.NetworkManager.Dispose();
            pair.Value.Funcs.Dispose();
            Manager.RemoveAccount(pair.Key);
        }
        Manager.CurrentUserId = 0;
        SelectedUserId = 0;
        FetchedAtUtc = null;
        Summary = "尚未读取账号数据";
    }
    public async Task ForgetAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!SecureStorage.Default.Remove(StorageKey)) { /* No saved key is also a valid cleared state. */ }
            DropAccounts();
            auth.Replace(new AuthOption { Accounts = new List<AccountInfo>() });
            var shareDir = Path.Combine(FileSystem.CacheDirectory, "sharing-root");
            if (Directory.Exists(shareDir)) Directory.Delete(shareDir, true);
        }
        finally { gate.Release(); }
    }
    public async Task ConnectAsync(long userId, Func<IReadOnlyList<PlayerDataInfo>, Task<long?>> chooseWorld, IProgress<string> progress)
    {
        await gate.WaitAsync();
        try
        {
            FetchedAtUtc = null;
            Summary = "正在读取账号";
            if (!Accounts.Any(a => a.UserId == userId)) throw new InvalidOperationException("请选择已导入的账号。");
            Manager.CurrentUserId = userId;
            SelectedUserId = userId;
            var account = Manager.Current;
            account.Funcs.LoginOk = false;
            account.NetworkManager.DisableAutoUpdateMasterData = true;
            var worlds = await Task.Run(async () =>
            {
                progress.Report("连接游戏服务器…");
                await account.NetworkManager.Initialize(_ => { });
                progress.Report("检查并下载基础数据…首次读取可能较久");
                await account.NetworkManager.DownloadMasterCatalog(_ => { });
                progress.Report("加载角色与装备资料…");
                account.NetworkManager.SetCultureInfo(new CultureInfo("zh-TW"));
                return await account.Funcs.GetPlayerDataInfo();
            });
            if (worlds.Count == 0) throw new InvalidOperationException("账号没有可用的区服。");
            var chosenId = await chooseWorld(worlds);
            if (chosenId == null) { Summary = "已取消选择区服"; return; }
            if (!worlds.Any(w => w.WorldId == chosenId.Value)) throw new InvalidOperationException("区服不匹配。");
            progress.Report("读取所选区服的账号快照…");
            await Task.Run(async () =>
            {
                // Bypass Funcs.AutoLogin/Login/AuthLogin: those register jobs and fetch unrelated pages.
                await account.NetworkManager.Login(chosenId.Value, _ => { });
                await account.Funcs.UserGetUserData();
            });
            var data = account.Funcs.UserSyncData;
            if (data.UserStatusDtoInfo == null) throw new InvalidOperationException("服务器没有返回有效账号资料。");
            account.Funcs.LoginOk = true;
            FetchedAtUtc = DateTimeOffset.UtcNow;
            Summary = $"{data.UserStatusDtoInfo.Name} · Rank {data.UserStatusDtoInfo.Rank} · {data.UserCharacterDtoInfos?.Count ?? 0} 个角色";
            progress.Report("账号读取完成，可以选择内容导出");
        }
        catch { FetchedAtUtc = null; Summary = "读取未完成，请重试"; throw; }
        finally { gate.Release(); }
    }
    public async Task<string> ExportAsync(string[] sections, bool splitZip, bool pretty)
    {
        await gate.WaitAsync();
        try
        {
            if (FetchedAtUtc is not { } fetched || !Manager.Current.Funcs.LoginOk)
                throw new InvalidOperationException("请先读取账号。");
            // Deliberately keep session snapshot semantics. Per-export refresh is a deferred issue.
            var snapshot = await Task.Run(() => MobileSnapshot.BuildAsync(Manager, sections, fetched));
            var bytes = splitZip ? MobileSnapshot.Zip(snapshot, sections) : MobileSnapshot.SerializeSafe(snapshot, pretty);
            var folder = Path.Combine(FileSystem.CacheDirectory, "sharing-root");
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "mementomori-account-*"))
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1)) File.Delete(old);
            var path = Path.Combine(folder, $"mementomori-account-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.{(splitZip ? "zip" : "json")}");
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        finally { gate.Release(); }
    }
}
