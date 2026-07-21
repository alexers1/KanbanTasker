using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Kanban.Web;
using Kanban.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API base URL comes from wwwroot/appsettings.json (ApiBaseUrl) so the same build
// can point at the local dev API or the deployed one without recompiling.
var apiBase = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase) });
builder.Services.AddScoped<KanbanApiClient>();
builder.Services.AddScoped<ThemeService>();

await builder.Build().RunAsync();
