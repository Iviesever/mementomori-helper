using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Text;
using System.Text.Json;
using MementoMori.Exporter.Android.Services;
using MementoMori.Ortega.Share.Data.Auth;
using Microsoft.Maui.Controls.Shapes;

namespace MementoMori.Exporter.Android;

public sealed class MainPage : ContentPage
{
    private readonly MobileSession session;
    private readonly Picker accounts = new() { Title = "选择账号", TextColor = Color.FromArgb("#172033") };
    private readonly Label status = Body("导入一次登录配置，之后直接在手机上读取和导出。");
    private readonly Label snapshot = Body("尚未读取账号");
    private readonly Label output = Body("默认生成一个精简 JSON，可直接分享给分析工具。");
    private readonly ActivityIndicator spinner = new() { Color = Color.FromArgb("#4565BA"), IsVisible = false };
    private readonly VerticalStackLayout sectionPanel = new() { Spacing = 0 };
    private readonly Dictionary<string, CheckBox> sections = new();
    private readonly Switch zip = new();
    private readonly Switch pretty = new();
    private readonly Button import = ActionButton("导入登录配置");
    private readonly Button read = ActionButton("读取账号");
    private readonly Button export = ActionButton("导出并分享");
    private readonly Button shareAgain = ActionButton("再次分享上一个文件", secondary: true);
    private readonly Button forget = ActionButton("清除本机登录配置", secondary: true);
    private bool appeared;
    private bool busy;
    private string? lastExport;
    private long operation;

    public MainPage(MobileSession session)
    {
        this.session = session;
        Title = "账号导出器";
        BackgroundColor = Color.FromArgb("#F4F6FB");
        accounts.ItemDisplayBinding = new Binding(nameof(AccountInfo.Name));
        accounts.SelectedIndexChanged += (_, _) => UpdateButtons();
        zip.Toggled += (_, _) => UpdateButtons();

        var accountCard = Card(
            Heading("01  账号"),
            Body("首次使用：导入 appsettings.user.json。它是私有登录配置，不是之前导出的游戏数据文件。请勿发到聊天或 GitHub。"),
            import, accounts, read, spinner, status, snapshot);

        var names = new (string Key, string Label)[]
        {
            ("player", "玩家资料"), ("progress", "主线进度"), ("levelLink", "等级链接"),
            ("characters", "角色与属性"), ("equipment", "装备与符石"), ("decks", "各模式队伍"),
            ("items", "背包与资源"), ("gacha", "卡池信息（额外联网读取列表）")
        };
        foreach (var entry in names)
        {
            var check = new CheckBox { IsChecked = entry.Key != "gacha", Color = Color.FromArgb("#4565BA") };
            sections.Add(entry.Key, check);
            check.CheckedChanged += (_, _) => UpdateButtons();
            var text = Body(entry.Label);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => { if (!busy) check.IsChecked = !check.IsChecked; };
            text.GestureRecognizers.Add(tap);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
                { new() { Width = 48 }, new() { Width = GridLength.Star } }, MinimumHeightRequest = 48 };
            text.VerticalOptions = LayoutOptions.Center;
            row.Add(check, 0, 0); row.Add(text, 1, 0);
            sectionPanel.Children.Add(row);
        }
        var sectionsCard = Card(Heading("02  选择导出内容"), sectionPanel);
        var optionsCard = Card(Heading("03  生成文件"),
            Toggle("按内容分文件，打包为 ZIP", zip), Toggle("JSON 易读排版（文件会更大）", pretty),
            export, shareAgain, output,
            Body("重复导出使用上次读取的账号快照。游戏内变更后，请再次点“读取账号”；文件生成时间不代表数据读取时间。卡池列表单独读取。"));
        var footer = Card(Body("独立运行，不需要电脑在线。不提供自动抽卡、领奖或战斗入口。读取时建议先完成游戏内操作，避免并发登录影响会话。"), forget);
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Spacing = 14, Padding = new Thickness(18, 18, 18, 32),
            Children = { Heading("MementoMori", 28), Body("手机账号导出 · 预览版 0.1.0"), accountCard, sectionsCard, optionsCard, footer }
        } };
        import.Clicked += async (_, _) => await RunUiAsync(ImportAsync);
        read.Clicked += async (_, _) => await RunUiAsync(ReadAccountAsync);
        export.Clicked += async (_, _) => await RunUiAsync(ExportAsync);
        shareAgain.Clicked += async (_, _) => await RunUiAsync(ShareLastAsync);
        forget.Clicked += async (_, _) => await RunUiAsync(ForgetAsync);
        UpdateButtons();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (appeared) return;
        appeared = true;
        await RunUiAsync(async () =>
        {
            await session.LoadSavedAsync();
            RefreshAccounts();
            status.Text = session.Accounts.Count == 0 ? "请先导入登录配置。" : "已载入本机配置；点“读取账号”连接游戏服务器。";
        });
    }
    private async Task ImportAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择私有登录配置 appsettings.user.json" });
        if (file == null) return;
        await using var stream = await file.OpenReadAsync();
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer)) > 0)
        {
            if (bytes.Length + count > CredentialImport.MaxBytes) throw new InvalidDataException("文件超过 512 KB，请选择登录配置。");
            bytes.Write(buffer, 0, count);
        }
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        await session.ImportAsync(await reader.ReadToEndAsync());
        lastExport = null;
        RefreshAccounts();
        snapshot.Text = "尚未读取账号";
        status.Text = "配置已保存在本机安全存储中。可删除手机公共目录中的传输副本。";
    }
    private void RefreshAccounts()
    {
        accounts.ItemsSource = session.Accounts.ToList();
        accounts.SelectedIndex = session.Accounts.Count > 0 ? 0 : -1;
    }
    private async Task ReadAccountAsync()
    {
        if (accounts.SelectedItem is not AccountInfo account) return;
        lastExport = null;
        snapshot.Text = "正在获取新快照，上次结果暂不可导出";
        var currentOperation = operation;
        var progress = new Progress<string>(message =>
        {
            if (busy && currentOperation == operation) status.Text = message;
        });
        await session.ConnectAsync(account.UserId, ChooseWorldAsync, progress);
        snapshot.Text = session.FetchedAtUtc is { } fetched
            ? $"{session.Summary}\n数据读取时间：{fetched.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : session.Summary;
        status.Text = session.FetchedAtUtc == null ? "读取未完成。" : "读取完成，选择需要的内容后导出。";
    }
    private Task<long?> ChooseWorldAsync(IReadOnlyList<PlayerDataInfo> worlds) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var choices = worlds.Select((w, i) => $"{i + 1}. 区服 {w.WorldId} · {w.Name} · Rank {w.PlayerRank}").ToArray();
        var answer = await DisplayActionSheet("选择导出区服", "取消", null, choices);
        var index = Array.IndexOf(choices, answer);
        return index >= 0 ? (long?)worlds[index].WorldId : null;
    });
    private async Task ExportAsync()
    {
        var selected = sections.Where(pair => pair.Value.IsChecked).Select(pair => pair.Key).ToArray();
        status.Text = "正在生成脱敏文件…";
        lastExport = await session.ExportAsync(selected, zip.IsToggled, pretty.IsToggled);
        var length = new FileInfo(lastExport).Length;
        output.Text = $"已生成 {(zip.IsToggled ? "ZIP" : "JSON")} · {length / 1024.0:F1} KB。分享完成与否由系统分享面板决定。";
        status.Text = "文件已生成，可以重复导出或再次分享。";
        await ShareLastAsync();
    }
    private async Task ShareLastAsync()
    {
        if (lastExport == null || !File.Exists(lastExport)) throw new InvalidOperationException("文件已过期，请重新导出。");
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "分享 MementoMori 账号数据",
            File = new ShareFile(lastExport, lastExport.EndsWith(".zip", StringComparison.Ordinal) ? "application/zip" : "application/json")
        });
    }
    private async Task ForgetAsync()
    {
        if (!await DisplayAlert("清除本机配置", "将清除本 App 保存的登录配置和临时导出文件，不会删除游戏账号。", "清除", "取消")) return;
        await session.ForgetAsync();
        lastExport = null;
        RefreshAccounts();
        snapshot.Text = "尚未读取账号";
        status.Text = "本机登录配置已清除。";
        output.Text = "尚未生成文件。";
    }
    private async Task RunUiAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; operation++; UpdateButtons();
        try { await action(); }
        catch (Exception e)
        {
            // Never display remote exception messages: the inherited protocol may include sensitive values.
            var message = e is InvalidDataException ? "配置或文件格式不正确，请重新选择私有登录配置。" : e is JsonException
                ? "登录配置不是有效 JSON。" : $"操作未完成（{e.GetType().Name}）。请检查网络或重新导入配置后再试。";
            status.Text = message;
            await DisplayAlert("未完成", message, "知道了");
        }
        finally { busy = false; operation++; UpdateButtons(); }
    }
    private void UpdateButtons()
    {
        import.IsEnabled = !busy; accounts.IsEnabled = !busy;
        read.IsEnabled = !busy && accounts.SelectedItem is AccountInfo;
        var matching = accounts.SelectedItem is AccountInfo a && session.SelectedUserId == a.UserId;
        export.IsEnabled = !busy && matching && session.FetchedAtUtc != null && sections.Values.Any(c => c.IsChecked);
        shareAgain.IsEnabled = !busy && matching && lastExport != null;
        forget.IsEnabled = !busy && session.Accounts.Count > 0;
        sectionPanel.IsEnabled = !busy; zip.IsEnabled = !busy; pretty.IsEnabled = !busy && !zip.IsToggled;
        spinner.IsRunning = busy; spinner.IsVisible = busy;
    }
    private static Label Body(string text) => new() { Text = text, FontSize = 14, TextColor = Color.FromArgb("#4C576D"), LineBreakMode = LineBreakMode.WordWrap };
    private static Label Heading(string text, double size = 18) => new() { Text = text, FontSize = size, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172033") };
    private static Button ActionButton(string text, bool secondary = false) => new()
    {
        Text = text, FontSize = 16, MinimumHeightRequest = 50, CornerRadius = 12,
        BackgroundColor = Color.FromArgb(secondary ? "#E8EDF8" : "#4565BA"),
        TextColor = secondary ? Color.FromArgb("#223250") : Colors.White
    };
    private static Border Card(params View[] children)
    {
        var content = new VerticalStackLayout { Spacing = 12 };
        foreach (var child in children) content.Children.Add(child);
        return new Border { BackgroundColor = Colors.White, StrokeThickness = 0, Padding = 16,
            StrokeShape = new RoundRectangle { CornerRadius = 16 }, Content = content };
    }
    private static Grid Toggle(string text, Switch toggle)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new() { Width = GridLength.Star }, new() { Width = GridLength.Auto } }, MinimumHeightRequest = 48 };
        var label = Body(text); label.VerticalOptions = LayoutOptions.Center;
        row.Add(label, 0, 0); row.Add(toggle, 1, 0);
        return row;
    }
}
