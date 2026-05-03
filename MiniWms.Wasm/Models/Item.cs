namespace MiniWms.Wasm.Models;

/// <summary>SKU / article master row.</summary>
public sealed record Item(long Id, string Code, string Description, string Unit, string CreatedAt);
