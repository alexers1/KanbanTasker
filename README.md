# Kanban Tasker

A personal kanban-board app in a clean **client/server architecture**: one ASP.NET Core
REST API backed by SQL Server Express, with **two front-ends that share the same backend
and the same "liquid glass" design language** —

- **Kanban.WinUI** — a native Windows 11 desktop thin client (WinUI 3).
- **Kanban.Web** — a Blazor WebAssembly web client.

Both talk to the API over HTTP; neither touches the database directly.

## Architecture

```
                    ┌──────────────────────┐
   WinUI 3 client ─▶│                      │
   (desktop, LAN)   │   Kanban.Api         │──▶  SQL Server Express
                    │   (ASP.NET Core,     │      (KanbanTasker DB,
   Blazor WASM ────▶│    REST, :5001)      │       + Import / Archive DBs)
   (browser, web)   └──────────────────────┘
```

### Projects (`src/`, managed by `Kanban.slnx`)

| Project | Role |
|---|---|
| **Kanban.Model** | Shared DTOs (`BoardDTO` / `ColumnDTO` / `TaskDTO`), async service interfaces, `RowOpResult`, `DatabaseUsage`. Referenced by every other project. |
| **Kanban.Services** | EF Core + ADO.NET implementations against SQL Server: boards/columns/tasks, native `.bak` backup & import, size monitoring, the archive-database strategy. |
| **Kanban.Api** | ASP.NET Core Web API. Controllers over the services; owns database provisioning (`EnsureCreated`). CORS open for the web client. |
| **Kanban.WinUI** | WinUI 3 desktop **thin client** — UI + `HttpClient`. No SQL/EF. |
| **Kanban.Web** | Blazor WebAssembly web client — UI + `HttpClient`. No SQL/EF. |

The data-integrity logic (re-indexing a column after a task is deleted, renumbering column
positions after a column delete, cascading a column rename to its tasks) lives **on the
server**, so both clients stay thin and consistent.

## Design language

A shared "liquid glass" look across both clients: frosted translucent surfaces, a specular
rim, and a warm ambient rust/peach glow, with light and dark themes.

- WinUI paints it with the compositor (DesktopAcrylic + Win2D) and theme dictionaries in `App.xaml`.
- The web reproduces it with CSS `backdrop-filter` + gradient rims; the design tokens in
  `Kanban.Web/wwwroot/css/glass.css` use the **same hex values** as the WinUI theme dictionaries.
- App icons are shared: the WinUI `Assets/AppIcon.ico` / `AppIcon.png` are synced into the
  web client at build time (favicon + sidebar logo), so there is a single source of truth.

## Data & backup

The database is **SQL Server Express** (`KanbanTasker`). The API creates the schema on startup.

- **Size monitoring** — data-file size vs. the Express per-database limit (50 GB on SQL Server
  2025 Express, 10 GB before), surfaced in each client's Settings → Storage.
- **Archive strategy** — completed tasks can be moved to a separate `KanbanTaskerArchive`
  database to keep the main database small.
- **Backup** — a full native `.bak` of the whole database (`BACKUP DATABASE … WITH INIT, FORMAT`).
- **Import** — restores a `.bak` into a `KanbanTaskerImport` **staging** database and then
  **merges its boards into the live database as new boards** (IDs remapped, nothing overwritten
  or deleted — import is additive/non-destructive).

> **Backup/import files are server-side.** The API and database are co-located, so a backup is
> written to, and an import reads from, a path **on the server**. The web client's Settings
> asks for a server path accordingly. (Downloading/uploading a `.bak` between a *remote* client
> and the server is not yet implemented.)
>
> A deployment that uses a **least-privilege SQL login** (not sysadmin/dbcreator) must have the
> staging/archive databases pre-created and owned by that login so it can restore/merge:
> `KanbanTaskerImport` and `KanbanTaskerArchive`, each `ALTER AUTHORIZATION … TO <login>`.

## Building & running (development)

Requires the .NET 10 SDK (and the Windows App SDK for the WinUI client).

```bash
# API (reads ConnectionStrings:KanbanDb from appsettings.json)
dotnet run --project src/Kanban.Api        # http://localhost:5000

# Web client (standalone dev server, points at ApiBaseUrl in wwwroot/appsettings.json)
dotnet run --project src/Kanban.Web

# Desktop client
dotnet run --project src/Kanban.WinUI
```

Connection strings and API addresses live in `appsettings.json` files that are **gitignored**
(only `appsettings.sample.json` is committed) — no credentials in source control.

## Clients

### WinUI desktop (thin client)
Framework-dependent, unpackaged publish with a Windows App Runtime pre-check; ships loose
files with .NET bundled, so the published folder is xcopy-portable. Settings → **Connection**
chooses how to reach the API:

- **LAN** — connect directly to the server by IP:port (no internet path). *Implemented.*
- **Default URL** — a public address (e.g. via a tunnel). *Stubbed for later.*

### Blazor web (`/kanban/`)
Published as static WebAssembly files and served under a `/kanban/` base path, calling the API
same-origin at `/kanban/api`. Collapsible sidebar and responsive layouts for desktop, tablet
(overlay drawer), and phone (full-bleed board, one column per swipe).

## Deployment (overview)

The web/API deploy behind a reverse proxy and a Cloudflare Tunnel so everything sits under one
Zero-Trust-protected host, with no new public entry point:

```
Internet ─▶ Cloudflare (Zero Trust Access) ─▶ tunnel ─▶ cloudflared ─▶ nginx:80
                                                                          ├─ /kanban/      → Blazor WASM (static)
                                                                          └─ /kanban/api/  → Kanban API (:5001)
```

- The API runs self-contained (no .NET needed on the server) on a private port, kept alive by
  a watchdog + a startup scheduled task.
- Serving Kanban as a **path** under an already-protected host means it inherits the existing
  Zero Trust — there is no URL that reaches it without going through authentication.
- On the LAN, the desktop client connects straight to the API's IP:port, bypassing the tunnel.

Infra specifics (server address, domain, credentials) are kept out of the repo.
