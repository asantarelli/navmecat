# NavMeCat

A lightweight, Navicat-style database manager for **SQL Server** and **SQLite**, built with
**C# / WPF (.NET 9)**.

Add connection strings, browse the server tree (databases → schemas → tables), open a table, and
view & edit its records in place — including adding and deleting rows — with changes pushed back to
the database.

![Status](https://img.shields.io/badge/status-v1-blue) ![Platform](https://img.shields.io/badge/platform-Windows-informational) ![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)

## Download

Grab the latest **portable `.exe`** from the
[**Releases**](https://github.com/robertorenz/navmecat/releases/latest) page.

- Single self-contained file — **no .NET install required**.
- Just download and double-click `NavMeCat.exe`.
- First launch is a couple of seconds slower (the compressed bundle self-extracts to a temp cache);
  later launches are fast.

## Features

- **Multiple database engines** — pick the engine when creating a connection. Each connection is
  tagged with an engine icon in the tree so you can tell them apart at a glance.
  - **SQL Server** and **SQLite** are fully supported today; **PostgreSQL** and **MongoDB** are
    selectable now and being wired up next.
  - **SQLite**: just point to a `.db` / `.sqlite` file — browse tables & views, view and edit rows
    (primary-key or rowid-safe), and inspect structure/DDL.
- **Connection manager** — add, edit, and remove connections.
  - SQL Server: Windows Authentication or SQL Server login.
  - Field-based builder *or* a raw connection string.
  - **Test Connection** before saving.
  - Connections are saved to `%AppData%\NavMeCat\connections.json`; passwords are
    encrypted at rest with **Windows DPAPI** (current-user scope).
- **Object tree** — lazily loads databases, schemas, and tables, color-coded by type.
- **Tabbed data view** — double-click a table to open it in its own tab (configurable row
  limit). Re-opening a table just switches to its existing tab. Tabs flag unsaved changes
  and can be closed individually.
  - **In-place editing** of cells.
  - **Add** new rows and **delete** rows.
  - **Save changes** writes everything back. Tables **without a primary key** are supported too —
    pick a **row identity** (the columns that identify a row) for safe updates and deletes.
  - **Filter** and multi-column **Sort** builders (Navicat-style) per tab.
  - **Spreadsheet-friendly copy & paste** — select rows/cells and **Ctrl+C** (or right-click →
    Copy / Copy with headers); pastes cleanly into Excel/Sheets with each value in its own cell.
    **Ctrl+V** pastes a block back into the grid from the top-left of the selection (adding rows
    past the end). A single clipboard value, or typing into one cell, fills **all selected
    cells**. Clarion date/time text is parsed back to the stored integer.
  - **Cell View panel** — a resizable panel below the grid that shows the full content of the
    selected cell, with a **View** drop button to render it as **Text** (editable), **Hex**,
    **Image**, or **Web** (HTML via WebView2). **Auto-detect** picks the best mode. Rows are
    capped to a single line in the grid so long values don't blow up row height.
- **Clarion date & time support** — automatically detects integer columns that hold
  [Clarion Standard Dates/Times](#clarion-dates--times) and displays them as real dates (📅)
  and times (🕒), while keeping them editable. Toggle per tab.
- **Object tree (Navicat-style)** — Server → Database → Schema → **Tables / Views / Functions /
  Procedures** folders, with a **filter/locator** box to jump to a table by name. Open tables or
  views to browse rows.
- **Structure inspector** — a dockable/pinnable side panel showing **Info**, **DDL**
  (`CREATE TABLE` + indexes + foreign keys), and **Relationships**.
- **SQL query window** — **New Query** opens a window to run arbitrary SQL (Run / F5) with a
  results grid, row counts and timing.
- **Graphical query builder** — compose SELECTs visually (tables, columns, joins with FK
  auto-detect, filters, sort) with live SQL.
- **Export** — export the current grid to **CSV, TSV, JSON, XML, HTML or Excel (.xlsx)**, with
  column selection.
- **Import data** — load a **CSV or Excel (.xlsx)** file into a table, with first-row-header
  detection and a source-to-column mapping that auto-maps by name. The whole import runs in a
  single transaction (all-or-nothing).
- **Generate INSERT script** — right-click a table to produce a ready-to-run `INSERT` script for
  its rows (wrapped in `SET IDENTITY_INSERT` when needed), viewable, copyable and savable as `.sql`.
- **Table designer** — create and alter tables: columns, types, nullability, defaults, primary
  key and indexes, with a copyable generated script.
- **Edit routines & views** — open and edit **functions, stored procedures and views**; create
  new ones from templates; execute or drop them.
- **Safe drop** — dropping a table/view/routine first checks `sys.sql_expression_dependencies`
  and warns you about objects that reference it.
- **Localization** — the interface is fully translatable; ships with **English and Spanish**.
  Switch language in **Settings** and the UI updates live (no restart).
- **Settings** — defaults for row limit, which panels open with a table, and the UI language.
- **Adaptive toolbar** — the per-table command bar is responsive: as the window narrows, button
  labels collapse to icons (with tooltips), and any buttons that still don't fit move into a
  **»** overflow menu — so the toolbar stays usable at any size.
- **Professional UI** — clean blue/slate theme, dark sidebar, button icons, styled modal dialogs.

## Clarion dates & times

Many Clarion-prepared tables store dates and times as integers:

- **Date** — the **number of days since December 28, 1800** (so `4` = 1801‑01‑01).
- **Time** — the **number of centiseconds since midnight, plus one** (so `1` = 00:00:00,
  `8,640,001` = 24:00:00).

NavMeCat detects these columns heuristically and shows them as `yyyy-MM-dd` dates (📅) and
`HH:mm:ss` times (🕒) on the column header. Dates are detected by name and/or value range;
times are detected mainly by name (their value range overlaps too much ordinary data to
trust values alone). Editing a converted cell accepts a normal date/time and writes the
correct integer back to the database.

Detection recognizes English and Spanish name hints (e.g. `FECHA` → date, `HORA` → time).
Use the **Clarion dates/times** checkbox in a table's toolbar to toggle all conversions on/off,
or **right-click any column header** to force it to **date**, **time**, or **plain number**, or
back to **auto-detect**. Empty values (stored as `0`) display as blank.

## Requirements

- Windows
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (to build) or the .NET 9 Desktop Runtime (to run)
- Network access to a SQL Server instance

## Getting started

```powershell
# Build
dotnet build

# Run
dotnet run --project NavMeCat.csproj
```

Then:

1. Click **New Connection**, fill in your server (e.g. `localhost\SQLEXPRESS`), choose
   authentication, and **Test** it.
2. Expand the connection in the tree to browse **databases → schemas → tables**.
3. **Double-click a table** to load its records.
4. Edit cells directly, add or delete rows, then click **Save changes**.

> In-place editing uses the table's **primary key** to generate updates and deletes. For tables
> without one, use the **Row identity…** picker in the toolbar to choose the identifying columns.

## Tech stack

| Concern            | Choice                                |
|--------------------|---------------------------------------|
| UI                 | WPF (.NET 9), MVVM (CommunityToolkit) |
| SQL Server access  | `Microsoft.Data.SqlClient`            |
| Editable grid      | `DataTable` + `SqlDataAdapter` + `SqlCommandBuilder` |
| Password storage   | DPAPI (`System.Security.Cryptography.ProtectedData`) |

## Project layout

```
Models/      ConnectionProfile
Services/    SqlServerService, EditableTableSession, ConnectionStore
ViewModels/  MainViewModel, DbTreeNode
Views/       ConnectionDialog, ModalDialog, Dialogs
Converters/  Tree icon / color converters
Themes/      Theme.xaml (palette + control styles)
```

## Roadmap ideas

- Ad-hoc SQL query editor with results grid
- Filtering / sorting / paging on large tables
- Export (CSV / JSON), and view table structure (columns, indexes, keys)
- Support for other engines (PostgreSQL, MySQL)
