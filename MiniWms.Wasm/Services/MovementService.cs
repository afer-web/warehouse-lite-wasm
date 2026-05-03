using MiniWms.Wasm.Models;

namespace MiniWms.Wasm.Services;

/// <summary>Registers immutable ledger rows; SQL trigger adjusts <c>Stocks</c> rollups automatically.</summary>
public sealed class MovementService(DatabaseService db)
{
    readonly DatabaseService _db = db;

    public Task<IReadOnlyList<Movement>> GetRecentAsync(int take = 12, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        return _db.QueryAsync<Movement>(
            $"""
             SELECT Id, ItemId, LocationId, Quantity, Type, Timestamp
             FROM Movements
             ORDER BY Timestamp DESC
             LIMIT {take};
             """,
            ct: ct);
    }

    public Task<IReadOnlyList<Movement>> GetAllPagedAsync(CancellationToken ct = default) =>
        _db.QueryAsync<Movement>(
            """
            SELECT Id, ItemId, LocationId, Quantity, Type, Timestamp
            FROM Movements
            ORDER BY Timestamp DESC
            LIMIT 500;
            """,
            ct: ct);

    /// <summary>Ledger projection with SKU / bin codes for dashboards and listings.</summary>
    public Task<IReadOnlyList<MovementListRow>> GetLedgerSliceAsync(int take = 250, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 750);
        return _db.QueryAsync<MovementListRow>(
            $"""
             SELECT m.Id,
                    m.ItemId,
                    m.LocationId,
                    m.Quantity,
                    m.Type,
                    m.Timestamp,
                    i.Code AS itemCode,
                    l.Code AS locationCode
             FROM Movements m
             INNER JOIN Items i ON i.Id = m.ItemId
             INNER JOIN Locations l ON l.Id = m.LocationId
             ORDER BY datetime(m.Timestamp) DESC
             LIMIT {take};
             """,
            ct: ct);
    }

    /// <exception cref="InvalidOperationException">Invalid quantity / insufficient stock.</exception>
    public async Task<long> RecordMovementAsync(long itemId, long locationId, double quantity,
        MovementType type, CancellationToken ct = default)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("Quantity must be positive.");
        }

        if (type == MovementType.Unload)
        {
            var balances = await _db
                .QueryAsync<SqlQty>(
                    "SELECT Quantity AS qty FROM Stocks WHERE ItemId=? AND LocationId=? LIMIT 1;",
                    new object?[] { itemId, locationId },
                    ct)
                .ConfigureAwait(false);
            var onHand = balances.FirstOrDefault()?.Qty ?? 0d;
            if (onHand + 1e-9 < quantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient quantity on-hand ({onHand:0.##}) for unloading {quantity:0.##}.");
            }
        }

        var ts = DateTimeOffset.UtcNow.ToString("O");
        await _db
            .ExecuteAsync(
                """
                INSERT INTO Movements(ItemId, LocationId, Quantity, Type, Timestamp)
                VALUES(?,?,?,?,?);
                """,
                new object?[] { itemId, locationId, quantity, type, ts },
                ct)
            .ConfigureAwait(false);

        var key = await _db
            .QueryAsync<SqlRowId>(
                "SELECT last_insert_rowid() AS rowId;",
                ct: ct)
            .ConfigureAwait(false);
        return key.FirstOrDefault()?.RowId ?? 0;
    }
}
