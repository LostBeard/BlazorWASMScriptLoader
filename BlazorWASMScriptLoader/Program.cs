using BlazorWASMScriptLoader;
using MetadataReferenceService.BlazorWasm;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddSingleton<ScriptLoaderService>();

builder.Services.AddSingleton<BlazorWasmMetadataReferenceService>();

await builder.Build().RunAsync();
