using MiniWms.Wasm.Models;

namespace MiniWms.Wasm.Services;

/// <summary>CRUD façade for <see cref="Item"/> rows persisted in WASM SQLite.</summary>
public sealed class ItemService(DatabaseService db)
{
    readonly DatabaseService _db = db;

    public Task<IReadOnlyList<Item>> GetAllAsync(CancellationToken ct = default) =>
        _db.QueryAsync<Item>(
            "SELECT Id, Code, Description, Unit, CreatedAt FROM Items ORDER BY Code COLLATE NOCASE;",
            ct: ct);

    public Task<Item?> FindAsync(long id, CancellationToken ct = default)
    {
        return FindCoreAsync(id, ct);
    }

    async Task<Item?> FindCoreAsync(long id, CancellationToken ct)
    {
        var rows = await _db
            .QueryAsync<Item>(
                "SELECT Id, Code, Description, Unit, CreatedAt FROM Items WHERE Id=? LIMIT 1;",
                parameters: new object?[] { id },
                ct: ct)
            .ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<long> UpsertAsync(Item draft, CancellationToken ct = default)
    {
        if (draft.Id == 0)
        {
            await _db
                .ExecuteAsync(
                    """
                    INSERT INTO Items (Code, Description, Unit, CreatedAt)
                    VALUES (?,?,?,?);
                    """,
                    parameters: new object?[] { draft.Code, draft.Description, draft.Unit, draft.CreatedAt },
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
                "UPDATE Items SET Code=?, Description=?, Unit=? WHERE Id=?;",
                parameters: new object?[] { draft.Code, draft.Description, draft.Unit, draft.Id },
                ct)
            .ConfigureAwait(false);
        return draft.Id;
    }

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        _db.ExecuteAsync("DELETE FROM Items WHERE Id=?;", new object?[] { id }, ct);

}
