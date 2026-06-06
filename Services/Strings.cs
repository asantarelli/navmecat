namespace NavMeCat.Services;

/// <summary>Translation tables. English is the fallback; Spanish is the first added language.</summary>
public static class Strings
{
    public static string Get(string key, string lang)
    {
        if (lang == "es" && Es.TryGetValue(key, out var es)) return es;
        return En.TryGetValue(key, out var en) ? en : key;
    }

    public static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        // Menu — File
        ["Menu_File"] = "_File",
        ["Menu_NewConnection"] = "New Connection",
        ["Menu_NewQuery"] = "New Query",
        ["Menu_QueryBuilder"] = "Query Builder",
        ["Menu_ExportTable"] = "Export current table…",
        ["Menu_Settings"] = "Settings…",
        ["Menu_Exit"] = "Exit",
        // Menu — Connection
        ["Menu_Connection"] = "_Connection",
        ["Menu_AddConnection"] = "Add Connection",
        ["Menu_EditConnection"] = "Edit Connection",
        ["Menu_RemoveConnection"] = "Remove Connection",
        ["Menu_Refresh"] = "Refresh",
        // Menu — View
        ["Menu_View"] = "_View",
        ["Menu_StructurePanel"] = "Structure panel",
        ["Menu_SqlPanel"] = "SQL preview panel",
        ["Menu_CellDetailPanel"] = "Cell detail panel",
        // Menu — Help
        ["Menu_Help"] = "_Help",
        ["Menu_Documentation"] = "Documentation",
        ["Menu_GitHub"] = "GitHub Repository",
        ["Menu_LatestRelease"] = "Latest Release",
        ["Menu_About"] = "About NavMeCat",

        // Sidebar
        ["Sidebar_Connections"] = "CONNECTIONS",
        ["Filter_TablesPlaceholder"] = "Filter tables…",
        ["Filter_TablesTooltip"] = "Type to filter tables (and schemas/databases) by name",
        ["Btn_Add"] = "Add",
        ["Btn_Edit"] = "Edit",
        ["Btn_Remove"] = "Remove",
        ["Btn_Refresh"] = "Refresh",

        // Per-table toolbar
        ["Toolbar_Unsaved"] = "● Unsaved changes",
        ["Btn_Filter"] = "Filter",
        ["Btn_Sort"] = "Sort",
        ["Btn_RowIdentity"] = "Row identity…",
        ["Tip_RowIdentity"] = "No primary key — choose which columns identify a row for edits/deletes",
        ["Btn_View"] = "View  ▾",
        ["Tip_View"] = "View the selected cell's content as Text, Hex, Image or Web",
        ["Btn_SQL"] = "SQL",
        ["Tip_SQL"] = "Show the SQL that Save will run for pending changes",
        ["Btn_Structure"] = "Structure",
        ["Tip_Structure"] = "Show the table structure: Info, DDL and Relationships",
        ["Lbl_RowLimit"] = "Row limit",
        ["Btn_Export"] = "Export",
        ["Tip_Export"] = "Export the current rows to CSV, JSON, XML, HTML or Excel",
        ["Btn_Reload"] = "Reload",
        ["Btn_SaveChanges"] = "Save changes",

        // Filter popup
        ["Filter_Title"] = "Filter",
        ["Filter_MatchAll"] = "Match all (AND)",
        ["Filter_MatchAny"] = "Match any (OR)",
        ["Filter_AddCondition"] = "＋ Add condition",
        // Sort popup
        ["Sort_Title"] = "Sort",
        ["Sort_AddLevel"] = "＋ Add level",
        // View popup
        ["View_Auto"] = "Auto-detect",
        ["View_Text"] = "Text",
        ["View_Hex"] = "Hex",
        ["View_Image"] = "Image",
        ["View_Web"] = "Web",
        ["View_HidePanel"] = "Hide panel",

        // Common buttons
        ["Btn_Clear"] = "Clear",
        ["Btn_Apply"] = "Apply",
        ["Btn_Cancel"] = "Cancel",
        ["Btn_Save"] = "Save",
        ["Btn_Browse"] = "Browse…",

        // Empty state
        ["Empty_NoTable"] = "No table open",
        ["Empty_Hint"] = "Double-click a table in the tree to open it in a new tab.",

        // Settings dialog
        ["Settings_Title"] = "Settings",
        ["Settings_WhenOpening"] = "When opening a table",
        ["Settings_DefaultRowLimit"] = "Default row limit",
        ["Settings_OpenStructure"] = "Open the Structure panel by default",
        ["Settings_DefaultSection"] = "Default section",
        ["Settings_OpenSql"] = "Open the SQL preview panel by default",
        ["Settings_OpenDetail"] = "Open the Cell detail panel by default",
        ["Settings_ConvertClarion"] = "Convert Clarion date / time / timestamp columns by default",
        ["Settings_ApplyNote"] = "Changes apply to tables opened from now on.",
        ["Settings_Language"] = "Language",
        ["Settings_Interface"] = "Interface",

        // Context menu
        ["Ctx_Open"] = "Open",
        ["Ctx_Design"] = "Design",
        ["Ctx_GenerateInsert"] = "Generate INSERT script…",
        ["Ctx_ImportData"] = "Import data…",
        ["Ctx_Drop"] = "Drop…",
        ["Ctx_Edit"] = "Edit",
        ["Ctx_Execute"] = "Execute…",
        ["Ctx_Refresh"] = "Refresh",
        ["Ctx_NewTable"] = "New Table…",
        ["Ctx_NewView"] = "New View…",
        ["Ctx_NewFunction"] = "New Function…",
        ["Ctx_NewProcedure"] = "New Procedure…",

        // Import dialog
        ["Import_Title"] = "Import data",
        ["Import_ChooseFile"] = "Choose a CSV or Excel (.xlsx) file…",
        ["Import_FirstRowHeader"] = "First row contains column names",
        ["Import_MapColumns"] = "Map source columns to table columns",
        ["Import_Skip"] = "(skip)",
        ["Import_Importing"] = "Importing…",
        ["Import_Button"] = "Import",
    };

    public static readonly Dictionary<string, string> Es = new(StringComparer.Ordinal)
    {
        // Menú — Archivo
        ["Menu_File"] = "_Archivo",
        ["Menu_NewConnection"] = "Nueva conexión",
        ["Menu_NewQuery"] = "Nueva consulta",
        ["Menu_QueryBuilder"] = "Generador de consultas",
        ["Menu_ExportTable"] = "Exportar tabla actual…",
        ["Menu_Settings"] = "Configuración…",
        ["Menu_Exit"] = "Salir",
        // Menú — Conexión
        ["Menu_Connection"] = "_Conexión",
        ["Menu_AddConnection"] = "Agregar conexión",
        ["Menu_EditConnection"] = "Editar conexión",
        ["Menu_RemoveConnection"] = "Quitar conexión",
        ["Menu_Refresh"] = "Actualizar",
        // Menú — Ver
        ["Menu_View"] = "_Ver",
        ["Menu_StructurePanel"] = "Panel de estructura",
        ["Menu_SqlPanel"] = "Panel de vista previa SQL",
        ["Menu_CellDetailPanel"] = "Panel de detalle de celda",
        // Menú — Ayuda
        ["Menu_Help"] = "A_yuda",
        ["Menu_Documentation"] = "Documentación",
        ["Menu_GitHub"] = "Repositorio de GitHub",
        ["Menu_LatestRelease"] = "Última versión",
        ["Menu_About"] = "Acerca de NavMeCat",

        // Barra lateral
        ["Sidebar_Connections"] = "CONEXIONES",
        ["Filter_TablesPlaceholder"] = "Filtrar tablas…",
        ["Filter_TablesTooltip"] = "Escriba para filtrar tablas (y esquemas/bases de datos) por nombre",
        ["Btn_Add"] = "Agregar",
        ["Btn_Edit"] = "Editar",
        ["Btn_Remove"] = "Quitar",
        ["Btn_Refresh"] = "Actualizar",

        // Barra de herramientas de la tabla
        ["Toolbar_Unsaved"] = "● Cambios sin guardar",
        ["Btn_Filter"] = "Filtrar",
        ["Btn_Sort"] = "Ordenar",
        ["Btn_RowIdentity"] = "Identidad de fila…",
        ["Tip_RowIdentity"] = "Sin clave principal — elija qué columnas identifican una fila para editar/eliminar",
        ["Btn_View"] = "Ver  ▾",
        ["Tip_View"] = "Ver el contenido de la celda seleccionada como Texto, Hex, Imagen o Web",
        ["Btn_SQL"] = "SQL",
        ["Tip_SQL"] = "Mostrar el SQL que ejecutará Guardar para los cambios pendientes",
        ["Btn_Structure"] = "Estructura",
        ["Tip_Structure"] = "Mostrar la estructura de la tabla: Info, DDL y Relaciones",
        ["Lbl_RowLimit"] = "Límite de filas",
        ["Btn_Export"] = "Exportar",
        ["Tip_Export"] = "Exportar las filas actuales a CSV, JSON, XML, HTML o Excel",
        ["Btn_Reload"] = "Recargar",
        ["Btn_SaveChanges"] = "Guardar cambios",

        // Ventana de filtro
        ["Filter_Title"] = "Filtro",
        ["Filter_MatchAll"] = "Coincidir todo (Y)",
        ["Filter_MatchAny"] = "Coincidir cualquiera (O)",
        ["Filter_AddCondition"] = "＋ Agregar condición",
        // Ventana de orden
        ["Sort_Title"] = "Ordenar",
        ["Sort_AddLevel"] = "＋ Agregar nivel",
        // Ventana de vista
        ["View_Auto"] = "Detección automática",
        ["View_Text"] = "Texto",
        ["View_Hex"] = "Hex",
        ["View_Image"] = "Imagen",
        ["View_Web"] = "Web",
        ["View_HidePanel"] = "Ocultar panel",

        // Botones comunes
        ["Btn_Clear"] = "Limpiar",
        ["Btn_Apply"] = "Aplicar",
        ["Btn_Cancel"] = "Cancelar",
        ["Btn_Save"] = "Guardar",
        ["Btn_Browse"] = "Examinar…",

        // Estado vacío
        ["Empty_NoTable"] = "Ninguna tabla abierta",
        ["Empty_Hint"] = "Haga doble clic en una tabla del árbol para abrirla en una nueva pestaña.",

        // Configuración
        ["Settings_Title"] = "Configuración",
        ["Settings_WhenOpening"] = "Al abrir una tabla",
        ["Settings_DefaultRowLimit"] = "Límite de filas predeterminado",
        ["Settings_OpenStructure"] = "Abrir el panel de estructura de forma predeterminada",
        ["Settings_DefaultSection"] = "Sección predeterminada",
        ["Settings_OpenSql"] = "Abrir el panel de vista previa SQL de forma predeterminada",
        ["Settings_OpenDetail"] = "Abrir el panel de detalle de celda de forma predeterminada",
        ["Settings_ConvertClarion"] = "Convertir columnas de fecha/hora/marca de tiempo Clarion de forma predeterminada",
        ["Settings_ApplyNote"] = "Los cambios se aplican a las tablas abiertas a partir de ahora.",
        ["Settings_Language"] = "Idioma",
        ["Settings_Interface"] = "Interfaz",

        // Menú contextual
        ["Ctx_Open"] = "Abrir",
        ["Ctx_Design"] = "Diseñar",
        ["Ctx_GenerateInsert"] = "Generar script INSERT…",
        ["Ctx_ImportData"] = "Importar datos…",
        ["Ctx_Drop"] = "Eliminar…",
        ["Ctx_Edit"] = "Editar",
        ["Ctx_Execute"] = "Ejecutar…",
        ["Ctx_Refresh"] = "Actualizar",
        ["Ctx_NewTable"] = "Nueva tabla…",
        ["Ctx_NewView"] = "Nueva vista…",
        ["Ctx_NewFunction"] = "Nueva función…",
        ["Ctx_NewProcedure"] = "Nuevo procedimiento…",

        // Importar
        ["Import_Title"] = "Importar datos",
        ["Import_ChooseFile"] = "Elija un archivo CSV o Excel (.xlsx)…",
        ["Import_FirstRowHeader"] = "La primera fila contiene los nombres de columna",
        ["Import_MapColumns"] = "Asignar columnas de origen a columnas de la tabla",
        ["Import_Skip"] = "(omitir)",
        ["Import_Importing"] = "Importando…",
        ["Import_Button"] = "Importar",
    };
}
