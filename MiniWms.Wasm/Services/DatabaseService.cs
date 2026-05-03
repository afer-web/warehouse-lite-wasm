using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace MiniWms.Wasm.Services;

/// <summary>Thin async façade over sqlite WASM (sql.js), exposed globally as miniWmsDb.</summary>
public sealed class DatabaseService
{
    internal static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
        };

    readonly IJSRuntime _js;

    public DatabaseService(IJSRuntime js) => _js = js;

    /// <summary>Loads sqlite wasm file, restores OPFS/IndexedDB snapshots, installs minimal triggers.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _js.InvokeVoidAsync("miniWmsDb.ensureReady").AsTask();

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        IReadOnlyList<object?>? parameters = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var binds = JsonSerializer.Serialize(ToBindArray(parameters));
        var json = await _js.InvokeAsync<string>("miniWmsDb.queryJson", sql, binds).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, SerializerOptions)?.ToArray() ?? Array.Empty<T>();
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"JSON mapping failed ({typeof(T).Name}): {ex}");
            return Array.Empty<T>();
        }
    }

    public async Task<long> ExecuteAsync(
        string sql,
        IReadOnlyList<object?>? parameters = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var binds = JsonSerializer.Serialize(ToBindArray(parameters));
        var result = await _js.InvokeAsync<RunResult>("miniWmsDb.run", sql, binds).ConfigureAwait(false);
        return result?.Changes ?? 0;
    }

    public async Task RunInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await _js.InvokeVoidAsync("miniWmsDb.begin").AsTask().ConfigureAwait(false);
        try
        {
            await work(ct).ConfigureAwait(false);
            await _js.InvokeVoidAsync("miniWmsDb.commit").AsTask().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            await _js.InvokeVoidAsync("miniWmsDb.rollback").AsTask().ConfigureAwait(false);
            throw;
        }
    }

    public Task PersistAsync(CancellationToken ct = default) =>
        _js.InvokeVoidAsync("miniWmsDb.persist").AsTask();

    static object?[] ToBindArray(IReadOnlyList<object?>? parameters) =>
        parameters is null ? Array.Empty<object?>() : parameters.Select(Normalize).ToArray();

    static object? Normalize(object? value) =>
        value switch
        {
            DateTimeOffset dto => dto.ToString("O"),
            DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O"),
            Enum e => Convert.ToInt32(e),
            _ => value,
        };

    sealed record RunResult(long Changes);
}
