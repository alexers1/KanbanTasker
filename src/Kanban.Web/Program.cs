using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Kanban.Web;
using Kanban.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same-origin by default: the API hosts this app under /kanban, so HostEnvironment.BaseAddress
// (from <base href="/kanban/">) already points at the API. A non-empty ApiBaseUrl in
// wwwroot/appsettings.json overrides it (e.g. a standalone dev build against a separate API).
var apiBase = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBase))
    apiBase = builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase, UriKind.Absolute) });
builder.Services.AddScoped<KanbanApiClient>();
builder.Services.AddScoped<ThemeService>();

await builder.Build().RunAsync();
