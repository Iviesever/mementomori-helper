using System.Text.Json;

namespace MementoMori.Exporter.Android.Services;

public static class CredentialImport
{
    public const int MaxBytes = 512 * 1024;
    public const string DefaultAuthUrl = "https://prd1-auth.mememori-boi.com/api/";

    // Copy only explicitly supported login fields. Ignore desktop jobs, paths and reporting URLs.
    public static AuthOption Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new InvalidDataException("配置文件超过 512 KB。请选择 appsettings.user.json。");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("AuthOption", out var auth))
            throw new InvalidDataException("这不是登录配置。游戏数据导出的 JSON 不能用来登录。");
        if (auth.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("AuthOption 格式不正确。");

        var url = Text(auth, "AuthUrl", DefaultAuthUrl);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.Host.EndsWith(".mememori-boi.com", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath != "/api/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("登录服务器地址不受支持，未导入配置。");

        var result = new AuthOption
        {
            AuthUrl = url,
            DeviceToken = Text(auth, "DeviceToken", ""),
            AppVersion = Text(auth, "AppVersion", "3.2.2"),
            OSVersion = Text(auth, "OSVersion", "Android"),
            ModelName = Text(auth, "ModelName", "Android"),
            Accounts = new List<AccountInfo>()
        };
        if (auth.TryGetProperty("Accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
        {
            if (accounts.GetArrayLength() > 32) throw new InvalidDataException("账号数量超过限制。");
            foreach (var account in accounts.EnumerateArray()) result.Accounts.Add(ReadAccount(account));
        }
        if (result.Accounts.Count == 0)
        {
            result.Accounts.Add(ReadAccount(auth));
            result.Accounts[0].Name = "导入的账号";
        }
        if (result.Accounts.Select(x => x.UserId).Distinct().Count() != result.Accounts.Count)
            throw new InvalidDataException("配置中存在重复账号。");
        return result;
    }

    public static string Serialize(AuthOption auth) => JsonSerializer.Serialize(new { AuthOption = auth });

    private static AccountInfo ReadAccount(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("UserId", out var id) ||
            id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var userId) || userId <= 0)
            throw new InvalidDataException("配置缺少有效的账号登录 ID。");
        var key = Text(value, "ClientKey", "");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("配置缺少 ClientKey。");
        var world = value.TryGetProperty("AutoLoginWorldId", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt64(out var n) ? n : 0;
        return new AccountInfo { UserId = userId, ClientKey = key, Name = Text(value, "Name", "导入的账号"),
            AutoLogin = false, AutoLoginWorldId = world };
    }

    private static string Text(JsonElement obj, string name, string fallback)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("配置字段类型不正确。");
        var text = value.GetString() ?? fallback;
        if (text.Length > 4096) throw new InvalidDataException("配置字段过长。");
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
}
