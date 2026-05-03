using MiniWms.Wasm.Models;

namespace MiniWms.Wasm.Services;

/// <summary>Read models for on-hand balances and dashboard aggregations.</summary>
public sealed class InventoryService(DatabaseService db)
{
    readonly DatabaseService _db = db;

    public Task<IReadOnlyList<StockBalance>> GetBalancesAsync(CancellationToken ct = default) =>
        _db.QueryAsync<StockBalance>(
            """
            SELECT s.ItemId,
                   i.Code AS itemCode,
                   s.LocationId,
                   l.Code AS locationCode,
                   s.Quantity
            FROM Stocks s
            INNER JOIN Items i ON i.Id = s.ItemId
            INNER JOIN Locations l ON l.Id = s.LocationId
            WHERE s.Quantity <> 0
            ORDER BY i.Code, l.Code;
            """,
            ct: ct);

    public Task<IReadOnlyList<StockByItemAggregate>> AggregateByItemsAsync(CancellationToken ct = default) =>
        _db.QueryAsync<StockByItemAggregate>(
            """
            SELECT i.Code AS sku,
                   SUM(s.Quantity) AS totalQty
            FROM Stocks s
            INNER JOIN Items i ON i.Id = s.ItemId
            GROUP BY i.Id
            HAVING SUM(s.Quantity) <> 0
            ORDER BY sku;
            """,
            ct: ct);
}

/// <summary>Dashboard-friendly projection for CSS bar telemetry.</summary>
public sealed record StockByItemAggregate(string Sku, double TotalQty);
