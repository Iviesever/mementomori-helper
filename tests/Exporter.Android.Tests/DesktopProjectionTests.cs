using System.Reflection;
using System.Text;
using System.Text.Json;
using MementoMori.Export;
using MementoMori.Exporter.Android.Services;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Share.Data.ApiInterface.Gacha;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Data.Gacha;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Data;
using MementoMori.Ortega.Share.Master.Table;
using MementoMori.WebUI;
using MessagePack;
using Xunit;

namespace Exporter.Android.Tests;

// Masters are process-wide. Do not run this fixture alongside other collections, and restore
// every table after each test. All records are synthetic; nothing is loaded from the game.
[CollectionDefinition("Synthetic export masters", DisableParallelization = true)]
public sealed class SyntheticExportMastersCollection { }

[Collection("Synthetic export masters")]
public sealed class DesktopProjectionTests
{
    private static Task<Dictionary<string, object?>> Build(UserSyncData data, string[] sections,
        Func<Task<GetListResponse?>>? list = null) => SafeExport.BuildSnapshotDataAsync(data, 123,
            sections.ToHashSet(StringComparer.OrdinalIgnoreCase),
            list ?? (() => throw new InvalidOperationException("Unselected gacha must not be queried.")));

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    private static UserSyncData EquipmentData(long sphereId = 999999) => new()
    {
        UserCharacterDtoInfos = new List<UserCharacterDtoInfo>
        {
            new() { Guid = "synthetic-instance", CharacterId = 777 }
        },
        UserEquipmentDtoInfos = new List<UserEquipmentDtoInfo>
        {
            new() { Guid = "synthetic-equipment", CharacterGuid = "synthetic-instance",
                EquipmentId = 9001, SphereId1 = sphereId, SphereUnlockedCount = 1, ReinforcementLv = 12 }
        }
    };

    private static JsonElement FirstEquipment(Dictionary<string, object?> snapshot) =>
        Json(snapshot["equipment"])[0].GetProperty("equipment")[0];

    [Fact]
    public async Task ActualProjectionKeepsEverySelectedSectionAndBothLevelLinkObjects()
    {
        var calls = 0;
        var snapshot = await Build(new UserSyncData(), ExportSelection.DefaultSections(), () =>
        {
            calls++;
            return Task.FromResult<GetListResponse?>(new GetListResponse { GachaCaseInfoList = new List<GachaCaseInfo>() });
        });
        foreach (var section in ExportSelection.DefaultSections()) Assert.True(snapshot.ContainsKey(section), section);
        Assert.True(snapshot.ContainsKey("levelLinkMembers"));
        Assert.Equal(ExportSelection.DefaultSections(), Json(snapshot["selectedSections"]).EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1, calls);
        Assert.Equal("upstream-baseline", Json(snapshot["source"]).GetProperty("helperCommitKind").GetString());
        Assert.Equal("selective-ui-v3.1.1", Json(snapshot["source"]).GetProperty("exporter").GetString());
    }

    [Fact]
    public async Task ActualProjectionDoesNotFetchUnselectedGacha()
    {
        var snapshot = await Build(new UserSyncData(), new[] { "player", "items" });
        Assert.False(snapshot.ContainsKey("gacha"));
        Assert.False(snapshot.ContainsKey("equipment"));
        Assert.False(snapshot.ContainsKey("levelLinkMembers"));
        Assert.Equal(0, Json(snapshot["items"]).GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingResponseOrListIsAnErrorNotAValidEmptyList(bool hasResponse)
    {
        GetListResponse? response = hasResponse ? new GetListResponse { GachaCaseInfoList = null! } : null;
        var snapshot = await Build(new UserSyncData(), new[] { "gacha" }, () => Task.FromResult(response));
        var result = Json(snapshot["gacha"]);
        Assert.Equal("InvalidDataException", result.GetProperty("errorType").GetString());
        Assert.Equal(0, result.GetProperty("cases").GetArrayLength());
        Assert.True(ExportMetadata.HasWarnings(snapshot));
    }

    [Fact]
    public async Task ExplicitEmptyListIsNotReportedAsAReadFailure()
    {
        var snapshot = await Build(new UserSyncData(), new[] { "gacha" }, () =>
            Task.FromResult<GetListResponse?>(new GetListResponse { GachaCaseInfoList = new List<GachaCaseInfo>() }));
        Assert.Equal(JsonValueKind.Null, Json(snapshot["gacha"]).GetProperty("errorType").ValueKind);
        Assert.False(ExportMetadata.HasWarnings(snapshot));
    }

    [Fact]
    public async Task ListFailureKeepsOnlyExceptionTypeAndDoesNotDiscardOtherSections()
    {
        var snapshot = await Build(new UserSyncData(), new[] { "items", "gacha" }, () =>
            Task.FromException<GetListResponse?>(new HttpRequestException("synthetic-remote-secret")));
        Assert.True(snapshot.ContainsKey("items"));
        Assert.Equal("HttpRequestException", Json(snapshot["gacha"]).GetProperty("errorType").GetString());
        Assert.DoesNotContain("synthetic-remote-secret", Encoding.UTF8.GetString(MobileSnapshot.SerializeSafe(snapshot)));
    }

    [Fact]
    public async Task CancellationDoesNotProduceASuccessfulPartialSnapshot()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Build(new UserSyncData(), new[] { "gacha" }, () =>
            Task.FromException<GetListResponse?>(new OperationCanceledException("synthetic cancellation"))));
    }

    [Fact]
    public async Task RealGachaProjectionKeepsCountsAndSortsByDisplayOrder()
    {
        var snapshot = await Build(new UserSyncData(), new[] { "gacha" }, () => Task.FromResult<GetListResponse?>(
            new GetListResponse
            {
                GachaCaseInfoList = new List<GachaCaseInfo>
                {
                    new() { GachaCaseId = 999992, DisplayOrder = 20, GachaDrawCount = 7, GachaCeilingCount = 100 },
                    new() { GachaCaseId = 999991, DisplayOrder = 10, GachaBonusDrawCount = 42 }
                }
            }));
        var cases = Json(snapshot["gacha"]).GetProperty("cases");
        Assert.Equal(2, cases.GetArrayLength());
        Assert.Equal(999991, cases[0].GetProperty("gachaCaseId").GetInt64());
        Assert.Equal(42, cases[0].GetProperty("bonusDrawCount").GetInt64());
        Assert.Equal(7, cases[1].GetProperty("drawCount").GetInt64());
        Assert.Equal(100, cases[1].GetProperty("ceilingCount").GetInt64());
    }

    [Fact]
    public async Task MissingSphereKeepsIdButNeverInventsLevelOrAttackType()
    {
        using var spheres = new MasterScope<SphereMB>(Masters.SphereTable, "[]");
        var snapshot = await Build(EquipmentData(), new[] { "equipment" });
        var rune = FirstEquipment(snapshot).GetProperty("runes")[0];
        Assert.Equal(999999, rune.GetProperty("sphereId").GetInt64());
        Assert.Equal(JsonValueKind.Null, rune.GetProperty("level").ValueKind);
        Assert.Equal(JsonValueKind.Null, rune.GetProperty("isAttackType").ValueKind);
        Assert.Equal("unavailable", rune.GetProperty("metadataStatus").GetString());
        Assert.Equal(12, FirstEquipment(snapshot).GetProperty("reinforcementLevel").GetInt64());
        Assert.True(ExportMetadata.HasWarnings(snapshot));
        var text = Encoding.UTF8.GetString(MobileSnapshot.SerializeSafe(snapshot));
        Assert.DoesNotContain("synthetic-instance", text);
        Assert.DoesNotContain("synthetic-equipment", text);
    }

    [Fact]
    public async Task UntranslatedSpherePreservesKnownLevelAndAttackType()
    {
        var rows = JsonSerializer.Serialize(new[] { new
        {
            Id = 999999L, NameKey = "[SyntheticSphereMissingTranslation]", Lv = 7L,
            IsAttackType = true, SphereType = (int)SphereType.Medium, RarityFlags = (int)ItemRarityFlags.SSR
        } });
        using var spheres = new MasterScope<SphereMB>(Masters.SphereTable, rows);
        var snapshot = await Build(EquipmentData(), new[] { "equipment" });
        var rune = FirstEquipment(snapshot).GetProperty("runes")[0];
        Assert.Equal(7, rune.GetProperty("level").GetInt64());
        Assert.True(rune.GetProperty("isAttackType").GetBoolean());
        Assert.Equal("Medium", rune.GetProperty("type").GetString());
        Assert.Equal("SSR", rune.GetProperty("rarity").GetString());
        Assert.Equal("[SyntheticSphereMissingTranslation]", rune.GetProperty("name").GetString());
        Assert.Equal("unresolved", rune.GetProperty("metadataStatus").GetString());
    }

    [Fact]
    public async Task RealInventoryProjectionUsesCompositeIdRatherThanSameNumberedEquipment()
    {
        using var composites = new MasterScope<EquipmentCompositeMB>(Masters.EquipmentCompositeTable,
            """[{"Id":42,"EquipmentId":9001}]""");
        using var equipment = new MasterScope<EquipmentMB>(Masters.EquipmentTable,
            """[{"Id":42,"NameKey":"[WrongEquipment]","RarityFlags":1},{"Id":9001,"NameKey":"[MappedEquipment]","RarityFlags":64}]""");
        var data = new UserSyncData
        {
            UserItemDtoInfo = new List<UserItemDtoInfo>
            {
                new() { ItemType = ItemType.EquipmentFragment, ItemId = 42, ItemCount = 15 }
            }
        };
        var snapshot = await Build(data, new[] { "items" });
        var item = Json(snapshot["items"])[0];
        Assert.Equal(42, item.GetProperty("itemId").GetInt64());
        Assert.Equal(15, item.GetProperty("count").GetInt64());
        Assert.Equal(Masters.EquipmentTable.GetById(9001).RarityFlags.ToString(), item.GetProperty("rarity").GetString());
        Assert.NotEqual(Masters.EquipmentTable.GetById(42).RarityFlags.ToString(), item.GetProperty("rarity").GetString());
        Assert.Equal("unresolved", item.GetProperty("metadataStatus").GetString());
    }

    [Fact]
    public async Task MissingFragmentMappingDoesNotGuessFromAnUnrelatedEquipment()
    {
        using var composites = new MasterScope<EquipmentCompositeMB>(Masters.EquipmentCompositeTable, "[]");
        using var equipment = new MasterScope<EquipmentMB>(Masters.EquipmentTable,
            """[{"Id":42,"NameKey":"Synthetic unrelated equipment","RarityFlags":1}]""");
        var snapshot = await Build(new UserSyncData
        {
            UserItemDtoInfo = new List<UserItemDtoInfo>
            {
                new() { ItemType = ItemType.EquipmentFragment, ItemId = 42, ItemCount = 3 }
            }
        }, new[] { "items" });
        var item = Json(snapshot["items"])[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("rarity").ValueKind);
        Assert.Equal(3, item.GetProperty("count").GetInt64());
        Assert.Equal("unresolved", item.GetProperty("metadataStatus").GetString());
    }

    [Fact]
    public async Task InventoryCountsRemainExactWhenMetadataCannotBeResolved()
    {
        var snapshot = await Build(new UserSyncData
        {
            UserItemDtoInfo = new List<UserItemDtoInfo>
            {
                new() { ItemType = ItemType.ChatEmoticon, ItemId = 999992, ItemCount = 9007199254740993L },
                new() { ItemType = ItemType.ChatEmoticon, ItemId = 999991, ItemCount = -1 },
                new() { ItemType = ItemType.ChatEmoticon, ItemId = 999990, ItemCount = 0 }
            }
        }, new[] { "items" });
        var items = Json(snapshot["items"]);
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(999991, items[0].GetProperty("itemId").GetInt64());
        Assert.Equal(-1, items[0].GetProperty("count").GetInt64());
        Assert.Equal(9007199254740993L, items[1].GetProperty("count").GetInt64());
        Assert.Equal("unresolved", items[1].GetProperty("metadataStatus").GetString());
        Assert.True(ExportMetadata.HasWarnings(snapshot));
    }

    private sealed class MasterScope<T> : IDisposable where T : MasterBookBase
    {
        private readonly TableBase<T> table;
        private readonly FieldInfo field = typeof(TableBase<T>).GetField("_datas", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly object? original;

        public MasterScope(TableBase<T> table, string json)
        {
            this.table = table;
            original = field.GetValue(table);
            try { table.Load(MessagePackSerializer.ConvertFromJson(json)); }
            catch { field.SetValue(table, original); throw; }
        }

        public void Dispose() => field.SetValue(table, original);
    }
}
