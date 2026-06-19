# Changelog

All notable changes to NavMeCat are documented here.

---

## v1.59.0 — 2026-06-19

### Added
- **SQL Beautifier** (`Ctrl+Shift+F`) — custom tokenizer, no external dependencies. Works on selected text or the entire editor. Available in Query Window and Routine Editor.
- **Query History** — last 50 queries per connection, persisted to AppData. Popup dropdown: single-click loads, double-click runs.
- **Multiple resultsets** — `TabControl` replaces the single DataGrid; each `SELECT` in a batch gets its own tab.
- **Schema Diff** — compare two databases on the same connection; expandable UI showing missing/differing tables and columns.
- **ER Diagram** — WebView2 canvas with force-directed layout, drag nodes, pan, zoom, and Bézier FK arrows.
- **Find in editor** (`Ctrl+F`) — AvalonEdit `SearchPanel` with a fully custom themed template; dropdown for Match case / Whole words / Regex; `◄ ►` navigation; `✕` to close.
- **Multi-schema autocomplete** — `dbo.` lists tables in that schema; `dbo.Table.` lists columns; columns from all referenced schemas are suggested in scope.
- **Session memory** — last active database per connection is saved and restored automatically on next launch.
- **Export button** in the Query Window toolbar.
- **App icon** wiring (`Assets/AppIcon.ico`, `csproj`, `App.xaml.cs`).

### Fixed
- **Dark theme — Menu bar** invisible (black text on dark background) → full `MenuItem`/`ContextMenu`/`Separator` template override.
- **Dark theme — SearchPanel** buttons showed empty borders (AvalonEdit paths used `SystemColors.ControlTextBrush`) → replaced with Unicode icons (`◄ ► ✕ ▾`) via custom `ControlTemplate`.
- `SystemColors` overrides added to dark theme so any WPF control using system colors renders correctly.
- `AppSettings.Clone()` was a shallow copy, causing the `LastDatabases` dictionary to be shared between instances → now deep-copies the dictionary.
- `SearchPanel.MarkerBrush` applied via XAML Style threw `NullReferenceException` (panel not yet attached to `TextArea`) → moved to code-behind, set after `Install()`.

---

## v1.58.2 — 2026-06-18

### Fixed
- Opening Oracle tables with out-of-range `DATE` values no longer crashes.

---

## v1.58.1

### Fixed
- "Copy table failed" (empty schema) when pasting TPS/DAT into SQL Server.

---

## v1.58.0

### Added
- Full Oracle editing: row edits, `DROP TABLE`, and copy as paste target.

---

## v1.57.0

### Added
- Oracle connections (read-only).

---

## v1.56.1

### Fixed
- Grid jumping to the end when changing a column "Show as" mode.

---

## v1.56.0

### Changed
- Structure panel now shows **Indexes** instead of Relationships.

---

## v1.55.4

### Fixed
- SQLite `DROP TABLE` failing with a `FOREIGN KEY` constraint error.

---

## v1.55.3

### Fixed
- Refresh both the tree and Objects list after pasting a table.

---

## v1.55.2

### Changed
- Hide **Paste** for read-only engines in the Objects list.

---

## v1.55.1

### Changed
- Unified "Drop" wording; hide Drop for read-only engines in the Objects list.

---

## v1.55.0

### Added
- Tree table context menu matches the Objects list.

### Fixed
- Slow SQLite drop confirmation.

---

## v1.54.3

### Added
- Credits & licenses in the User Guide; external links open in browser.

---

## v1.54.2

### Added
- FAQ section in the User Guide (English & Spanish).

---

## v1.54.1

### Added
- `F1` opens the User Guide.

---

## v1.54.0

### Added
- Built-in User Guide (English & Spanish) in the Help menu.

---

## v1.53.2

### Added
- Column-mapping dialog: SQL type dropdown + size field.

---

## v1.53.1

### Fixed
- Convert Clarion `LONG` date/time to real SQL date/time on copy.

---

## v1.53.0

### Added
- Read classic Clarion `.DAT` files.
- Editable TPS/DAT → SQL type mapping.

---

## v1.52.1

### Added
- TPS: list `.tps` files in the Objects tab on the connection.

---

## v1.52.0

### Added
- Read Clarion TPS files (folder connection).
- Copy TPS → SQL Server/SQLite.

---

## v1.51.1

### Added
- Export complete: **Open file** / **Open folder** buttons.

---

## v1.51.0

### Added
- Export: filter scope choice + JSON formatting options.

---

## v1.50.0

### Added
- Export formats: DBF, TXT, XLS (legacy Excel), and SQL (`INSERT` statements).

---

## v1.49.1

### Fixed
- User manager: SQL Server principal load (bit cast error).

---

## v1.49.0

### Added
- User & role manager: logins, roles, privileges.

---

## v1.48.1

### Fixed
- MySQL/MariaDB: don't pin to the default database for server-level operations.

---

## v1.48.0

### Added
- MySQL and MariaDB support.

---

## v1.47.0 – v1.47.3

### Added / Changed
- Colored command-bar icons; iterative icon polish.

---

## v1.46.1

### Fixed
- No crash on `SELECT *` cross joins in query results.

---

## v1.46.0

### Added
- Visual query designer works with SQLite and Firebird.
- Command-bar button for Query Builder.

---

## v1.45.0

### Added
- Query window works with SQLite (and Firebird).

---

## v1.44.0

### Added
- Navicat-style command bar.

---

## v1.43.0 – v1.43.1

### Added
- Copy/paste tables from the Objects list.
- Right-click context menu on the Objects list.

---

## v1.42.0 – v1.42.4

### Added
- Multi-cell live fill while typing.

### Added
- Info tab: object header, owner, collation, size.

---

## v1.39.0 – v1.41.0

### Added
- Objects tab (persistent, replaces overlay).
- Object list on database and schema nodes.
- Richer Info tab (Navicat-style).
- Tab tooltips showing table origin.

---

## v1.37.0 – v1.38.0

### Added
- SQL Server tree: skip database level when a default DB is set.
- Object list on database & schema nodes.

---

## v1.35.0 – v1.36.0

### Added
- Cross-engine table copy.
- Navicat-style object list.
- Stronger drop warning.

---

## v1.32.0 – v1.34.0

### Added
- Copy & paste a table (same connection).
- Paste into a different connection (same engine).
- `SqlBulkCopy` for cross-connection SQL Server paste.

---

## v1.31.0

### Added
- MongoDB connection + document viewer.

---

## v1.29.0 – v1.30.0

### Added
- Firebird connections.
- Firebird embedded (no-server) mode.

---

## v1.27.0 – v1.28.0

### Added
- Multi-engine connections + SQLite support.
- Table designer for SQLite.

---

## v1.25.0 – v1.26.1

### Added
- Data import/export tools.
- Safe drop.
- Spanish localization.
- Adaptive overflow command bar.

---

## v1.0.0

### Added
- Initial WPF SQL Server database manager.
- Table browser with tabs, editable grid, clipboard copy/paste.
- Clarion date/time/timestamp detection.
- SQL query window.
- Graphical query builder.
- Structure inspector, SQL preview, cell detail pane.
- Tree filter/locator.
- Settings screen.
- Menu bar with keyboard shortcuts.
- Export (CSV, TSV, JSON, XML, HTML, XLSX).
- Stored procedure / function / view editor.
- Table designer.
