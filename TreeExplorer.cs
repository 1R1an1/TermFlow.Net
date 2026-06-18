using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleUtils
{
    public enum ExplorerFilter
    {
        All,
        OnlyFolders,
        OnlyFiles
    }

    public static class TreeExplorer
    {
        private const int ReservedRows = 7;

        /// <summary>
        /// Explora el sistema de archivos para seleccionar una ÚNICA opción (Carpeta o Archivo según el filtro).
        /// Retorna la ruta absoluta seleccionada o null si cancela.
        /// </summary>
        public static async Task<string> ExploreOneAsync(string title, string rootDir, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                return await InternalExploreAsync(title, rootDir, isMulti: false, filter, token).ContinueWith(t => t.Result.FirstOrDefault(), token);
            }
            finally
            {
                Engine.ExitFullScreen();
            }
        }

        /// <summary>
        /// Explora el sistema de archivos permitiendo SELECCIÓN MÚLTIPLE con herencia jerárquica.
        /// Retorna las rutas absolutas resueltas según el filtro aplicado.
        /// </summary>
        public static async Task<string[]> ExploreMultiAsync(string title, string rootDir, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                return await InternalExploreAsync(title, rootDir, isMulti: true, filter, token);
            }
            finally
            {
                Engine.ExitFullScreen();
            }
        }

        private static async Task<string[]> InternalExploreAsync(string title, string rootDir, bool isMulti, ExplorerFilter filter, CancellationToken token)
        {
            string currentDir = Path.GetFullPath(rootDir);
            int cursor = 0;
            StringBuilder buffer = new StringBuilder(4096);

            ScrollState layout = new ScrollState();
            bool shouldRender = true;

            // Infraestructura para herencia jerárquica de selección
            HashSet<string> marked = new HashSet<string>();
            HashSet<string> unmarkedExceptions = new HashSet<string>();

            List<string> entries = FetchAndSortEntries(currentDir);

            while (!token.IsCancellationRequested)
            {
                if (layout.Update(cursor, entries.Count, ReservedRows))
                {
                    shouldRender = true;
                    Console.Write("\x1b[2J");
                }
                cursor = layout.Cursor;

                if (shouldRender)
                {
                    RenderTree(buffer, title, currentDir, entries, layout.Cursor, layout.Scroll, layout.VisibleRows, isMulti, filter, marked, unmarkedExceptions);
                    shouldRender = false;
                }

                var inputEvent = InputReader.ReadInput();
                if (inputEvent.Type != InputEventType.None)
                {
                    shouldRender = true;

                    if (inputEvent.Type == InputEventType.Key)
                    {
                        var key = inputEvent.KeyInfo;

                        if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q' || key.KeyChar == 'Q')
                            return Array.Empty<string>();

                        // Confirmación en Modo Múltiple
                        if (isMulti && (key.KeyChar == 'c' || key.KeyChar == 'C'))
                        {
                            return ResolveMarkedEntries(marked, unmarkedExceptions, filter, rootDir);
                        }

                        // Navegación Vertical
                        if (key.Key == ConsoleKey.DownArrow || key.KeyChar == 'j' || key.KeyChar == 'J')
                        {
                            if (entries.Count > 0 && cursor < entries.Count - 1) cursor++;
                        }
                        else if (key.Key == ConsoleKey.UpArrow || key.KeyChar == 'k' || key.KeyChar == 'K')
                        {
                            if (cursor > 0) cursor--;
                        }
                        // Navegación Horizontal / Apertura o Selección Única
                        else if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.RightArrow || key.KeyChar == 'l' || key.KeyChar == 'L')
                        {
                            if (entries.Count > 0)
                            {
                                string selectedPath = entries[cursor];
                                bool isDir = Directory.Exists(selectedPath);

                                if (isDir && key.Key != ConsoleKey.Spacebar)
                                {
                                    // Si es directorio, Enter/l/FlechaDerecha entran a explorar
                                    currentDir = selectedPath;
                                    entries = FetchAndSortEntries(currentDir);
                                    cursor = 0;
                                }
                                else if (!isMulti)
                                {
                                    // En modo de selección única, si es un archivo que cumple el filtro, se selecciona
                                    if (!isDir && filter != ExplorerFilter.OnlyFolders) return [selectedPath];
                                }
                            }
                        }
                        else if (key.Key == ConsoleKey.LeftArrow || key.KeyChar == 'h' || key.KeyChar == 'H')
                        {
                            DirectoryInfo parent = Directory.GetParent(currentDir);
                            if (parent != null)
                            {
                                currentDir = parent.FullName;
                                entries = FetchAndSortEntries(currentDir);
                                cursor = 0;
                            }
                        }
                        // Gestión de Marcado (Espacio)
                        else if (key.Key == ConsoleKey.Spacebar && entries.Count > 0)
                        {
                            string targetPath = entries[cursor];
                            bool isDir = Directory.Exists(targetPath);

                            if (!isMulti)
                            {
                                // Selección única de Carpetas usando Espacio
                                if (isDir && filter != ExplorerFilter.OnlyFiles) return [targetPath];
                                if (!isDir && filter != ExplorerFilter.OnlyFolders) return [targetPath];
                            }
                            else
                            {
                                // Control de restricciones de filtro en Selección Múltiple
                                if (filter == ExplorerFilter.OnlyFolders && !isDir) continue;
                                if (filter == ExplorerFilter.OnlyFiles && isDir) continue;

                                ToggleSelection(targetPath, marked, unmarkedExceptions);
                            }
                        }
                    }
                    else if (inputEvent.Type == InputEventType.ScrollUp)
                    {
                        if (cursor > 0) cursor--;
                    }
                    else if (inputEvent.Type == InputEventType.ScrollDown)
                    {
                        if (entries.Count > 0 && cursor < entries.Count - 1) cursor++;
                    }
                }

                await Task.Delay(15, token);
            }

            return Array.Empty<string>();
        }

        private static List<string> FetchAndSortEntries(string dir)
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(dir))
                {
                    // Mezclado y ordenamiento alfabético puro sin importar si es archivo o carpeta
                    var items = Directory.GetFileSystemEntries(dir)
                                         .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
                    list.AddRange(items);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            return list;
        }

        private static bool IsPathMarked(string path, HashSet<string> marked, HashSet<string> unmarkedExceptions)
        {
            string current = path;
            while (current != null)
            {
                if (unmarkedExceptions.Contains(current)) return false;
                if (marked.Contains(current)) return true;
                current = Path.GetDirectoryName(current);
            }
            return false;
        }

        private static void ToggleSelection(string path, HashSet<string> marked, HashSet<string> unmarkedExceptions)
        {
            bool currentlyMarked = IsPathMarked(path, marked, unmarkedExceptions);
            string subPathPrefix = path + Path.DirectorySeparatorChar;

            if (currentlyMarked)
            {
                // Si ya está marcado y el usuario quiere desmarcarlo
                if (marked.Contains(path)) marked.Remove(path);
                else unmarkedExceptions.Add(path);

                // Limpiar estados redundantes hijos
                marked.RemoveWhere(p => p.StartsWith(subPathPrefix));
                unmarkedExceptions.RemoveWhere(p => p.StartsWith(subPathPrefix));
            }
            else
            {
                // Si está desmarcado y el usuario quiere marcarlo
                if (unmarkedExceptions.Contains(path)) unmarkedExceptions.Remove(path);
                else marked.Add(path);

                // Limpiar estados redundantes hijos
                marked.RemoveWhere(p => p.StartsWith(subPathPrefix));
                unmarkedExceptions.RemoveWhere(p => p.StartsWith(subPathPrefix));
            }
        }

        private static string[] ResolveMarkedEntries(HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter, string rootDir)
        {
            var resolved = new List<string>();

            foreach (var path in marked)
            {
                if (File.Exists(path))
                {
                    if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(path, marked, unmarkedExceptions))
                        resolved.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    TraverseAndResolve(path, filter, marked, unmarkedExceptions, resolved);
                }
            }

            return resolved.Distinct().OrderBy(p => p).ToArray();
        }

        private static void TraverseAndResolve(string dir, ExplorerFilter filter, HashSet<string> marked, HashSet<string> unmarkedExceptions, List<string> resolved)
        {
            if (!IsPathMarked(dir, marked, unmarkedExceptions)) return;

            if (filter != ExplorerFilter.OnlyFiles) resolved.Add(dir);

            try
            {
                if (filter != ExplorerFilter.OnlyFolders)
                {
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        if (IsPathMarked(file, marked, unmarkedExceptions)) resolved.Add(file);
                    }
                }

                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    TraverseAndResolve(subDir, filter, marked, unmarkedExceptions, resolved);
                }
            }
            catch { }
        }

        private static void RenderTree(StringBuilder buffer, string title, string currentDir, List<string> entries, int cursor, int scroll, int visibleRows, bool isMulti, ExplorerFilter filter, HashSet<string> marked, HashSet<string> unmarkedExceptions)
        {
            var theme = Engine.Theme;
            buffer.Clear().Append("\x1b[H");

            // Cabeceras
            buffer.Append($"  {theme.Primary}{theme.Bold}{title}{theme.Reset}\x1b[K\n");
            buffer.Append($"  {theme.Dim}{new string(theme.BorderHorizontal, Math.Max(20, title.Length))}{theme.Reset}\x1b[K\n");
            buffer.Append($"  Ruta: {theme.Dim}{currentDir}{theme.Reset}\x1b[K\n");

            int end = Math.Min(entries.Count, scroll + visibleRows);

            if (scroll > 0) buffer.Append($"  {theme.Dim}↑ ({scroll} más arriba){theme.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            if (entries.Count == 0)
            {
                buffer.Append($"    {theme.Dim}(Carpeta vacía o sin accesos){theme.Reset}\x1b[K\n");
                for (int i = 1; i < visibleRows; i++) buffer.Append("\x1b[K\n");
            }
            else
            {
                for (int i = scroll; i < end; i++)
                {
                    string fullPath = entries[i];
                    bool isDir = Directory.Exists(fullPath);
                    string name = Path.GetFileName(fullPath);
                    string displayName = isDir ? $"{name}/" : name;

                    // Evaluar el checkbox basándose en el sistema jerárquico
                    string checkPrefix = "";
                    if (isMulti)
                    {
                        // Evaluamos con simetría si este elemento debe o no mostrar checkbox según el filtro
                        bool showCheckbox = true;
                        if (filter == ExplorerFilter.OnlyFolders && !isDir) showCheckbox = false;
                        if (filter == ExplorerFilter.OnlyFiles && isDir) showCheckbox = false;

                        if (showCheckbox)
                        {
                            bool isChecked = IsPathMarked(fullPath, marked, unmarkedExceptions);
                            checkPrefix = isChecked ? $"{theme.Success}{theme.Checked}{theme.Reset} "
                                                    : $"{theme.Dim}{theme.Unchecked}{theme.Reset} ";
                        }
                        else
                        {
                            // Medimos la longitud real del texto del checkbox de tu tema (+ 1 espacio de separación)
                            // y generamos la cantidad exacta de espacios en blanco para una alineación matemática perfecta.
                            int visualWidth = (theme.Unchecked ?? "[ ]").Length + 1;
                            checkPrefix = new string(' ', visualWidth);
                        }
                    }

                    string itemColor = isDir ? $"{theme.Primary}" : "";

                    if (i == cursor)
                    {
                        buffer.Append($"  {theme.Primary}{theme.Indicator}{theme.Reset} {checkPrefix}{theme.Bold}{itemColor}{displayName}{theme.Reset}\x1b[K\n");
                    }
                    else
                    {
                        string normalStyle = isDir ? $"{theme.Primary}" : $"{theme.Dim}";
                        buffer.Append($"    {checkPrefix}{normalStyle}{displayName}{theme.Reset}\x1b[K\n");
                    }
                }

                for (int i = (end - scroll); i < visibleRows; i++) buffer.Append("\x1b[K\n");
            }

            int remaining = entries.Count - end;
            if (remaining > 0) buffer.Append($"  {theme.Dim}↓ ({remaining} más abajo){theme.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            // Barra de instrucciones dinámica y contextual
            buffer.Append("  ");
            if (isMulti)
            {
                buffer.Append($"{theme.Warning}Space{theme.Reset} marcar  {theme.Warning}h/j/k/l/←→{theme.Reset} navegar  {theme.Warning}c{theme.Reset} confirmar  {theme.Warning}Esc/q{theme.Reset} salir");
            }
            else
            {
                string helpKey = filter == ExplorerFilter.OnlyFiles ? "Enter" : "Space/Enter";
                buffer.Append($"{theme.Warning}{helpKey}{theme.Reset} elegir  {theme.Warning}h/j/k/l/←→{theme.Reset} navegar  {theme.Warning}Esc/q{theme.Reset} salir");
            }
            buffer.Append("\x1b[K\n");
            buffer.Append("\x1b[K");

            Console.Write(buffer.ToString());
        }
    }
}