namespace MiniWms.Wasm.Models;

/// <summary>Movement row enriched with human-readable master data for grids.</summary>
public sealed record MovementListRow(
    long Id,
    long ItemId,
    long LocationId,
    double Quantity,
    MovementType Type,
    string Timestamp,
    string ItemCode,
    string LocationCode);
