using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MementoMori.Common.Localization;
using MementoMori.Export;
using MementoMori.Exporter.Android.Services;
using Xunit;

namespace Exporter.Android.Tests;

public sealed class ExporterParityTests
{
    [Theory]
    [InlineData("0.3.1", "android-native-v0.3.1")]
    [InlineData("4.2.0", "android-native-v4.2.0")]
    public void ExportIdentityUsesSuppliedApplicationVersion(string version, string expected) =>
        Assert.Equal(expected, ExportMetadata.AndroidExporterIdentity(version));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void InvalidVersionDoesNotSilentlyBecomeTheOldVersion(string? version) =>
        Assert.Throws<ArgumentException>(() => ExportMetadata.AndroidExporterIdentity(version!));

    [Fact]
    public void MissingRuneIsNotReportedAsLevelZeroOrDefenseType()
    {
        using var document = JsonDocument.Parse(MobileSnapshot.SerializeSafe(ExportMetadata.MissingSphere(999999)));
        var rune = document.RootElement;
        Assert.Equal(999999L, rune.GetProperty("sphereId").GetInt64());
        Assert.Equal(JsonValueKind.Null, rune.GetProperty("level").ValueKind);
        Assert.Equal(JsonValueKind.Null, rune.GetProperty("isAttackType").ValueKind);
        Assert.Equal("unavailable", rune.GetProperty("metadataStatus").GetString());
        Assert.True(ExportMetadata.HasWarnings(new { runes = new[] { ExportMetadata.MissingSphere(999999) } }));
    }

    [Fact]
    public void EquipmentFragmentRarityUsesTheCompositeMapping()
    {
        long observed = 0;
        var result = ExportMetadata.ResolveEquipmentFragmentRarity(42,
            id => { Assert.Equal(42L, id); return 9001; },
            id => { observed = id; return id == 9001 ? "SSR" : "D"; });
        Assert.Equal(9001L, observed);
        Assert.Equal("SSR", result);
    }

    [Fact]
    public void MissingCompositeDoesNotFallBackToAnUnrelatedEquipmentId()
    {
        var queriedEquipment = false;
        Assert.Throws<InvalidDataException>(() => ExportMetadata.ResolveEquipmentFragmentRarity(42,
            _ => throw new InvalidDataException(),
            _ => { queriedEquipment = true; return "D"; }));
        Assert.False(queriedEquipment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("[ItemName123]")]
    public void MissingNamesAndTranslationKeysAreMarked(string? name) =>
        Assert.Equal("unresolved", ExportMetadata.ItemMetadataStatus(name, "SR"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingRarityIsNotTreatedAsResolved(string? rarity) =>
        Assert.Equal("unresolved", ExportMetadata.ItemMetadataStatus("Synthetic item", rarity));

    [Fact]
    public void LocalizedUnknownMarkerIsMarked() => Assert.Equal("unresolved",
        ExportMetadata.ItemMetadataStatus(ResourceStrings.Unknown, ResourceStrings.Unknown));

    [Fact]
    public void KnownNameAndNoneRarityAreValid() => Assert.Equal("resolved",
        ExportMetadata.ItemMetadataStatus("Synthetic gold", "None"));

    [Theory]
    [InlineData(false, null, "InvalidDataException")]
    [InlineData(true, null, null)]
    [InlineData(false, "HttpRequestException", "HttpRequestException")]
    public void MissingListAndValidEmptyListAreDifferent(bool hasList, string? errorType, string? expected) =>
        Assert.Equal(expected, ExportMetadata.ListReadError(hasList, errorType));

    [Fact]
    public void PartialGachaIsVisibleButOrdinaryNamesAreNotWarnings()
    {
        Assert.True(ExportMetadata.HasWarnings(new { gacha = new { errorType = "HttpRequestException", cases = Array.Empty<object>() } }));
        Assert.False(ExportMetadata.HasWarnings(new { name = "unavailable", gacha = new { errorType = (string?)null, cases = Array.Empty<object>() } }));
        Assert.True(ExportMetadata.HasWarnings(new { items = new[] { new { metadataStatus = "unresolved" } } }));
    }

    // Synthetic payload only. Tests the real serializers/ZIP packers, not live login or battle math.
    private static Dictionary<string, object?> Snapshot() => new()
    {
        ["schema"] = MobileSnapshot.Schema,
        ["generatedAtUtc"] = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        ["source"] = new { exporter = "synthetic-test" },
        ["selectedSections"] = ExportSelection.DefaultSections(),
        ["player"] = new { name = "Synthetic", rank = 1 },
        ["progress"] = new { bossClearMaxQuestId = 1 },
        ["levelLink"] = new { partyLevel = 100 },
        ["levelLinkMembers"] = new[] { new { cellNo = 0, instanceIndex = 1, characterId = 7 } },
        ["characters"] = new[] { new { instanceIndex = 1, characterId = 7, rawLevel = 1, effectiveLevel = 100, isLevelLinkMember = true,
            stats = new { hp = 100L, attack = 20L, speed = 3000L } } },
        ["equipment"] = new[] { new { instanceIndex = 1, characterId = 7,
            equipment = new[] { new { equipmentId = 42, runes = new[] { ExportMetadata.MissingSphere(999999) } } } } },
        ["decks"] = new[] { new { deckNo = 1, slots = new[] { new { slot = 1, instanceIndex = 1, characterId = 7 } } } },
        ["items"] = new[] { new { itemType = "Gold", itemId = 1, count = 123L, metadataStatus = "resolved" } },
        ["gacha"] = new { errorType = "InvalidDataException", cases = Array.Empty<object>() }
    };

    private static byte[] Desktop(string method, params object?[] args) =>
        (byte[])typeof(MementoMori.WebUI.SafeExport).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DesktopAndAndroidSerializeTheSameContract(bool pretty)
    {
        var snapshot = Snapshot();
        Assert.Equal(Desktop("SerializeSafe", snapshot, pretty), MobileSnapshot.SerializeSafe(snapshot, pretty));
    }

    [Fact]
    public void AllEightZipSectionsMatchIncludingLinkMembersAndFailureMarkers()
    {
        var snapshot = Snapshot();
        var selected = ExportSelection.DefaultSections();
        using var desktop = new ZipArchive(new MemoryStream(Desktop("BuildZip", snapshot, selected.ToHashSet(StringComparer.OrdinalIgnoreCase))));
        using var android = new ZipArchive(new MemoryStream(MobileSnapshot.Zip(snapshot, selected)));
        Assert.Equal(9, android.Entries.Count);
        Assert.Equal(desktop.Entries.Select(e => e.FullName).Order(), android.Entries.Select(e => e.FullName).Order());
        foreach (var entry in desktop.Entries)
        {
            using var left = new StreamReader(entry.Open());
            using var right = new StreamReader(android.GetEntry(entry.FullName)!.Open());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(left.ReadToEnd()), JsonNode.Parse(right.ReadToEnd())), entry.FullName);
        }
        using var linkReader = new StreamReader(android.GetEntry("level-link.json")!.Open());
        using var link = JsonDocument.Parse(linkReader.ReadToEnd());
        Assert.Equal(1, link.RootElement.GetProperty("levelLinkMembers").GetArrayLength());
    }

    [Theory]
    [InlineData("clientkey")]
    [InlineData("password")]
    [InlineData("authtoken")]
    [InlineData("token")]
    [InlineData("session")]
    [InlineData("credential")]
    [InlineData("guid")]
    [InlineData("playerid")]
    [InlineData("userid")]
    public void BothSerializersRejectSensitiveProperties(string key)
    {
        var value = new { nested = new[] { new Dictionary<string, object?> { [key] = "synthetic-only" } } };
        Assert.Throws<InvalidOperationException>(() => MobileSnapshot.SerializeSafe(value));
        var exception = Assert.Throws<TargetInvocationException>(() => Desktop("SerializeSafe", value, false));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }
}
