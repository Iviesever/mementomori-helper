using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MementoMori;
using MementoMori.Exporter.Android.Services;
using Xunit;

namespace Exporter.Android.Tests;

public class ExportContractTests
{
    // Fake values only. No real game credential is needed or used by these tests.
    private const string Valid = """
        {"AuthOption":{"AuthUrl":null,"Accounts":[{"UserId":123,"ClientKey":"fake-local-test-key","Name":"Test","AutoLogin":true,"AutoLoginWorldId":7}]},"GameConfig":{"AutoJob":{"AutoFreeGacha":true}}}
        """;

    [Fact]
    public void ImportStripsDesktopSettingsAndDisablesAutoLogin()
    {
        var auth = CredentialImport.Parse(Valid);
        var account = Assert.Single(auth.Accounts);
        Assert.False(account.AutoLogin);
        Assert.Equal(7L, account.AutoLoginWorldId);
        Assert.Equal(CredentialImport.DefaultAuthUrl, auth.AuthUrl);
        Assert.DoesNotContain("GameConfig", CredentialImport.Serialize(auth));
    }
    [Theory]
    [InlineData("http://prd1-auth.mememori-boi.com/api/")]
    [InlineData("https://example.org/api/")]
    [InlineData("https://mememori-boi.com.example.org/api/")]
    [InlineData("https://prd1-auth.mememori-boi.com/api/?password=anything")]
    [InlineData("https://user:pass@prd1-auth.mememori-boi.com/api/")]
    public void RejectsUntrustedAuthEndpoints(string endpoint)
    {
        Assert.Throws<InvalidDataException>(() => CredentialImport.Parse(Valid.Replace("\"AuthUrl\":null", "\"AuthUrl\":" + JsonSerializer.Serialize(endpoint))));
    }
    [Fact]
    public void RejectsExportJsonAsLoginConfig() => Assert.Throws<InvalidDataException>(() =>
        CredentialImport.Parse("{\"schema\":\"mementomori-safe-account-export-v3.1\",\"player\":{\"name\":\"Test\"}}"));
    [Fact]
    public void LegacyImportAndRoundtripWork()
    {
        var auth = CredentialImport.Parse("{\"AuthOption\":{\"UserId\":123,\"ClientKey\":\"fake\"}}");
        var reloaded = CredentialImport.Parse(CredentialImport.Serialize(auth));
        Assert.Equal(123L, Assert.Single(reloaded.Accounts).UserId);
        Assert.False(reloaded.Accounts[0].AutoLogin);
    }
    [Fact]
    public void RejectsDuplicateAccounts() => Assert.Throws<InvalidDataException>(() => CredentialImport.Parse(
        "{\"AuthOption\":{\"Accounts\":[{\"UserId\":1,\"ClientKey\":\"a\"},{\"UserId\":1,\"ClientKey\":\"b\"}]}}"));
    [Fact]
    public void RejectsOversizedImport() => Assert.Throws<InvalidDataException>(() => CredentialImport.Parse(new string(' ', CredentialImport.MaxBytes + 1)));
    [Fact]
    public void RejectsNonNumericAccountId() => Assert.Throws<InvalidDataException>(() => CredentialImport.Parse(
        "{\"AuthOption\":{\"UserId\":\"secret\",\"ClientKey\":\"a\"}}"));
    [Theory]
    [InlineData("password")]
    [InlineData("ClientKey")]
    [InlineData("USERID")]
    [InlineData("guid")]
    public void SensitiveFieldsAreRejectedRecursively(string field)
    {
        var input = new { items = new[] { new Dictionary<string, object?> { [field] = "fake" } } };
        Assert.Throws<InvalidOperationException>(() => MobileSnapshot.SerializeSafe(input));
    }
    [Fact]
    public void SensitiveWordsInOrdinaryStringsAreNotFalsePositives()
    {
        var json = Encoding.UTF8.GetString(MobileSnapshot.SerializeSafe(new { name = "password is text, not a field" }));
        Assert.Contains("password", json);
    }
    [Fact]
    public void ZipIncludesManifestAndBothLinkObjects()
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["generatedAtUtc"] = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ["source"] = new { exporter = "test" },
            ["levelLink"] = new { partyLevel = 100 },
            ["levelLinkMembers"] = new[] { new { cellNo = 1, instanceIndex = 2 } }
        };
        using var stream = new MemoryStream(MobileSnapshot.Zip(snapshot, new[] { "levelLink" }));
        using var zip = new ZipArchive(stream);
        Assert.Equal(new[] { "level-link.json", "manifest.json" }, zip.Entries.Select(e => e.FullName).OrderBy(s => s).ToArray());
        using var reader = new StreamReader(zip.GetEntry("level-link.json")!.Open());
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        Assert.Equal(100, document.RootElement.GetProperty("levelLink").GetProperty("partyLevel").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("levelLinkMembers").GetArrayLength());
    }
    [Fact]
    public void WritableOptionsRemainInMemory()
    {
        var options = new MemoryOptions<AuthOption>(new AuthOption());
        options.Update(v => v.AppVersion = "test-version");
        Assert.Equal("test-version", options.Value.AppVersion);
        options.Replace(new AuthOption());
        Assert.Null(options.Value.AppVersion);
    }
}
