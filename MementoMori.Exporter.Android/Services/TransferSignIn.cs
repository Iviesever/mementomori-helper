using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MementoMori.Exporter.Android.Services;

/// <summary>Validates transfer login and persists only the returned client key, never the password.</summary>
public static class TransferSignIn
{
    public static long ParseTransferCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128)
            throw new TransferLoginInputException("请输入游戏内的数字引继码（用户 ID），不是角色所在区服编号。");
        // Accept pasted grouping spaces and full-width digits, but never signs or decimal notation.
        var digits = new string(code.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (digits.Length is 0 or > 19 || digits.Any(c => c < '0' || c > '9') ||
            !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            throw new TransferLoginInputException("引继码应为有效的纯数字用户 ID，请从游戏内重新复制。");
        return userId;
    }

    public static async Task<AuthOption> SignInAsync(AuthOption current, string code, string password,
        string? accountName, SavedCredentials storage, Func<long, string, Task<string>> exchange)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(exchange);
        var userId = ParseTransferCode(code);
        if (string.IsNullOrWhiteSpace(password) || password.Length > 4096)
            throw new TransferLoginInputException("请输入引继密码，密码不能超过 4096 个字符。");
        if (accountName?.Length > 64)
            throw new TransferLoginInputException("账号备注不能超过 64 个字符。");

        // Build a separate candidate. Failed validation/exchange/save cannot mutate a working account.
        var accounts = (current.Accounts ?? new List<AccountInfo>()).Select(a => new AccountInfo
        {
            UserId = a.UserId, ClientKey = a.ClientKey, Name = a.Name,
            AutoLogin = false, AutoLoginWorldId = a.AutoLoginWorldId
        }).ToList();
        var index = accounts.FindIndex(a => a.UserId == userId);
        if (accounts.Count > 32 || (index < 0 && accounts.Count == 32))
            throw new TransferLoginInputException("本机最多保存 32 个账号，请先管理已有账号。");
        var name = string.IsNullOrWhiteSpace(accountName)
            ? (index >= 0 ? accounts[index].Name : $"账号 {accounts.Count + 1}") : accountName.Trim();
        var candidateAccount = new AccountInfo
        {
            UserId = userId, Name = name, ClientKey = "pending-not-persisted",
            AutoLogin = false, AutoLoginWorldId = index >= 0 ? accounts[index].AutoLoginWorldId : 0
        };
        if (index < 0) accounts.Add(candidateAccount);
        else accounts[index] = candidateAccount;
        AuthOption candidate;
        try
        {
            // Reuse the import allowlist and endpoint/size/duplicate validation before any network call.
            candidate = CredentialImport.Parse(CredentialImport.Serialize(new AuthOption
            {
                AuthUrl = current.AuthUrl, AppVersion = current.AppVersion, DeviceToken = current.DeviceToken,
                OSVersion = current.OSVersion, ModelName = current.ModelName, Accounts = accounts
            }));
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException)
        {
            throw new TransferLoginInputException("本机账号配置无效，请清除本机配置后重新登录。");
        }

        string key;
        try
        {
            // Do not trim or normalize a password. It is used only for this exchange.
            key = await exchange(userId, password);
            if (string.IsNullOrWhiteSpace(key) || key.Length > 4096)
                throw new InvalidDataException();
        }
        catch (Exception)
        {
            // Remote exception bodies can contain the password or authentication response.
            // Do not retain them as InnerException or expose them through diagnostics.
            throw new TransferLoginFailedException();
        }

        candidate.Accounts.Single(a => a.UserId == userId).ClientKey = key;
        // Initialize may have obtained a newer game version during the exchange.
        candidate.AppVersion = current.AppVersion ?? candidate.AppVersion;
        try
        {
            return await storage.SaveAsync(CredentialImport.Serialize(candidate));
        }
        catch (Exception)
        {
            // The remote transfer may already be effective. Do not claim it was rolled back.
            throw new TransferLoginPersistenceException();
        }
    }
}

public sealed class TransferLoginInputException(string message) : Exception(message);

public sealed class TransferLoginFailedException() : Exception(
    "引继登录未完成。请检查引继码、密码和网络后重试；若游戏正在维护，请稍后重试。本机已保存的账号资料不会自动删除。");

public sealed class TransferLoginPersistenceException() : Exception(
    "引继验证成功，但本机账号保存失败。游戏端引继可能已经生效；请重试登录，必要时清除本机配置后重试。此次没有记住新账号。");
