using MessagePack;
using MementoMori.Ortega;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master;
using Xunit;

namespace Exporter.Android.Tests;

public sealed class ProtocolSerializationTests
{
    [Fact]
    public void DefaultProtocolIsUntrustedAndDepthBounded()
    {
        Assert.True(ProtocolSerialization.Options.Security.HashCollisionResistant);
        Assert.Equal(128, ProtocolSerialization.Options.Security.MaximumObjectGraphDepth);
        Assert.Same(ProtocolSerialization.Options, MessagePackSerializer.DefaultOptions);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-3L)]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void ExistingItemMapAndInt64AmountsRoundTripUnchanged(long amount)
    {
        var item = new UserItemDtoInfo { ItemType = ItemType.Gold, ItemId = 1, ItemCount = amount, PlayerId = 123 };
        var oldFormat = MessagePackSerializer.Serialize(item, MessagePackSerializerOptions.Standard);
        var hardenedFormat = MessagePackSerializer.Serialize(item, ProtocolSerialization.Options);
        Assert.Equal(oldFormat, hardenedFormat);
        var read = MessagePackSerializer.Deserialize<UserItemDtoInfo>(oldFormat);
        Assert.Equal(amount, read.ItemCount);
        Assert.Equal(item.ItemType, read.ItemType);
        Assert.Equal(1L, read.ItemId);
    }

    [Fact]
    public void UnknownServerFieldsDoNotEraseKnownFields()
    {
        var bytes = MessagePackSerializer.ConvertFromJson("""{"ItemType":3,"ItemId":1,"ItemCount":9007199254740993,"FutureField":"ignored"}""");
        var item = MessagePackSerializer.Deserialize<UserItemDtoInfo>(bytes);
        Assert.Equal(9007199254740993L, item.ItemCount);
        Assert.Equal(1L, item.ItemId);
    }

    [Fact]
    public void EquipmentAndRuneIdentifiersRetainTheirProtocolRepresentation()
    {
        var dto = new UserEquipmentDtoInfo { Guid = "synthetic-equipment", CharacterGuid = "synthetic-character",
            EquipmentId = 42, SphereId1 = 67, SphereId2 = 49, ReinforcementLv = 140 };
        var bytes = MessagePackSerializer.Serialize(dto, MessagePackSerializerOptions.Standard);
        var read = MessagePackSerializer.Deserialize<UserEquipmentDtoInfo>(bytes);
        Assert.Equal(dto.Guid, read.Guid);
        Assert.Equal(dto.CharacterGuid, read.CharacterGuid);
        Assert.Equal(67L, read.SphereId1);
        Assert.Equal(140L, read.ReinforcementLv);
    }

    [Fact]
    public void MasterCatalogStringDictionaryRemainsCompatible()
    {
        var input = new MasterBookCatalog();
        input.MasterBookInfoMap.Add("SyntheticMB", new MasterBookInfo { Name = "SyntheticMB", Hash = "synthetic", Size = 7 });
        var output = MessagePackSerializer.Deserialize<MasterBookCatalog>(MessagePackSerializer.Serialize(input));
        Assert.Equal(7, output.MasterBookInfoMap["SyntheticMB"].Size);
    }

    [Theory]
    [InlineData(MessagePackCompression.Lz4Block)]
    [InlineData(MessagePackCompression.Lz4BlockArray)]
    public void ExistingCompressionModesRemainCompatible(MessagePackCompression compression)
    {
        var options = ProtocolSerialization.Options.WithCompression(compression);
        var payload = new string('a', 4096);
        Assert.Equal(payload, MessagePackSerializer.Deserialize<string>(MessagePackSerializer.Serialize(payload, options), options));
    }

    [Fact]
    public void ExcessiveObjectNestingIsRejected()
    {
        var value = new DepthNode();
        for (var i = 0; i < ProtocolSerialization.MaximumDepth + 10; i++) value = new DepthNode { Child = value };
        var bytes = MessagePackSerializer.Serialize(value, MessagePackSerializerOptions.Standard);
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<DepthNode>(bytes));
    }

    [Fact]
    public void TruncatedInputIsRejected() => Assert.Throws<MessagePackSerializationException>(() =>
        MessagePackSerializer.Deserialize<UserItemDtoInfo>(new byte[] { 0x81 }));

    [MessagePackObject]
    public sealed class DepthNode
    {
        [Key(0)] public DepthNode? Child { get; set; }
    }
}
