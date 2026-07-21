using System.Text.Json.Serialization;
using KanbanTasker.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from configuration so the same build targets different
// databases: appsettings.json = local test DB (dev), appsettings.Production.json =
// the server DB (192.168.5.2\SQLEXPRESS, user tasker). Switch with ASPNETCORE_ENVIRONMENT.
ServiceCollectionExtensions.ConnectionString =
    builder.Configuration.GetConnectionString("KanbanDb")
    ?? throw new InvalidOperationException("ConnectionStrings:KanbanDb is missing from configuration.");

builder.Services.AddKanbanServices();

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
        // Board <-> Task navigation properties form a cycle; drop the back-reference on serialize.
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddOpenApi();

// Permissive CORS for the WinUI client and the future Blazor WASM front-end.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Ensure the schema exists (moved here from the old WinUI startup): the API now owns
// database provisioning, and the thin client never touches the database directly.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KanbanTasker.Services.Database.Db>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw(
        "IF COL_LENGTH('tblTasks','Attachments') IS NULL " +
        "ALTER TABLE tblTasks ADD Attachments nvarchar(max) NOT NULL DEFAULT N''");
    app.Logger.LogInformation("Database ready (EnsureCreated + schema upgrade).");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Database initialization failed — check the connection string.");
}

app.MapOpenApi();
app.UseCors();
app.MapControllers();
app.MapGet("/", () => Results.Ok(new { service = "Kanban API", status = "running" }));

app.Run();
