namespace MiniWms.Wasm.Models;

/// <summary>Warehouse movement direction for ledger rows.</summary>
public enum MovementType
{
    /// <summary>Inbound / receipt.</summary>
    Load = 0,

    /// <summary>Outbound / issue.</summary>
    Unload = 1
}
