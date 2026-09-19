namespace NevaVision.Core;

public sealed record FishObservation(
    int RodIndex,
    bool IsActive,
    bool IsHooked,
    string FishId,
    string FishName,
    double? WeightKg,
    double? DistanceMeters,
    int? Slot,
    bool IsRare,
    string? RawName = null);

public sealed record ObservationSnapshot(
    DateTimeOffset Timestamp,
    bool Attached,
    int? ProcessId,
    string Status,
    IReadOnlyList<FishObservation> Observations);

public sealed class OverlayOptions
{
    public bool ShowFish { get; set; } = true;
    public bool ShowName { get; set; } = true;
    public bool ShowWeight { get; set; } = true;
    public bool ShowDistance { get; set; } = true;
    public bool ShowRarity { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool ShowRodSlot { get; set; } = true;
    public bool BiteAssistant { get; set; } = true;
}
