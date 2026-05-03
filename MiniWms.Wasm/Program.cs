using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MiniWms.Wasm.Services;

namespace MiniWms.Wasm;

/// <summary>
/// MiniWMS Blazor WebAssembly bootstrap. All persistence is handled in-browser (SQLite WASM + OPFS/IndexedDB).
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        // Single browser session shares one WASM SQLite connection backed by exported DB snapshots.
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<ItemService>();
        builder.Services.AddSingleton<LocationService>();
        builder.Services.AddSingleton<MovementService>();
        builder.Services.AddSingleton<InventoryService>();

        await builder.Build().RunAsync();
    }
}
