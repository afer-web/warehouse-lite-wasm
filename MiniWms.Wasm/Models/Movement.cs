namespace MiniWms.Wasm.Models;

/// <summary>Stock ledger entry referencing item and location.</summary>
public sealed record Movement(long Id, long ItemId, long LocationId, double Quantity, MovementType Type, string Timestamp);
