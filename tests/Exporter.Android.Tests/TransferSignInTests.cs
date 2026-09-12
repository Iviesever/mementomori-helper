using System.IO.Compression;
using System.Text.Json;
using MementoMori;
using MementoMori.Exporter.Android.Services;
using Xunit;

namespace Exporter.Android.Tests;

public sealed class TransferSignInTests
{
    private const string Password = "fake-transfer-password";
    private const string ReturnedKey = "fake-server-client-key";
    private static AuthOption Existing() => CredentialImport.Parse("""
        {"AuthOption":{"AppVersion":"test-version","Accounts":[{"UserId":123,"ClientKey":"fake-old-key","Name":"已有账号","AutoLoginWorldId":7}]}}
        """);

    private sealed class Vault
    {
        public string? Value;
        public int Writes;
        public bool FailWrite;
        public SavedCredentials Store => new(() => Task.FromResult(Value), value =>
        {
            Writes++;
            if (FailWrite) throw new IOException("fake storage failure");
            Value = value;
            return Task.CompletedTask;
        }, () => Value = null);
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData(" 123 456 789 ", 123456789L)]
    [InlineData("１２３　４５６", 123456L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void ParsesPastedTransferCodesWithoutLosingInt64Precision(string code, long expected) =>
        Assert.Equal(expected, TransferSignIn.ParseTransferCode(code));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-123")]
    [InlineData("+123")]
    [InlineData("12.3")]
    [InlineData("1e3")]
    [InlineData("123abc")]
    [InlineData("9223372036854775808")]
    public void RejectsInvalidTransferCodes(string code) =>
        Assert.Throws<TransferLoginInputException>(() => TransferSignIn.ParseTransferCode(code));

    [Fact]
    public void RejectsOversizedTransferCode() => Assert.Throws<TransferLoginInputException>(() =>
        TransferSignIn.ParseTransferCode(new string('1', 129)));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingPasswordNeverCallsNetworkOrWrites(string password)
    {
        var vault = new Vault();
        var calls = 0;
        await Assert.ThrowsAsync<TransferLoginInputException>(() => TransferSignIn.SignInAsync(new AuthOption(),
            "456", password, null, vault.Store, (_, _) => { calls++; return Task.FromResult(ReturnedKey); }));
        Assert.Equal(0, calls);
        Assert.Equal(0, vault.Writes);
    }

    [Fact]
    public async Task FirstLoginStoresClientKeyNotPasswordAndReloadsAfterRestart()
    {
        var vault = new Vault();
        var current = new AuthOption();
        var next = await TransferSignIn.SignInAsync(current, "456", Password, "我的账号", vault.Store,
            (id, password) =>
            {
                Assert.Equal(456L, id);
                Assert.Equal(Password, password);
                Assert.Null(vault.Value);
                return Task.FromResult(ReturnedKey);
            });
        Assert.Empty(current.Accounts);
        Assert.Equal(1, vault.Writes);
        Assert.DoesNotContain(Password, vault.Value!);
        Assert.DoesNotContain("pending-not-persisted", vault.Value!);
        using var json = JsonDocument.Parse(vault.Value!);
        Assert.False(json.RootElement.GetProperty("AuthOption").TryGetProperty("Password", out _));
        // A fresh wrapper models restarting the app: no exchange/password is required to load.
        var reloaded = await vault.Store.LoadAsync();
        var account = Assert.Single(reloaded!.Accounts);
        Assert.Equal(456L, account.UserId);
        Assert.Equal(ReturnedKey, account.ClientKey);
        Assert.Equal("我的账号", account.Name);
        Assert.False(account.AutoLogin);
        Assert.Equal(CredentialImport.DefaultAuthUrl, next.AuthUrl);
    }

    [Fact]
    public async Task AddingAccountPreservesOthersAndPreservesPasswordExactly()
    {
        var current = Existing();
        var vault = new Vault();
        var exactPassword = "  fake password 空格  ";
        var next = await TransferSignIn.SignInAsync(current, "456", exactPassword, null, vault.Store,
            (_, password) => { Assert.Equal(exactPassword, password); return Task.FromResult(ReturnedKey); });
        Assert.Single(current.Accounts);
        Assert.Equal(2, next.Accounts.Count);
        Assert.Equal("fake-old-key", next.Accounts[0].ClientKey);
        Assert.Equal(7, next.Accounts[0].AutoLoginWorldId);
        Assert.Equal("账号 2", next.Accounts[1].Name);
        Assert.All(next.Accounts, a => Assert.False(a.AutoLogin));
        Assert.DoesNotContain(exactPassword, vault.Value!);
    }

    [Fact]
    public async Task ReloginUpdatesInPlaceRatherThanDuplicatingAccount()
    {
        var current = Existing();
        var next = await TransferSignIn.SignInAsync(current, "123", Password, null, new Vault().Store,
            (_, _) => Task.FromResult(ReturnedKey));
        var account = Assert.Single(next.Accounts);
        Assert.Equal("已有账号", account.Name);
        Assert.Equal(7, account.AutoLoginWorldId);
        Assert.Equal(ReturnedKey, account.ClientKey);
        Assert.Equal("fake-old-key", current.Accounts[0].ClientKey);
    }

    [Fact]
    public async Task ExplicitAccountNameCanReplaceExistingRemark()
    {
        var next = await TransferSignIn.SignInAsync(Existing(), "123", Password, "  新备注  ", new Vault().Store,
            (_, _) => Task.FromResult(ReturnedKey));
        Assert.Equal("新备注", Assert.Single(next.Accounts).Name);
    }

    [Fact]
    public async Task FailedExchangeKeepsSavedAndWorkingAccountAndDoesNotLeakRemoteMessage()
    {
        var current = Existing();
        var original = CredentialImport.Serialize(current);
        var vault = new Vault { Value = original };
        var error = await Assert.ThrowsAsync<TransferLoginFailedException>(() => TransferSignIn.SignInAsync(current,
            "456", Password, null, vault.Store, (_, _) => throw new HttpRequestException(Password + ReturnedKey)));
        Assert.Equal(original, vault.Value);
        Assert.Equal(original, CredentialImport.Serialize(current));
        Assert.Equal(0, vault.Writes);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.DoesNotContain(ReturnedKey, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingServerKeyIsNeverSaved(string key)
    {
        var vault = new Vault();
        await Assert.ThrowsAsync<TransferLoginFailedException>(() => TransferSignIn.SignInAsync(Existing(), "456",
            Password, null, vault.Store, (_, _) => Task.FromResult(key)));
        Assert.Equal(0, vault.Writes);
    }

    [Fact]
    public async Task FailedPersistenceDoesNotReturnOrApplyReplacementAccount()
    {
        var current = Existing();
        var original = CredentialImport.Serialize(current);
        var vault = new Vault { Value = original, FailWrite = true };
        var error = await Assert.ThrowsAsync<TransferLoginPersistenceException>(() => TransferSignIn.SignInAsync(current,
            "123", Password, null, vault.Store, (_, _) => Task.FromResult(ReturnedKey)));
        Assert.Equal(original, vault.Value);
        Assert.Equal(original, CredentialImport.Serialize(current));
        Assert.Equal(1, vault.Writes);
        Assert.Contains("游戏端引继可能已经生效", error.Message);
        Assert.DoesNotContain(ReturnedKey, error.ToString());
    }

    [Fact]
    public async Task FreshGameVersionIsPersistedAfterExchange()
    {
        var current = Existing();
        var next = await TransferSignIn.SignInAsync(current, "456", Password, null, new Vault().Store,
            (_, _) => { current.AppVersion = "new-test-version"; return Task.FromResult(ReturnedKey); });
        Assert.Equal("new-test-version", next.AppVersion);
    }

    [Fact]
    public async Task InvalidLocalEndpointNeverReceivesPassword()
    {
        var current = Existing();
        current.AuthUrl = "https://example.org/api/";
        var vault = new Vault();
        var calls = 0;
        await Assert.ThrowsAsync<TransferLoginInputException>(() => TransferSignIn.SignInAsync(current, "456", Password,
            null, vault.Store, (_, _) => { calls++; return Task.FromResult(ReturnedKey); }));
        Assert.Equal(0, calls);
        Assert.Equal(0, vault.Writes);
    }

    [Fact]
    public async Task AccountLimitIsCheckedBeforeExchangeButReloginRemainsPossible()
    {
        var current = Existing();
        current.Accounts = Enumerable.Range(1, 32).Select(i => new AccountInfo
            { UserId = i, ClientKey = "fake", Name = "测试" }).ToList();
        var calls = 0;
        Task<string> Exchange(long id, string password) { calls++; return Task.FromResult(ReturnedKey); }
        await Assert.ThrowsAsync<TransferLoginInputException>(() => TransferSignIn.SignInAsync(current, "456", Password,
            null, new Vault().Store, Exchange));
        Assert.Equal(0, calls);
        var next = await TransferSignIn.SignInAsync(current, "1", Password, null, new Vault().Store, Exchange);
        Assert.Equal(1, calls);
        Assert.Equal(32, next.Accounts.Count);
        Assert.Equal(ReturnedKey, next.Accounts[0].ClientKey);
    }

    [Fact]
    public async Task OversizedPasswordAndNameAreRejectedBeforeExchange()
    {
        var calls = 0;
        Task<string> Exchange(long id, string password) { calls++; return Task.FromResult(ReturnedKey); }
        await Assert.ThrowsAsync<TransferLoginInputException>(() => TransferSignIn.SignInAsync(Existing(), "456",
            new string('a', 4097), null, new Vault().Store, Exchange));
        await Assert.ThrowsAsync<TransferLoginInputException>(() => TransferSignIn.SignInAsync(Existing(), "456",
            Password, new string('a', 65), new Vault().Store, Exchange));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void DefaultExportIncludesAllEightSectionsIncludingGacha()
    {
        Assert.Equal(new[] { "player", "progress", "levelLink", "characters", "equipment", "decks", "items", "gacha" },
            ExportSelection.DefaultSections());
        var changed = ExportSelection.DefaultSections();
        changed[0] = "changed-by-caller";
        Assert.Equal("player", ExportSelection.DefaultSections()[0]);
        Assert.Equal(MobileSnapshot.Sections, ExportSelection.DefaultSections());
    }

    [Fact]
    public void AllSelectedZipContainsEveryModuleAndLevelLinkMembers()
    {
        var data = new Dictionary<string, object?>
        {
            ["schema"] = MobileSnapshot.Schema, ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["source"] = new { exporter = "test" },
            ["levelLinkMembers"] = new[] { new { cellNo = 1, instanceIndex = 2 } }
        };
        foreach (var key in ExportSelection.DefaultSections()) data[key] = new { test = key };
        using var zip = new ZipArchive(new MemoryStream(MobileSnapshot.Zip(data, ExportSelection.DefaultSections())));
        Assert.Equal(9, zip.Entries.Count);
        Assert.NotNull(zip.GetEntry("gacha.json"));
        using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        using var manifest = JsonDocument.Parse(reader.ReadToEnd());
        Assert.Equal(ExportSelection.DefaultSections(), manifest.RootElement.GetProperty("selectedSections")
            .EnumerateArray().Select(e => e.GetString()!).ToArray());
        using var linkReader = new StreamReader(zip.GetEntry("level-link.json")!.Open());
        using var link = JsonDocument.Parse(linkReader.ReadToEnd());
        Assert.Equal(1, link.RootElement.GetProperty("levelLinkMembers").GetArrayLength());
    }
}
