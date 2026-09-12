using System;
using System.IO;
using System.Text.Json;
using MementoMori.Common.Localization;
using MementoMori.Ortega.Share;

namespace MementoMori.Export;

// Shared by both exporters. Never repair account amounts or IDs by guessing.
public static class ExportMetadata
{
    public static string AndroidExporterIdentity(string applicationVersion)
    {
        if (!global::System.Version.TryParse(applicationVersion, out var version))
            throw new ArgumentException("An actual application version is required.", nameof(applicationVersion));
        return "android-native-v" + version;
    }

    public static object MissingSphere(long sphereId) => new
    {
        sphereId, name = (string?)null, level = (long?)null, type = (string?)null,
        rarity = (string?)null, isAttackType = (bool?)null, metadataStatus = "unavailable"
    };

    public static string ItemMetadataStatus(string? name, string? rarity) =>
        IsUnresolved(name) || IsUnresolved(rarity) ? "unresolved" : "resolved";

    private static bool IsUnresolved(string? value) => string.IsNullOrWhiteSpace(value) ||
        value == ResourceStrings.Unknown || (value.StartsWith('[') && value.EndsWith(']'));

    // An explicitly empty list is valid; a missing response/list is not.
    public static string? ListReadError(bool hasList, string? errorType) =>
        errorType ?? (hasList ? null : nameof(InvalidDataException));

    // EquipmentFragment IDs address EquipmentCompositeMB, not EquipmentMB.
    public static string EquipmentFragmentRarity(long fragmentId) => ResolveEquipmentFragmentRarity(fragmentId,
        id => (Masters.EquipmentCompositeTable.GetById(id)
            ?? throw new InvalidDataException("Equipment fragment metadata is missing.")).EquipmentId,
        id => (Masters.EquipmentTable.GetById(id)
            ?? throw new InvalidDataException("Equipment metadata is missing.")).RarityFlags.ToString());

    public static string ResolveEquipmentFragmentRarity(long fragmentId,
        Func<long, long> equipmentIdForFragment, Func<long, string> rarityForEquipment) =>
        rarityForEquipment(equipmentIdForFragment(fragmentId));

    // Accept only the explicit safe projection here, never a raw response.
    public static bool HasWarnings(object snapshot) => HasWarnings(JsonSerializer.SerializeToElement(snapshot));

    private static bool HasWarnings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.NameEquals("metadataStatus") && property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() is "unresolved" or "unavailable") return true;
                if (property.NameEquals("errorType") && property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(property.Value.GetString())) return true;
                if (HasWarnings(property.Value)) return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                if (HasWarnings(item)) return true;
        return false;
    }
}
