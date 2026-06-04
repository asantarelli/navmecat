# NavMeCat

A lightweight, Navicat-style database manager for **SQL Server**, built with **C# / WPF (.NET 9)**.

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

- **Connection manager** — add, edit, and remove SQL Server connections.
  - Windows Authentication or SQL Server login.
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
  - **Save changes** writes everything back via `SqlDataAdapter` (requires a primary key).
  - **Filter** and multi-column **Sort** builders (Navicat-style) per tab.
  - **Cell detail panel** — view/edit the full content of the selected cell in a scrollable
    box below the grid (handy for long HTML/text). Rows are capped to a single line in the
    grid so long values don't blow up row height.
- **Clarion date & time support** — automatically detects integer columns that hold
  [Clarion Standard Dates/Times](#clarion-dates--times) and displays them as real dates (📅)
  and times (🕒), while keeping them editable. Toggle per tab.
- **Professional UI** — clean blue/slate theme, dark sidebar, styled modal dialogs.

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

> In-place editing requires the table to have a **primary key** so updates and deletes can be
> generated automatically.

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
