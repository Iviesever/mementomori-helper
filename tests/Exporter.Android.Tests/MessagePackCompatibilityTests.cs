using System.Buffers;
using MessagePack;
using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Data;
using Xunit;

namespace Exporter.Android.Tests;

public sealed class MessagePackCompatibilityTests
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithSecurity(MessagePackSecurity.UntrustedData);

    // Fixed standard MessagePack map, not produced by the serializer under test.
    // Synthetic values only: ItemCount=2^53+1, ItemId=1, ItemType=Gold(3), PlayerId=0.
    private const string ItemWire = "84A94974656D436F756E74D30020000000000001A64974656D496401A84974656D5479706503A8506C61796572496400";

    [Fact]
    public void FixedMapFixturePreservesTheWireContractAndLargeIntegers()
    {
        var value = MessagePackSerializer.Deserialize<UserItemDtoInfo>(Convert.FromHexString(ItemWire), Options);
        Assert.Equal(9007199254740993L, value.ItemCount);
        Assert.Equal(1, value.ItemId);
        Assert.Equal(ItemType.Gold, value.ItemType);
        Assert.Equal(0, value.PlayerId);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void ItemCountsDoNotRoundOrClamp(long count)
    {
        var source = new UserItemDtoInfo { ItemCount = count, ItemId = 99, ItemType = (ItemType)9999 };
        var result = MessagePackSerializer.Deserialize<UserItemDtoInfo>(MessagePackSerializer.Serialize(source, Options), Options);
        Assert.Equal(count, result.ItemCount);
        Assert.Equal((ItemType)9999, result.ItemType);
    }

    [Theory]
    [InlineData(MessagePackCompression.None)]
    [InlineData(MessagePackCompression.Lz4Block)]
    [InlineData(MessagePackCompression.Lz4BlockArray)]
    public void ActualSyncDtoRoundtripsWithSupportedCompression(MessagePackCompression compression)
    {
        var options = Options.WithCompression(compression);
        var data = new UserSyncData
        {
            UserItemDtoInfo = Enumerable.Range(0, 64).Select(i => new UserItemDtoInfo
                { ItemId = i, ItemType = ItemType.Gold, ItemCount = 9007199254740993L }).ToList(),
            UserCharacterDtoInfos = new List<UserCharacterDtoInfo>(),
            UserEquipmentDtoInfos = new List<UserEquipmentDtoInfo>
            {
                new() { EquipmentId = 42, Guid = "synthetic-equipment", CharacterGuid = "synthetic-character",
                    SphereId1 = 3, SphereId4 = 6, ReinforcementLv = 100 }
            }
        };
        var decoded = MessagePackSerializer.Deserialize<UserSyncData>(MessagePackSerializer.Serialize(data, options), options);
        Assert.Equal(64, decoded.UserItemDtoInfo.Count);
        Assert.All(decoded.UserItemDtoInfo, item => Assert.Equal(9007199254740993L, item.ItemCount));
        Assert.Empty(decoded.UserCharacterDtoInfos);
        Assert.Equal(new long[] { 3, 0, 0, 6 }, Assert.Single(decoded.UserEquipmentDtoInfos).GetSphereIds());
        Assert.Equal(100, decoded.UserEquipmentDtoInfos[0].ReinforcementLv);
    }

    [Fact]
    public void ReadOnlyMasterDataStillUsesItsSerializationConstructor()
    {
        var source = new SphereMB(55, false, "synthetic", null!, null!, 1, SphereType.Small,
            "description", true, Array.Empty<MementoMori.Ortega.Share.Data.Item.UserItem>(), 7, "name", ItemRarityFlags.SSR);
        var decoded = MessagePackSerializer.Deserialize<SphereMB>(MessagePackSerializer.Serialize(source, Options), Options);
        Assert.Equal(55, decoded.Id);
        Assert.Equal(7, decoded.Lv);
        Assert.True(decoded.IsAttackType);
        Assert.Equal(ItemRarityFlags.SSR, decoded.RarityFlags);
    }

    [Fact]
    public void UnknownMapFieldsCanBeSkippedWithoutLosingKnownFields()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);
        writer.Write("FutureServerField"); writer.WriteArrayHeader(2); writer.Write(1); writer.WriteNil();
        writer.Write("ItemCount"); writer.Write(42L); writer.Flush();
        Assert.Equal(42, MessagePackSerializer.Deserialize<UserItemDtoInfo>(buffer.WrittenMemory, Options).ItemCount);
    }

    [Fact]
    public void NilRemainsNil() => Assert.Null(MessagePackSerializer.Deserialize<UserSyncData?>(new byte[] { 0xc0 }, Options));

    [Fact]
    public void TruncatedInputIsRejected()
    {
        var bytes = Convert.FromHexString(ItemWire);
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<UserItemDtoInfo>(bytes[..^1], Options));
    }

    [Fact]
    public void UntrustedInputDepthLimitIsEnforced()
    {
        var bytes = Enumerable.Repeat((byte)0x91, 32).Append((byte)0xc0).ToArray();
        var options = Options.WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(8));
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<object>(bytes, options));
    }
}
