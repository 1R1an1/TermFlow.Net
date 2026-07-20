/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen.TreeExplorer
{
    /// <summary>
    /// Navegador de árbol full-screen con soporte para selección única y múltiple.
    /// Funciona contra cualquier <see cref="IExplorerDataSource"/> (físico o virtual)
    /// y ofrece presets para carpetas reales o rutas virtuales in-memory.
    /// </summary>
    public static partial class TreeExplorer
    {
        private const int ReservedRows = 8;

        #region Lógica universal de selección (estática, agnóstica al origen)

        /// <summary>
        /// Determina si una ruta está marcada considerando herencia de directorios padres
        /// y excepciones explícitas (unmark).
        /// </summary>
        /// <param name="path">Ruta a evaluar.</param>
        /// <param name="marked">Conjunto de rutas marcadas explícitamente.</param>
        /// <param name="unmarkedExceptions">Conjunto de rutas excluidas de la herencia.</param>
        /// <param name="source">Origen de datos para resolver padres.</param>
        /// <returns><c>true</c> si la ruta queda efectivamente marcada tras aplicar herencia y excepciones.</returns>
        private static bool IsPathMarked(string path, HashSet<string> marked, HashSet<string> unmarkedExceptions, IExplorerDataSource source)
        {
            string current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if (unmarkedExceptions.Contains(current)) return false;
                if (marked.Contains(current)) return true;
                current = source.GetParent(current);
            }
            return marked.Contains(source.RootPath);
        }

        /// <summary>
        /// Alterna el estado de marca de una ruta, propagando la operación a sus subrutas
        /// y limpiando marcas/excepciones redundantes debajo de ella.
        /// </summary>
        /// <param name="path">Ruta cuyo estado de marca se alternará.</param>
        /// <param name="marked">Conjunto de rutas marcadas (se modifica).</param>
        /// <param name="unmarkedExceptions">Conjunto de excepciones (se modifica).</param>
        /// <param name="source">Origen de datos para resolver subpaths.</param>
        private static void ToggleSelection(string path, HashSet<string> marked, HashSet<string> unmarkedExceptions, IExplorerDataSource source)
        {
            bool currentlyMarked = IsPathMarked(path, marked, unmarkedExceptions, source);
            string prefix = source.GetSubPathPrefix(path);

            if (currentlyMarked)
            {
                if (marked.Contains(path)) marked.Remove(path);
                else unmarkedExceptions.Add(path);
            }
            else
            {
                if (unmarkedExceptions.Contains(path)) unmarkedExceptions.Remove(path);
                else marked.Add(path);
            }

            marked.RemoveWhere(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            unmarkedExceptions.RemoveWhere(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Resuelve la lista final de archivos/carpetas marcados recorriendo recursivamente
        /// cada ruta marcada y aplicando el filtro indicado. Implementación genérica usada
        /// como fallback si el origen de datos no provee una versión optimizada.
        /// </summary>
        /// <param name="source">Origen de datos a consultar.</param>
        /// <param name="marked">Rutas marcadas explícitamente.</param>
        /// <param name="unmarkedExceptions">Excepciones de unmark.</param>
        /// <param name="filter">Filtro de tipo de entrada a incluir.</param>
        /// <returns>Array de rutas resueltas, sin duplicados y ordenado alfabéticamente.</returns>
        private static string[] ResolveMarkedEntriesUniversal(IExplorerDataSource source, HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter)
        {
            var resolved = new List<string>();
            foreach (var path in marked)
            {
                if (!source.IsDirectory(path))
                {
                    if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(path, marked, unmarkedExceptions, source))
                        resolved.Add(path);
                }
                else
                    TraverseUniversal(path, source, filter, marked, unmarkedExceptions, resolved);
            }
            return resolved.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToArray();
        }

        /// <summary>
        /// Recursión universal que desciende por las carpetas marcadas recolectando las
        /// entradas que cumplen con el filtro.
        /// </summary>
        /// <param name="dir">Carpeta a recorrer.</param>
        /// <param name="source">Origen de datos.</param>
        /// <param name="filter">Filtro a aplicar.</param>
        /// <param name="marked">Rutas marcadas.</param>
        /// <param name="unmarkedExceptions">Excepciones.</param>
        /// <param name="resolved">Lista acumuladora de rutas resueltas.</param>
        private static void TraverseUniversal(string dir, IExplorerDataSource source, ExplorerFilter filter,
            HashSet<string> marked, HashSet<string> unmarkedExceptions, List<string> resolved)
        {
            if (!IsPathMarked(dir, marked, unmarkedExceptions, source)) return;
            if (filter != ExplorerFilter.OnlyFiles) resolved.Add(dir);

            var children = source.FetchAndSortEntries(dir);
            foreach (var child in children)
            {
                if (!child.IsDirectory)
                {
                    if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(child.Id, marked, unmarkedExceptions, source))
                        resolved.Add(child.Id);
                }
                else
                {
                    TraverseUniversal(child.Id, source, filter, marked, unmarkedExceptions, resolved);
                }
            }
        }

        #endregion

        #region API pública

        /// <summary>
        /// Atajo para explorar un directorio físico con selección única.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="rootDir">Ruta física raíz a explorar.</param>
        /// <param name="filter">Filtro de entradas a mostrar.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>Ruta elegida o <see cref="string.Empty"/> si se cancela.</returns>
        public static async Task<string> ExploreOneAsync(string title, string rootDir, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
            => await ExploreOneAsync(title, dataSource: new PhysicalDataSource(rootDir), filter, token: token);

        /// <summary>
        /// Atajo para explorar un directorio físico con selección múltiple.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="rootDir">Ruta física raíz a explorar.</param>
        /// <param name="filter">Filtro de entradas a mostrar.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>Array de rutas marcadas o vacío si se cancela.</returns>
        public static async Task<string[]> ExploreMultiAsync(string title, string rootDir, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
            => await ExploreMultiAsync(title, dataSource: new PhysicalDataSource(rootDir), filter, token: token);

        /// <summary>
        /// Atajo para explorar un conjunto de rutas virtuales con selección única.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="virtualPaths">Enumerables de rutas virtuales estilo Unix ("a/b/c").</param>
        /// <param name="virtualRoot">Nombre a usar como nodo raíz virtual.</param>
        /// <param name="filter">Filtro de entradas a mostrar.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>Ruta virtual elegida o <see cref="string.Empty"/> si se cancela.</returns>
        public static async Task<string> ExploreOneAsync(string title, IEnumerable<string> virtualPaths, string virtualRoot = "Root", ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
            => await ExploreOneAsync(title, dataSource: new VirtualDataSource(virtualPaths, virtualRoot), filter, token: token);

        /// <summary>
        /// Atajo para explorar un conjunto de rutas virtuales con selección múltiple.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="virtualPaths">Enumerables de rutas virtuales estilo Unix ("a/b/c").</param>
        /// <param name="virtualRoot">Nombre a usar como nodo raíz virtual.</param>
        /// <param name="filter">Filtro de entradas a mostrar.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>Array de rutas virtuales marcadas o vacío si se cancela.</returns>
        public static async Task<string[]> ExploreMultiAsync(string title, IEnumerable<string> virtualPaths, string virtualRoot = "Root", ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
            => await ExploreMultiAsync(title, dataSource: new VirtualDataSource(virtualPaths, virtualRoot), filter, token: token);

        /// <summary>
        /// Exploración de selección única contra un <see cref="IExplorerDataSource"/> arbitrario.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="dataSource">Origen de datos a explorar.</param>
        /// <param name="filter">Filtro de entradas a mostrar.</param>
        /// <param name="initialPath">Subruta inicial opcional dentro de <paramref name="dataSource"/>.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>Ruta elegida o <see cref="string.Empty"/> si se cancela.</returns>
        public static async Task<string> ExploreOneAsync(string title, IExplorerDataSource dataSource, ExplorerFilter filter = ExplorerFilter.All, string initialPath = null, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                var result = await InternalExploreAsync(title, dataSource, isMulti: false, filter, initialPath, token);
                return result.FirstOrDefault() ?? string.Empty;
            }
            catch (OperationCanceledException) { return string.Empty; }
            finally { Engine.ExitFullScreen(); }
        }

        /// <summary>
        /// Exploración de selección múltiple contra un <see cref="IExplorerDataSource"/> arbitrario.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="dataSource">Origen de datos a explorar.</param>
        /// <param name="filter">Filtro de entradas a mostrar.</param>
        /// <param name="initialPath">Subruta inicial opcional dentro de <paramref name="dataSource"/>.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>Array de rutas marcadas o vacío si se cancela.</returns>
        public static async Task<string[]> ExploreMultiAsync(string title, IExplorerDataSource dataSource, ExplorerFilter filter = ExplorerFilter.All, string initialPath = null, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                return await InternalExploreAsync(title, dataSource, isMulti: true, filter, initialPath, token);
            }
            catch (OperationCanceledException) { return Array.Empty<string>(); }
            finally { Engine.ExitFullScreen(); }
        }

        #endregion

        #region Motor central

        /// <summary>
        /// Obtiene las entradas hijas de un nodo, usando la variante asíncrona del origen
        /// si está disponible, o la síncrona en caso contrario.
        /// </summary>
        /// <param name="dataSource">Origen de datos.</param>
        /// <param name="nodeId">ID del nodo padre.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>Lista de entradas hijas ordenadas.</returns>
        private static async Task<List<ExplorerEntry>> FetchEntriesAsync(IExplorerDataSource dataSource, string nodeId, CancellationToken token)
        {
            if (dataSource is IAsyncExplorerDataSource asyncSource)
                return await asyncSource.FetchAndSortEntriesAsync(nodeId, token);
            // Fuentes síncronas (disco/virtual) son inmediatas, no necesitan Task.Run
            return dataSource.FetchAndSortEntries(nodeId);
        }

        /// <summary>
        /// Bucle central de la exploración. Maneja navegación, scroll, marcas (en modo multi)
        /// y entrada de teclado hasta que el usuario confirma o cancela.
        /// </summary>
        /// <param name="title">Título a mostrar.</param>
        /// <param name="dataSource">Origen de datos a explorar.</param>
        /// <param name="isMulti"><c>true</c> para selección múltiple con checkboxes.</param>
        /// <param name="filter">Filtro de tipo de entrada a permitir seleccionar.</param>
        /// <param name="initialPath">Subruta inicial opcional.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>Array de rutas seleccionadas (vacío si se cancela).</returns>
        private static async Task<string[]> InternalExploreAsync(string title, IExplorerDataSource dataSource, bool isMulti, ExplorerFilter filter, string initialPath, CancellationToken token)
        {
            // Si mandas una ruta inicial arranca ahí, si no, usa la raíz del origen de datos
            string currentNode = !string.IsNullOrEmpty(initialPath) ? Path.Combine(dataSource.RootPath, initialPath) : dataSource.RootPath;
            int cursor = 0;
            StringBuilder buffer = new StringBuilder(4096);
            ScrollState layout = new ScrollState();
            bool shouldRender = true;

            HashSet<string> marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> unmarkedExceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<ExplorerEntry> entries = await FetchEntriesAsync(dataSource, currentNode, token);

            bool exit = false;
            string[] result = Array.Empty<string>();
            bool nodeChanged = false; // Control de estado para carga asíncrona

            var router = new InputRouter()
                .BindCancel(() => { result = Array.Empty<string>(); exit = true; })
                .BindNavigate(
                    () => { if (cursor > 0) cursor--; },
                    () => { if (entries.Count > 0 && cursor < entries.Count - 1) cursor++; }
                )
                .BindScroll(
                    () => { if (cursor > 0) cursor--; },
                    () => { if (entries.Count > 0 && cursor < entries.Count - 1) cursor++; }
                );


            router.Bind("l/→/Enter", isMulti ? "entrar" : "entrar/elegir", () =>
            {
                if (entries.Count == 0) return;
                ExplorerEntry selected = entries[cursor];
                if (selected.IsDirectory)
                {
                    currentNode = selected.Id;
                    nodeChanged = true;
                    cursor = 0;
                }
                else if (!isMulti && filter != ExplorerFilter.OnlyFolders)
                {
                    result = [selected.Id];
                    exit = true;
                }
            }, ConsoleKey.L, ConsoleKey.RightArrow, ConsoleKey.Enter);


            router.Bind("h/←", "volver", () =>
            {
                string parent = dataSource.GetParent(currentNode);
                if (!string.IsNullOrEmpty(parent))
                    currentNode = parent; nodeChanged = true; cursor = 0;
            }, ConsoleKey.H, ConsoleKey.LeftArrow);

            if (isMulti)
            {
                router.BindSelect(() =>
                {
                    if (entries.Count == 0) return;
                    var target = entries[cursor];
                    if (filter == ExplorerFilter.OnlyFolders && !target.IsDirectory) return;
                    if (filter == ExplorerFilter.OnlyFiles && target.IsDirectory) return;
                    ToggleSelection(target.Id, marked, unmarkedExceptions, dataSource);
                });

                router.Bind("c", "Confirmar", () =>
                {
                    var optimized = dataSource.ResolveMarkedEntries(marked, unmarkedExceptions, filter);
                    result = optimized ?? ResolveMarkedEntriesUniversal(dataSource, marked, unmarkedExceptions, filter);
                    exit = true;
                }, ConsoleKey.C);
            }
            else
            {
                router.BindSelect(() =>
                {
                    if (entries.Count == 0) return;
                    ExplorerEntry target = entries[cursor];
                    if (target.IsDirectory && filter != ExplorerFilter.OnlyFiles) { result = new[] { target.Id }; exit = true; }
                    if (!target.IsDirectory && filter != ExplorerFilter.OnlyFolders) { result = new[] { target.Id }; exit = true; }
                }, "elegir");
            }

            while (!token.IsCancellationRequested && !exit)
            {
                if (layout.Update(cursor, entries.Count, ReservedRows))
                {
                    shouldRender = true;
                    Console.Write("\x1b[2J");
                }
                cursor = layout.Cursor;

                if (shouldRender)
                {
                    RenderTree(buffer, title, currentNode, entries, layout.Cursor, layout.Scroll, layout.VisibleRows, isMulti, filter, marked, unmarkedExceptions, dataSource, router);
                    shouldRender = false;
                }

                var inputEvent = InputReader.ReadInput();
                if (inputEvent.Type != InputEventType.None)
                {
                    shouldRender = true;
                    router.Handle(inputEvent);

                    if (nodeChanged)
                    {
                        entries = await FetchEntriesAsync(dataSource, currentNode, token);
                        nodeChanged = false;
                    }
                }
                await Task.Delay(15, token);
            }

            return result;
        }

        /// <summary>
        /// Dibuja el explorador completo: cabecera, ruta actual, ítems con checkbox (en modo multi),
        /// indicadores de scroll y footer contextual con los atajos.
        /// </summary>
        /// <param name="buffer">StringBuilder reutilizable.</param>
        /// <param name="title">Título a mostrar.</param>
        /// <param name="currentDir">Ruta del nodo actualmente abierto.</param>
        /// <param name="entries">Entradas visibles del nodo actual.</param>
        /// <param name="cursor">Índice del cursor actual.</param>
        /// <param name="scroll">Índice del primer ítem visible.</param>
        /// <param name="visibleRows">Cantidad máxima de filas visibles.</param>
        /// <param name="isMulti">Indica modo selección múltiple (activa checkboxes).</param>
        /// <param name="filter">Filtro activo (aforda qué entradas muestran checkbox).</param>
        /// <param name="marked">Conjunto de rutas marcadas.</param>
        /// <param name="unmarkedExceptions">Conjunto de excepciones de unmark.</param>
        /// <param name="dataSource">Origen de datos para resolver herencia de marcas.</param>
        /// <param name="router">Enrutador que renderiza el footer.</param>
        private static void RenderTree(StringBuilder buffer, string title, string currentDir, List<ExplorerEntry> entries,
            int cursor, int scroll, int visibleRows, bool isMulti, ExplorerFilter filter,
            HashSet<string> marked, HashSet<string> unmarkedExceptions, IExplorerDataSource dataSource, InputRouter router)
        {
            buffer.Clear().Append("\x1b[H");

            buffer.Append("\x1b[K\n");
            buffer.Append($"  {title}\x1b[K\n");
            buffer.Append($"  {ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, title.GetVisualLength())}{ThemeColors.Reset}\x1b[K\n");
            buffer.Append($"  Ruta: {ThemeColors.Dim}{currentDir}{ThemeColors.Reset}\x1b[K\n");

            int end = Math.Min(entries.Count, scroll + visibleRows);

            if (scroll > 0) buffer.Append($"  {ThemeColors.Dim}↑ ({scroll} más arriba){ThemeColors.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            if (entries.Count == 0)
            {
                buffer.Append($"    {ThemeColors.Dim}(Carpeta vacía o sin accesos){ThemeColors.Reset}\x1b[K\n");
                for (int i = 1; i < visibleRows; i++) buffer.Append("\x1b[K\n");
            }
            else
            {
                for (int i = scroll; i < end; i++)
                {
                    ExplorerEntry entry = entries[i];
                    string displayName = entry.IsDirectory ? $"{entry.Name}/" : entry.Name;

                    string checkPrefix = "";
                    if (isMulti)
                    {
                        bool showCheckbox = true;
                        if (filter == ExplorerFilter.OnlyFolders && !entry.IsDirectory) showCheckbox = false;
                        if (filter == ExplorerFilter.OnlyFiles && entry.IsDirectory) showCheckbox = false;

                        if (showCheckbox)
                        {
                            bool isChecked = IsPathMarked(entry.Id, marked, unmarkedExceptions, dataSource);
                            checkPrefix = isChecked ? $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} "
                                                    : $"{ThemeColors.Dim}{ConsoleGlyphs.Unchecked}{ThemeColors.Reset} ";
                        }
                        else
                        {
                            int visualWidth = (ConsoleGlyphs.Unchecked ?? "[ ]").Length + 1;
                            checkPrefix = new string(' ', visualWidth);
                        }
                    }

                    string itemColor = entry.IsDirectory ? ThemeColors.Selector + AnsiColor.Bold : ThemeColors.Selector;

                    if (i == cursor)
                    {
                        buffer.Append($"  {ThemeColors.Selector}{ConsoleGlyphs.Indicator}{ThemeColors.Reset} {checkPrefix}{itemColor}{displayName}{ThemeColors.Reset}\x1b[K\n");
                    }
                    else
                    {
                        string normalStyle = entry.IsDirectory ? AnsiColor.White + AnsiColor.Bold : $"{ThemeColors.Dim}";
                        buffer.Append($"    {checkPrefix}{normalStyle}{displayName}{ThemeColors.Reset}\x1b[K\n");
                    }
                }

                for (int i = end - scroll; i < visibleRows; i++) buffer.Append("\x1b[K\n");
            }

            int remaining = entries.Count - end;
            if (remaining > 0) buffer.Append($"  {ThemeColors.Dim}↓ ({remaining} más abajo){ThemeColors.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            router.RenderFooter(buffer);
            buffer.Append("\x1b[K\n");
            buffer.Append("\x1b[K");

            Console.Write(buffer.ToString());
        }

        #endregion
    }
}
