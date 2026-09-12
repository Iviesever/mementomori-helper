using System.Runtime.CompilerServices;
using MessagePack;

namespace MementoMori.Ortega;

/// <summary>Apply bounded, untrusted-data handling to the existing MessagePack protocol.</summary>
public static class ProtocolSerialization
{
    public const int MaximumDepth = 128;
    public static MessagePackSerializerOptions Options { get; } =
        MessagePackSerializerOptions.Standard.WithSecurity(
            MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(MaximumDepth));

    // Existing HTTP, master-table and MagicOnion call sites use DefaultOptions.
    // Keep the same Standard resolver and map keys; do not enable typeless deserialization.
    [ModuleInitializer]
    public static void Initialize() => MessagePackSerializer.DefaultOptions = Options;
}
