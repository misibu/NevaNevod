namespace NevaVision.Core;

/// <summary>
/// Read-only pointer layout observed in RF4 4.0.25034 while examining the
/// external overlay behavior. It is intentionally kept in one file so a new
/// game build can be supported without changing the UI.
/// </summary>
public static class Rf4Signature
{
    public const string TargetBuild = "4.0.25034";

    // GameAssembly.dll + this RVA is the root used by the original external reader.
    public const ulong RootRva = 0x40BE5B8;

    public static readonly ulong[] RootObjectChain = { 0xB8, 0x80, 0x10, 0x100 };

    // Three rod chains. Each chain is resolved from RootAddress.
    public static readonly ulong[][] RodChains =
    {
        new[] { 0xB8UL, 0x148, 0x30, 0x80, 0x38 },
        new[] { 0xB8UL, 0x148, 0x30, 0x80, 0x58 },
        new[] { 0xB8UL, 0x148, 0x30, 0x80, 0x78 }
    };

    // These chains are resolved from a rod/object pointer returned above.
    public static readonly ulong[] WeightChain = { 0x50, 0x30, 0x28 };
    public static readonly ulong[] StateChain = { 0x50, 0x30, 0x44 };
    public static readonly ulong[] SlotChain = { 0xE0, 0x38, 0x20, 0x40, 0x40, 0x48 };
    public static readonly ulong[] PositionChain = { 0x40, 0x10, 0x38, 0x18, 0x0 };
    public static readonly ulong[] FishNameChain = { 0x60, 0x10, 0x60, 0x0 };

    // Player position used by the distance calculation in the external reader.
    public static readonly ulong[] PlayerPositionChain = { 0xB8, 0x88, 0x98, 0x10, 0x38, 0x18, 0x0 };

    // A one-byte object flag used for the rare/marked color in the overlay.
    public static readonly ulong[] RarityChain = { 0x50, 0x55 };

    public const double GramsPerKilogram = 1000.0;
}
