namespace MiniWms.Wasm.Models;

/// <summary>Derived on-hand quantity for an SKU at a bin.</summary>
public sealed record StockBalance(
    long ItemId,
    string ItemCode,
    long LocationId,
    string LocationCode,
    double Quantity);
