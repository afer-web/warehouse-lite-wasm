namespace MiniWms.Wasm.Models;

/// <summary>Warehouse bin / location master row.</summary>
public sealed record Location(long Id, string Code, string Area, string Description);
