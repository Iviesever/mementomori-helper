using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Text;
using System.Text.Json;
using MementoMori.Exporter.Android.Services;
using MementoMori.Ortega.Share.Data.Auth;
using RoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;

namespace MementoMori.Exporter.Android;

public sealed class MainPage : ContentPage
{
    private readonly MobileSession session;
    private readonly Picker accounts = new() { Title = "选择本机已保存的账号", TextColor = Color.FromArgb("#172033") };
    private readonly Entry transferCode = new()
    {
        Placeholder = "引继码 / 用户 ID", Keyboard = Keyboard.Numeric, MaxLength = 128,
        IsTextPredictionEnabled = false, IsSpellCheckEnabled = false, TextColor = Color.FromArgb("#172033")
    };
    private readonly Entry transferPassword = new()
    {
        Placeholder = "引继密码", IsPassword = true, MaxLength = 4096,
        IsTextPredictionEnabled = false, IsSpellCheckEnabled = false, TextColor = Color.FromArgb("#172033")
    };
    private readonly Entry accountName = new()
    {
        Placeholder = "账号备注（可不填）", MaxLength = 64, TextColor = Color.FromArgb("#172033")
    };
    private readonly Label remembered = Body("登录成功后会记住账号，不保存引继密码。");
    private readonly Label status = Body("使用引继码和密码登录，之后直接在手机上读取和导出。");
    private readonly Label snapshot = Body("尚未读取账号");
    private readonly Label output = Body("默认全选全部内容，生成后可保存 JSON / ZIP 到手机或分享。");
    private readonly ActivityIndicator spinner = new() { Color = Color.FromArgb("#4565BA"), IsVisible = false };
    private readonly VerticalStackLayout sectionPanel = new() { Spacing = 0 };
    private readonly Dictionary<string, CheckBox> sections = new();
    private readonly Switch zip = new();
    private readonly Switch pretty = new();
    private readonly Button login = ActionButton("引继登录并保存账号");
    private readonly Button import = ActionButton("导入已有登录配置（备用）", secondary: true);
    private readonly Button read = ActionButton("读取已保存的账号");
    private readonly Button selectAll = ActionButton("全选导出内容", secondary: true);
    private readonly Button export = ActionButton("生成文件");
    private readonly Button save = ActionButton("保存文件到手机", secondary: true);
    private readonly Button shareAgain = ActionButton("分享已生成的文件", secondary: true);
    private readonly Button forget = ActionButton("清除本机账号和临时文件", secondary: true);
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
        accounts.SelectedIndexChanged += (_, _) =>
        {
            ClearLastExport();
            UpdateSnapshot();
            UpdateButtons();
        };
        zip.Toggled += (_, _) => UpdateButtons();
        transferCode.TextChanged += (_, _) => UpdateButtons();
        transferPassword.TextChanged += (_, _) => UpdateButtons();

        var accountCard = Card(
            Heading("01  登录与本机账号"),
            Body("首次直接输入游戏内的引继码（用户 ID）和引继密码，无需电脑配置文件。引继操作可能使其他设备的旧登录凭据失效，建议先退出游戏。"),
            transferCode, transferPassword, accountName, login,
            Body("登录凭据保存在本机安全存储；密码只用于本次登录，提交结束后清空。不要把密码或私有配置发到聊天、GitHub。"),
            remembered, accounts, read, spinner, status, snapshot,
            import, Body("备用导入仅接受私有 appsettings.user.json，不是游戏养成数据的导出 JSON。"));

        var names = new (string Key, string Label)[]
        {
            ("player", "玩家资料"), ("progress", "主线进度"), ("levelLink", "等级链接"),
            ("characters", "角色与属性"), ("equipment", "装备与符石"), ("decks", "各模式队伍"),
            ("items", "背包与资源"), ("gacha", "卡池信息（额外联网读取列表）")
        };
        var defaults = ExportSelection.DefaultSections().ToHashSet(StringComparer.Ordinal);
        foreach (var entry in names)
        {
            var check = new CheckBox { IsChecked = defaults.Contains(entry.Key), Color = Color.FromArgb("#4565BA") };
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
        selectAll.Clicked += (_, _) =>
        {
            if (busy) return;
            foreach (var check in sections.Values) check.IsChecked = true;
        };
        var sectionsCard = Card(Heading("02  导出内容 · 默认全部勾选"), selectAll, sectionPanel,
            Body("包含原有全部 8 类导出信息，卡池也默认勾选。可按需取消；再次启动时恢复全选。资料未解析或卡池读取失败会在文件和生成结果中标记。"));
        var optionsCard = Card(Heading("03  生成、保存与分享"),
            Toggle("按内容分文件，打包为 ZIP", zip), Toggle("JSON 易读排版（文件会更大）", pretty),
            export, output, save, shareAgain,
            Body("修改勾选项或格式后，需要重新点“生成文件”。保存和分享使用上面已生成的文件，不会重新联网。"),
            Body("重复生成使用上次读取的账号快照。游戏内变更后，请再次点“读取已保存的账号”；文件生成时间不代表数据读取时间。卡池列表单独读取。"));
        var footer = Card(
            Body("手机独立运行，不需要电脑在线。不提供自动抽卡、领奖或战斗入口。"),
            Body("保存到公共目录或分享出去的副本由你管理，清除本机账号不会删除这些副本。退出或重启 App 会丢失内存快照，账号仍会保留；再次导出请重新读取。"), forget);
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Spacing = 14, Padding = new Thickness(18, 18, 18, 32),
            Children = { Heading("MementoMori", 28), Body($"手机账号导出 · 预览版 {AppInfo.Current.VersionString}"), accountCard, sectionsCard, optionsCard, footer }
        } };
        login.Clicked += async (_, _) => await RunUiAsync(SignInAsync);
        import.Clicked += async (_, _) => await RunUiAsync(ImportAsync);
        read.Clicked += async (_, _) => await RunUiAsync(ReadAccountAsync);
        export.Clicked += async (_, _) => await RunUiAsync(ExportAsync);
        save.Clicked += async (_, _) => await RunUiAsync(SaveLastAsync);
        shareAgain.Clicked += async (_, _) => await RunUiAsync(ShareLastAsync);
        forget.Clicked += async (_, _) => await RunUiAsync(ForgetAsync);
        UpdateButtons();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (appeared) { UpdateButtons(); return; }
        appeared = true;
        await RunUiAsync(async () =>
        {
            await session.LoadSavedAsync();
            RefreshAccounts();
            status.Text = session.Accounts.Count == 0
                ? "请输入引继码和密码登录。"
                : "已载入本机账号，无需再输密码；点“读取已保存的账号”。";
        }, restoringCredentials: true);
    }

    private async Task SignInAsync()
    {
        long userId;
        try
        {
            status.Text = "正在验证引继信息并保存本机账号…";
            userId = await session.SignInAsync(transferCode.Text ?? "", transferPassword.Text ?? "", accountName.Text);
        }
        finally
        {
            // Do not leave the password in the form on success, failure, or storage errors.
            transferPassword.Text = string.Empty;
        }
        ClearLastExport();
        RefreshAccounts(userId);
        UpdateSnapshot();
        status.Text = "账号已保存在本机，正在准备选择区服。";
        // Saving the key precedes world selection. Cancelling or failing the read does not lose login.
        await ReadAccountAsync();
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
        var json = await reader.ReadToEndAsync();
        try { await session.ImportAsync(json); }
        finally { ClearLastExport(); RefreshAccounts(); UpdateSnapshot(); }
        status.Text = "账号已保存在本机安全存储中。可删除手机公共目录中的配置传输副本。";
    }

    private void RefreshAccounts(long? preferredUserId = null)
    {
        var list = session.Accounts.ToList();
        accounts.ItemsSource = list;
        var preferred = preferredUserId.HasValue ? list.FindIndex(a => a.UserId == preferredUserId.Value) : -1;
        accounts.SelectedIndex = preferred >= 0 ? preferred : (list.Count > 0 ? 0 : -1);
        remembered.Text = list.Count == 0 ? "尚未保存账号。登录成功后会自动记住账号，不保存引继密码。"
            : $"本机已保存 {list.Count} 个账号。下次打开直接选择并读取，无需再输密码。";
    }

    private bool MatchesSnapshot => accounts.SelectedItem is AccountInfo account &&
        session.SelectedUserId == account.UserId && session.FetchedAtUtc != null;

    private void UpdateSnapshot()
    {
        snapshot.Text = MatchesSnapshot && session.FetchedAtUtc is { } fetched
            ? $"{session.Summary}\n数据读取时间：{fetched.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "当前所选账号尚未读取；请点“读取已保存的账号”。";
    }

    private void ClearLastExport()
    {
        lastExport = null;
        output.Text = "尚未生成当前账号的文件。";
    }

    private async Task ReadAccountAsync()
    {
        if (accounts.SelectedItem is not AccountInfo account) return;
        ClearLastExport();
        snapshot.Text = "正在获取新快照，上次结果暂不可导出";
        var currentOperation = operation;
        var progress = new Progress<string>(message =>
        {
            if (busy && currentOperation == operation) status.Text = message;
        });
        try
        {
            await session.ConnectAsync(account.UserId, ChooseWorldAsync, progress);
            status.Text = session.FetchedAtUtc == null ? session.Summary : "读取完成；导出内容默认全选，可直接生成文件。";
        }
        finally { UpdateSnapshot(); }
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
        if (accounts.SelectedItem is not AccountInfo account) return;
        var selected = sections.Where(pair => pair.Value.IsChecked).Select(pair => pair.Key).ToArray();
        ClearLastExport();
        status.Text = "正在生成脱敏文件…";
        lastExport = await session.ExportAsync(account.UserId, selected, zip.IsToggled, pretty.IsToggled);
        var length = new FileInfo(lastExport).Length;
        var labels = selected.Select(key => key switch
        {
            "player" => "玩家", "progress" => "主线", "levelLink" => "等级链接", "characters" => "角色",
            "equipment" => "装备符石", "decks" => "队伍", "items" => "背包", "gacha" => "卡池", _ => key
        });
        output.Text = $"已生成 {(zip.IsToggled ? "ZIP" : "JSON")} · {length / 1024.0:F1} KB\n内容：{string.Join("、", labels)}\n{Path.GetFileName(lastExport)}";
        if (session.LastExportHasWarnings)
        {
            output.Text += "\n注意：部分资料未解析或卡池读取失败；详见文件中的 metadataStatus / errorType。";
            status.Text = "已生成带提示的文件；不能把未解析资料当作真实的零值或空卡池。";
        }
        else
        {
            status.Text = "文件已生成；可以保存到手机，也可以分享。";
        }
    }

    private string RequireLastExport()
    {
        if (!MatchesSnapshot || !session.ExportFiles.IsAvailable(lastExport))
        {
            ClearLastExport();
            throw new FileNotFoundException("The temporary export is no longer available.");
        }
        return session.ExportFiles.Validate(lastExport!);
    }

    private async Task ShareLastAsync()
    {
        var path = RequireLastExport();
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "分享 MementoMori 账号数据",
            File = new ShareFile(path, ExportFileStore.MimeType(path))
        });
        status.Text = "已打开系统分享面板；是否发送成功请以目标 App 为准。文件仍可保存或再次分享。";
    }

    private async Task SaveLastAsync()
    {
        var path = RequireLastExport();
        if (Platform.CurrentActivity is not MainActivity activity)
            throw new InvalidOperationException("No active Android window.");
        var resolver = global::Android.App.Application.Context.ContentResolver
            ?? throw new InvalidOperationException("No document provider.");
        var uri = await activity.ChooseExportDestinationAsync(Path.GetFileName(path), ExportFileStore.MimeType(path));
        if (uri == null)
        {
            status.Text = "已取消保存。文件仍可重新保存或分享。";
            return;
        }
        try
        {
            await Task.Run(async () =>
            {
                using var destination = resolver.OpenOutputStream(uri, "wt")
                    ?? throw new IOException("The document provider could not open a writable stream.");
                await session.ExportFiles.CopyToAsync(path, destination);
            });
        }
        catch
        {
            try { global::Android.Provider.DocumentsContract.DeleteDocument(resolver, uri); }
            catch (Exception) { }
            throw new IOException("Export copy did not complete.");
        }
        status.Text = "文件已保存到你选择的位置。可在其他 App 的附件选择器中找到；临时文件仍可再次分享。";
    }

    private async Task ForgetAsync()
    {
        if (!await DisplayAlert("清除本机账号", "将清除本 App 保存的登录凭据和临时导出文件，下次需要重新引继登录或导入配置。不会删除游戏账号，也不会删除已经保存或分享出去的文件。", "清除", "取消")) return;
        transferPassword.Text = string.Empty;
        transferCode.Text = string.Empty;
        accountName.Text = string.Empty;
        ClearLastExport();
        try { await session.ForgetAsync(); }
        finally { RefreshAccounts(); UpdateSnapshot(); }
        status.Text = "本机账号和临时文件已清除。可以重新引继登录。";
    }

    private async Task RunUiAsync(Func<Task> action, bool restoringCredentials = false)
    {
        if (busy) return;
        busy = true; operation++; UpdateButtons();
        try { await action(); }
        catch (OperationCanceledException)
        {
            status.Text = "操作已取消、超时或被系统中断，可以重试。已保存的账号仍会保留。";
        }
        catch (Exception e)
        {
            // Only our fixed login errors may display Message. Never show a remote exception body.
            var message = restoringCredentials
                ? "本机账号资料无法读取。可重新引继登录或导入配置；若仍失败，请先清除本机账号。"
                : e switch
                {
                    TransferLoginInputException or TransferLoginFailedException or TransferLoginPersistenceException => e.Message,
                    InvalidDataException => "配置或文件格式不正确，请重新选择私有登录配置。",
                    JsonException => "登录配置不是有效 JSON。",
                    FileNotFoundException => "临时文件已失效或文件无法读取，请重新生成或重新选择文件。",
                    HttpRequestException => "网络请求失败，请检查网络后重试。已保存的账号不会因此删除。",
                    IOException => "文件读写未完成，请检查空间和保存位置后重试。目标目录可能留有不完整副本。",
                    _ => $"操作未完成（{e.GetType().Name}）。可重试读取或重新引继登录，已保存的账号不会自动删除。"
                };
            status.Text = message;
            await DisplayAlert("未完成", message, "知道了");
        }
        finally { busy = false; operation++; UpdateButtons(); }
    }

    private void UpdateButtons()
    {
        import.IsEnabled = !busy; accounts.IsEnabled = !busy;
        transferCode.IsEnabled = !busy; transferPassword.IsEnabled = !busy; accountName.IsEnabled = !busy;
        login.IsEnabled = !busy && !string.IsNullOrWhiteSpace(transferCode.Text) && !string.IsNullOrWhiteSpace(transferPassword.Text);
        read.IsEnabled = !busy && accounts.SelectedItem is AccountInfo;
        selectAll.IsEnabled = !busy;
        export.IsEnabled = !busy && MatchesSnapshot && sections.Values.Any(c => c.IsChecked);
        var hasFile = !busy && MatchesSnapshot && session.ExportFiles.IsAvailable(lastExport);
        shareAgain.IsEnabled = hasFile; save.IsEnabled = hasFile;
        forget.IsEnabled = !busy;
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
