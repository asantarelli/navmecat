# NavMeCat

A lightweight, Navicat-style database manager for **SQL Server**, built with **C# / WPF (.NET 9)**.

Add connection strings, browse the server tree (databases → schemas → tables), open a table, and
view & edit its records in place — including adding and deleting rows — with changes pushed back to
the database.

![Status](https://img.shields.io/badge/status-v1-blue) ![Platform](https://img.shields.io/badge/platform-Windows-informational) ![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)

## Features

- **Connection manager** — add, edit, and remove SQL Server connections.
  - Windows Authentication or SQL Server login.
  - Field-based builder *or* a raw connection string.
  - **Test Connection** before saving.
  - Connections are saved to `%AppData%\NavMeCat\connections.json`; passwords are
    encrypted at rest with **Windows DPAPI** (current-user scope).
- **Object tree** — lazily loads databases, schemas, and tables, color-coded by type.
- **Data grid** — double-click a table to load its rows (configurable row limit).
  - **In-place editing** of cells.
  - **Add** new rows and **delete** rows.
  - **Save changes** writes everything back via `SqlDataAdapter` (requires a primary key).
- **Professional UI** — clean blue/slate theme, dark sidebar, styled modal dialogs.

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
