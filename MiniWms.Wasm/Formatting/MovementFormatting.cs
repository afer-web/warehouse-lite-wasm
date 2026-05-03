using MiniWms.Wasm.Models;

namespace MiniWms.Wasm.Formatting;

public static class MovementFormatting
{
    public static string ToLabel(MovementType type) =>
        type switch
        {
            MovementType.Load => "Load",
            MovementType.Unload => "Unload",
            _ => type.ToString(),
        };
}
