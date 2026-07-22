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

// Allow large .bak uploads (LAN-only import); the default multipart limit is ~128 MB.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = long.MaxValue;
});

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

    // Add columns that EnsureCreated won't add to a pre-existing database: Attachments, and
    // the Uuid + Created/UpdatedAt stamps used for idempotent backup import. NEWID()/121-format
    // defaults backfill existing rows. ALTER and the timestamp backfill run in separate batches
    // so the new columns resolve.
    db.Database.ExecuteSqlRaw(@"
IF COL_LENGTH('tblTasks','Attachments') IS NULL ALTER TABLE tblTasks   ADD Attachments nvarchar(max) NOT NULL DEFAULT N'';
IF COL_LENGTH('tblBoards','Uuid')       IS NULL ALTER TABLE tblBoards  ADD Uuid uniqueidentifier NOT NULL DEFAULT NEWID();
IF COL_LENGTH('tblBoards','CreatedAt')  IS NULL ALTER TABLE tblBoards  ADD CreatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblBoards','UpdatedAt')  IS NULL ALTER TABLE tblBoards  ADD UpdatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblColumns','Uuid')      IS NULL ALTER TABLE tblColumns ADD Uuid uniqueidentifier NOT NULL DEFAULT NEWID();
IF COL_LENGTH('tblColumns','CreatedAt') IS NULL ALTER TABLE tblColumns ADD CreatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblColumns','UpdatedAt') IS NULL ALTER TABLE tblColumns ADD UpdatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblTasks','Uuid')        IS NULL ALTER TABLE tblTasks   ADD Uuid uniqueidentifier NOT NULL DEFAULT NEWID();
IF COL_LENGTH('tblTasks','CreatedAt')   IS NULL ALTER TABLE tblTasks   ADD CreatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblTasks','UpdatedAt')   IS NULL ALTER TABLE tblTasks   ADD UpdatedAt nvarchar(40) NOT NULL DEFAULT '';");

    db.Database.ExecuteSqlRaw(@"
UPDATE tblBoards  SET CreatedAt = CONVERT(nvarchar(40), SYSUTCDATETIME(), 121) WHERE CreatedAt = '';
UPDATE tblBoards  SET UpdatedAt = CreatedAt WHERE UpdatedAt = '';
UPDATE tblColumns SET CreatedAt = CONVERT(nvarchar(40), SYSUTCDATETIME(), 121) WHERE CreatedAt = '';
UPDATE tblColumns SET UpdatedAt = CreatedAt WHERE UpdatedAt = '';
UPDATE tblTasks   SET CreatedAt = CONVERT(nvarchar(40), SYSUTCDATETIME(), 121) WHERE CreatedAt = '';
UPDATE tblTasks   SET UpdatedAt = CreatedAt WHERE UpdatedAt = '';");

    app.Logger.LogInformation("Database ready (EnsureCreated + schema upgrade + stamp backfill).");
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
