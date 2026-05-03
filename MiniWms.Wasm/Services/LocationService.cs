using MiniWms.Wasm.Models;

namespace MiniWms.Wasm.Services;

/// <summary>Bin / aisle master CRUD façade.</summary>
public sealed class LocationService(DatabaseService db)
{
    readonly DatabaseService _db = db;

    public Task<IReadOnlyList<Location>> GetAllAsync(CancellationToken ct = default) =>
        _db.QueryAsync<Location>(
            "SELECT Id, Code, Area, Description FROM Locations ORDER BY Code COLLATE NOCASE;",
            ct: ct);

    public async Task<Location?> FindAsync(long id, CancellationToken ct = default)
    {
        var rows = await _db
            .QueryAsync<Location>(
                "SELECT Id, Code, Area, Description FROM Locations WHERE Id=? LIMIT 1;",
                new object?[] { id },
                ct)
            .ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<long> UpsertAsync(Location draft, CancellationToken ct = default)
    {
        if (draft.Id == 0)
        {
            await _db
                .ExecuteAsync(
                    "INSERT INTO Locations (Code, Area, Description) VALUES (?,?,?);",
                    new object?[] { draft.Code, draft.Area, draft.Description },
                    ct)
                .ConfigureAwait(false);

            var key = await _db
                .QueryAsync<SqlRowId>(
                    "SELECT last_insert_rowid() AS rowId;",
                    ct: ct)
                .ConfigureAwait(false);
            return key.FirstOrDefault()?.RowId ?? 0;
        }

        await _db
            .ExecuteAsync(
                "UPDATE Locations SET Code=?, Area=?, Description=? WHERE Id=?;",
                new object?[] { draft.Code, draft.Area, draft.Description, draft.Id },
                ct)
            .ConfigureAwait(false);
        return draft.Id;
    }

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        _db.ExecuteAsync("DELETE FROM Locations WHERE Id=?;", new object?[] { id }, ct);

}
