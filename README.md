# Kanban Tasker

A personal kanban-board app in a clean **browser/server architecture**: a Blazor WebAssembly
web client on top of one ASP.NET Core REST API backed by SQL Server Express. The client talks
to the API over HTTP and never touches the database directly.

## Architecture

```
   Blazor WASM ────▶  ┌──────────────────────┐
   (browser)          │   Kanban.Api         │──▶  SQL Server Express
                      │   (ASP.NET Core,     │      (KanbanTasker DB,
                      │    REST, :5001)      │       + Import / Archive DBs)
                      └──────────────────────┘
```

### Projects (`src/`, managed by `Kanban.slnx`)

| Project | Role |
|---|---|
| **Kanban.Model** | Shared DTOs (`BoardDTO` / `ColumnDTO` / `TaskDTO`), the data-service interfaces, `RowOpResult`, `DatabaseUsage`, `IStamped`. |
| **Kanban.Services** | EF Core + ADO.NET against SQL Server: boards/columns/tasks, native `.bak` backup/import (upsert by Uuid), size monitoring, the archive-database strategy. |
| **Kanban.Api** | ASP.NET Core Web API. Controllers over the services; owns database provisioning + startup migration. CORS open for the web client. |
| **Kanban.Web** | Blazor WebAssembly web client — UI + `HttpClient`. No SQL/EF. |

Data-integrity logic (re-indexing a column after a task delete, renumbering column positions
after a column delete, cascading a column rename to its tasks) lives **on the server**, so the
client stays thin.

## Design language

A "liquid glass" look: frosted translucent surfaces via CSS `backdrop-filter`, a specular rim,
and a warm ambient rust/peach glow, with light and dark themes. Design tokens live in
`Kanban.Web/wwwroot/css/glass.css`. The app icons (`wwwroot/favicon.ico`, `wwwroot/applogo.png`)
are the browser-tab icon and the sidebar logo.

Responsive layouts: docked collapsible sidebar on desktop, an overlay drawer on tablet, and on
phones a **column tab bar** — tap a column to view it full-width, one at a time.

**Reorderable columns** — grab a column header and drag it to a new position. A brief
press-and-hold arms the drag: the column lifts once, follows the cursor, and settles into its new
slot on release, with the neighbours sliding to make room. The order is persisted (each column's
`Position`) so it survives a reload. Reordering uses [SortableJS](https://github.com/SortableJS/Sortable),
vendored locally under `wwwroot/lib/` (no CDN, works offline), because press-and-hold activation,
touch dragging, and the reorder animation are exactly what the native HTML5 drag events — which
Blazor supports and the cards already use — do not provide. Desktop and tablet only; the phone tab
layout does not reorder.

## Identity & backup

The database is **SQL Server Express** (`KanbanTasker`); the API creates/upgrades the schema on
startup. Every board/column/task carries a stable **`Uuid`** plus `CreatedAt`/`UpdatedAt` stamps.

- **Size monitoring** — data-file size vs. the Express per-database limit (50 GB on SQL Server
  2025 Express, 10 GB before), in Settings → Storage.
- **Archive strategy** — completed tasks can move to a separate `KanbanTaskerArchive` database.
- **Backup** — a full native `.bak` of the whole database, streamed to the browser as a download.
- **Import** — upload a `.bak`; it restores into a `KanbanTaskerImport` **staging** database and
  **upserts by `Uuid`**: re-importing the same backup changes nothing, missing rows are restored,
  and *overwrite* mode is a full restore that repairs even newer-but-corrupted rows. Nothing is
  ever deleted.

> **Backup/import are LAN-only.** A full `.bak` can be large, so both endpoints refuse any request
> whose Host is the public tunnel domain (403); use the server's LAN address. A deployment with a
> **least-privilege SQL login** must have the staging/archive databases pre-created and owned by
> that login: `KanbanTaskerImport` and `KanbanTaskerArchive`, each `ALTER AUTHORIZATION … TO <login>`.

Attachments are stored as **path references** (`MACHINE\path`, a file or a whole folder), never
uploaded — the database only records which machine and where a card's material lives.

## Building & running (development)

Requires the .NET 10 SDK.

```bash
# API (reads ConnectionStrings:KanbanDb from appsettings.json)
dotnet run --project src/Kanban.Api        # http://localhost:5000

# Web client (standalone dev server; ApiBaseUrl in wwwroot/appsettings.json,
# empty = same-origin when hosted by the API under /kanban)
dotnet run --project src/Kanban.Web
```

Connection strings live in gitignored `appsettings.json` files (only `appsettings.sample.json`
is committed) — no credentials in source control.

## Deployment (overview)

The web + API deploy behind a reverse proxy and a Cloudflare Tunnel, so everything sits under one
Zero-Trust-protected host with no new public entry point:

```
Internet ─▶ Cloudflare (Zero Trust Access) ─▶ cloudflared ─▶ nginx:80
                                                                ├─ /kanban/      → Blazor WASM (static)
                                                                └─ /kanban/api/  → Kanban API (:5001)
```

- The API runs self-contained (no .NET needed on the server) on a private port, kept alive by a
  watchdog + a startup scheduled task; the WASM is published as static files under `/kanban/`.
- Serving Kanban as a **path** under an already-protected host means it inherits the existing Zero
  Trust — no URL reaches it without authentication. Backup/import are additionally gated to the LAN.

Infra specifics (server address, domain, credentials) are kept out of the repo.
