using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    public enum ExplorerFilter
    {
        All,
        OnlyFolders,
        OnlyFiles
    }

    /// <summary>
    /// Estructura de rendimiento empaquetada en memoria (0 allocations en Heap por elemento).
    /// </summary>
    public readonly struct ExplorerEntry
    {
        public string Id { get; }
        public string Name { get; }
        public bool IsDirectory { get; }

        public ExplorerEntry(string id, string name, bool isDirectory)
        {
            Id = id;
            Name = name;
            IsDirectory = isDirectory;
        }
    }

    public static class TreeExplorer
    {
        private const int ReservedRows = 8;

        #region Abstracción de Origen de Datos

        private interface IExplorerDataSource
        {
            string RootPath { get; }
            bool IsDirectory(string id);
            string GetParent(string id);
            string GetSubPathPrefix(string id);
            List<ExplorerEntry> FetchAndSortEntries(string id);
            string[] ResolveMarkedEntries(HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter);
        }

        // Proveedor Físico (Optimizado para un solo viaje al disco por navegación)
        private sealed class PhysicalDataSource : IExplorerDataSource
        {
            public string RootPath { get; }
            public PhysicalDataSource(string rootDir) => RootPath = Path.GetFullPath(rootDir);

            public bool IsDirectory(string id) => Directory.Exists(id);
            public string GetParent(string id) => Directory.GetParent(id)?.FullName ?? string.Empty;

            public string GetSubPathPrefix(string id) =>
                id.EndsWith(Path.DirectorySeparatorChar) ? id : id + Path.DirectorySeparatorChar;

            public List<ExplorerEntry> FetchAndSortEntries(string id)
            {
                var list = new List<ExplorerEntry>();
                try
                {
                    var di = new DirectoryInfo(id);
                    if (di.Exists)
                    {
                        // GetFileSystemInfos recupera metadatos de archivos y carpetas en UNA SOLA llamada al SO
                        var infos = di.GetFileSystemInfos()
                                      .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);

                        foreach (var info in infos)
                        {
                            bool isDir = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                            list.Add(new ExplorerEntry(info.FullName, info.Name, isDir));
                        }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
                return list;
            }

            public string[] ResolveMarkedEntries(HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter)
            {
                var resolved = new List<string>();
                foreach (var path in marked)
                {
                    if (File.Exists(path))
                    {
                        if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(path, marked, unmarkedExceptions, this))
                            resolved.Add(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        TraverseAndResolve(path, filter, marked, unmarkedExceptions, resolved);
                    }
                }
                return resolved.Distinct().OrderBy(p => p).ToArray();
            }

            private void TraverseAndResolve(string dir, ExplorerFilter filter, HashSet<string> marked, HashSet<string> unmarkedExceptions, List<string> resolved)
            {
                if (!IsPathMarked(dir, marked, unmarkedExceptions, this)) return;
                if (filter != ExplorerFilter.OnlyFiles) resolved.Add(dir);

                try
                {
                    if (filter != ExplorerFilter.OnlyFolders)
                    {
                        foreach (var file in Directory.GetFiles(dir))
                        {
                            if (IsPathMarked(file, marked, unmarkedExceptions, this)) resolved.Add(file);
                        }
                    }
                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        TraverseAndResolve(subDir, filter, marked, unmarkedExceptions, resolved);
                    }
                }
                catch { }
            }
        }

        // Proveedor Virtual (Construcción O(N) en instanciación, O(1) en navegación)
        private sealed class VirtualDataSource : IExplorerDataSource
        {
            private readonly Dictionary<string, List<ExplorerEntry>> _hierarchy = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _parents = new(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _globalDirs = new(StringComparer.OrdinalIgnoreCase);

            public string RootPath { get; }

            public VirtualDataSource(IEnumerable<string> paths, string virtualRoot)
            {
                // Normalización robusta contra raíces vacías o "/"
                string cleanRoot = virtualRoot.Replace('\\', '/').Trim('/');
                RootPath = string.IsNullOrEmpty(cleanRoot) ? "/" : "/" + cleanRoot;

                _globalDirs.Add(RootPath);

                var structuralHierarchy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                var rawFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var rawPath in paths)
                {
                    if (string.IsNullOrWhiteSpace(rawPath)) continue;

                    string cleanPath = rawPath.Replace('\\', '/').Trim('/');
                    if (string.IsNullOrEmpty(cleanPath)) continue;

                    string[] segments = cleanPath.Split('/');
                    string current = RootPath;

                    for (int i = 0; i < segments.Length; i++)
                    {
                        string segment = segments[i];
                        string next = current == "/" ? $"/{segment}" : $"{current}/{segment}";

                        bool isLast = (i == segments.Length - 1);
                        bool isExplicitDir = isLast && (rawPath.EndsWith('/') || rawPath.EndsWith('\\'));

                        if (!isLast || isExplicitDir) _globalDirs.Add(next);
                        else rawFiles.Add(next);

                        if (!structuralHierarchy.TryGetValue(current, out var children))
                        {
                            children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            structuralHierarchy[current] = children;
                        }
                        children.Add(next);

                        _parents[next] = current;
                        current = next;
                    }
                }

                // Congelamos la estructura en Listas de ExplorerEntry optimizadas para lectura directa
                foreach (var kvp in structuralHierarchy)
                {
                    var entriesList = new List<ExplorerEntry>();
                    foreach (var childPath in kvp.Value)
                    {
                        bool isDir = _globalDirs.Contains(childPath);
                        string name = childPath.Split('/').LastOrDefault() ?? childPath;
                        entriesList.Add(new ExplorerEntry(childPath, name, isDir));
                    }
                    _hierarchy[kvp.Key] = entriesList.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }

            public bool IsDirectory(string id) => _globalDirs.Contains(id);
            public string GetParent(string id) => _parents.TryGetValue(id, out var parent) ? parent : string.Empty;
            public string GetSubPathPrefix(string id) => id.EndsWith('/') ? id : id + "/";

            public List<ExplorerEntry> FetchAndSortEntries(string id) =>
                _hierarchy.TryGetValue(id, out var list) ? list : new List<ExplorerEntry>();

            public string[] ResolveMarkedEntries(HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter)
            {
                var resolved = new List<string>();
                foreach (var path in marked)
                {
                    if (!_globalDirs.Contains(path))
                    {
                        if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(path, marked, unmarkedExceptions, this))
                            resolved.Add(path);
                    }
                    else
                    {
                        TraverseAndResolve(path, filter, marked, unmarkedExceptions, resolved);
                    }
                }
                return resolved.Distinct().OrderBy(p => p).ToArray();
            }

            private void TraverseAndResolve(string dir, ExplorerFilter filter, HashSet<string> marked, HashSet<string> unmarkedExceptions, List<string> resolved)
            {
                if (!IsPathMarked(dir, marked, unmarkedExceptions, this)) return;
                if (filter != ExplorerFilter.OnlyFiles) resolved.Add(dir);

                if (_hierarchy.TryGetValue(dir, out var children))
                {
                    foreach (var child in children)
                    {
                        if (!child.IsDirectory)
                        {
                            if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(child.Id, marked, unmarkedExceptions, this))
                                resolved.Add(child.Id);
                        }
                        else
                        {
                            TraverseAndResolve(child.Id, filter, marked, unmarkedExceptions, resolved);
                        }
                    }
                }
            }
        }

        #endregion

        #region API Pública (Overloads Limpios)

        public static async Task<string> ExploreOneAsync(string title, string rootDir, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                var result = await InternalExploreAsync(title, new PhysicalDataSource(rootDir), isMulti: false, filter, token);
                return result.FirstOrDefault() ?? string.Empty;
            }
            catch (OperationCanceledException) { return string.Empty; }
            finally { Engine.ExitFullScreen(); }
        }

        public static async Task<string[]> ExploreMultiAsync(string title, string rootDir, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                return await InternalExploreAsync(title, new PhysicalDataSource(rootDir), isMulti: true, filter, token);
            }
            catch (OperationCanceledException) { return Array.Empty<string>(); }
            finally { Engine.ExitFullScreen(); }
        }

        public static async Task<string> ExploreOneAsync(string title, IEnumerable<string> virtualPaths, string virtualRoot = "Root", ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                var result = await InternalExploreAsync(title, new VirtualDataSource(virtualPaths, virtualRoot), isMulti: false, filter, token);
                return result.FirstOrDefault() ?? string.Empty;
            }
            catch (OperationCanceledException) { return string.Empty; }
            finally { Engine.ExitFullScreen(); }
        }

        public static async Task<string[]> ExploreMultiAsync(string title, IEnumerable<string> virtualPaths, string virtualRoot = "Root", ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                return await InternalExploreAsync(title, new VirtualDataSource(virtualPaths, virtualRoot), isMulti: true, filter, token);
            }
            catch (OperationCanceledException) { return Array.Empty<string>(); }
            finally { Engine.ExitFullScreen(); }
        }

        #endregion

        #region Motor Reactor Centralizado

        private static async Task<string[]> InternalExploreAsync(string title, IExplorerDataSource dataSource, bool isMulti, ExplorerFilter filter, CancellationToken token)
        {
            string currentNode = dataSource.RootPath;
            int cursor = 0;
            StringBuilder buffer = new StringBuilder(4096);

            ScrollState layout = new ScrollState();
            bool shouldRender = true;

            HashSet<string> marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> unmarkedExceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<ExplorerEntry> entries = dataSource.FetchAndSortEntries(currentNode);

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
                    RenderTree(buffer, title, currentNode, entries, layout.Cursor, layout.Scroll, layout.VisibleRows, isMulti, filter, marked, unmarkedExceptions, dataSource);
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

                        if (isMulti && (key.KeyChar == 'c' || key.KeyChar == 'C'))
                        {
                            return dataSource.ResolveMarkedEntries(marked, unmarkedExceptions, filter);
                        }

                        if (key.Key == ConsoleKey.DownArrow || key.KeyChar == 'j' || key.KeyChar == 'J')
                        {
                            if (entries.Count > 0 && cursor < entries.Count - 1) cursor++;
                        }
                        else if (key.Key == ConsoleKey.UpArrow || key.KeyChar == 'k' || key.KeyChar == 'K')
                        {
                            if (cursor > 0) cursor--;
                        }
                        else if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.RightArrow || key.KeyChar == 'l' || key.KeyChar == 'L')
                        {
                            if (entries.Count > 0)
                            {
                                ExplorerEntry selected = entries[cursor];

                                if (selected.IsDirectory && key.Key != ConsoleKey.Spacebar)
                                {
                                    currentNode = selected.Id;
                                    entries = dataSource.FetchAndSortEntries(currentNode);
                                    cursor = 0;
                                }
                                else if (!isMulti)
                                {
                                    if (!selected.IsDirectory && filter != ExplorerFilter.OnlyFolders) return new[] { selected.Id };
                                }
                            }
                        }
                        else if (key.Key == ConsoleKey.LeftArrow || key.KeyChar == 'h' || key.KeyChar == 'H')
                        {
                            string parent = dataSource.GetParent(currentNode);
                            if (!string.IsNullOrEmpty(parent))
                            {
                                currentNode = parent;
                                entries = dataSource.FetchAndSortEntries(currentNode);
                                cursor = 0;
                            }
                        }
                        else if (key.Key == ConsoleKey.Spacebar && entries.Count > 0)
                        {
                            ExplorerEntry target = entries[cursor];

                            if (!isMulti)
                            {
                                if (target.IsDirectory && filter != ExplorerFilter.OnlyFiles) return new[] { target.Id };
                                if (!target.IsDirectory && filter != ExplorerFilter.OnlyFolders) return new[] { target.Id };
                            }
                            else
                            {
                                if (filter == ExplorerFilter.OnlyFolders && !target.IsDirectory) continue;
                                if (filter == ExplorerFilter.OnlyFiles && target.IsDirectory) continue;

                                ToggleSelection(target.Id, marked, unmarkedExceptions, dataSource);
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

        private static bool IsPathMarked(string path, HashSet<string> marked, HashSet<string> unmarkedExceptions, IExplorerDataSource dataSource)
        {
            string current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if (unmarkedExceptions.Contains(current)) return false;
                if (marked.Contains(current)) return true;
                current = dataSource.GetParent(current);
            }
            return false;
        }

        private static void ToggleSelection(string path, HashSet<string> marked, HashSet<string> unmarkedExceptions, IExplorerDataSource dataSource)
        {
            bool currentlyMarked = IsPathMarked(path, marked, unmarkedExceptions, dataSource);
            string subPathPrefix = dataSource.GetSubPathPrefix(path);

            if (currentlyMarked)
            {
                if (marked.Contains(path)) marked.Remove(path);
                else unmarkedExceptions.Add(path);

                marked.RemoveWhere(p => p.StartsWith(subPathPrefix, StringComparison.OrdinalIgnoreCase));
                unmarkedExceptions.RemoveWhere(p => p.StartsWith(subPathPrefix, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                if (unmarkedExceptions.Contains(path)) unmarkedExceptions.Remove(path);
                else marked.Add(path);

                marked.RemoveWhere(p => p.StartsWith(subPathPrefix, StringComparison.OrdinalIgnoreCase));
                unmarkedExceptions.RemoveWhere(p => p.StartsWith(subPathPrefix, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static void RenderTree(StringBuilder buffer, string title, string currentDir, List<ExplorerEntry> entries, int cursor, int scroll, int visibleRows, bool isMulti, ExplorerFilter filter, HashSet<string> marked, HashSet<string> unmarkedExceptions, IExplorerDataSource dataSource)
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

                for (int i = (end - scroll); i < visibleRows; i++) buffer.Append("\x1b[K\n");
            }

            int remaining = entries.Count - end;
            if (remaining > 0) buffer.Append($"  {ThemeColors.Dim}↓ ({remaining} más abajo){ThemeColors.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            buffer.Append("  ");
            if (isMulti)
            {
                buffer.Append($"{ThemeColors.Warning}Space{ThemeColors.Reset} marcar   {ThemeColors.Warning}h/j/k/l/←→{ThemeColors.Reset} navegar   {ThemeColors.Warning}c{ThemeColors.Reset} confirmar   {ThemeColors.Warning}Esc/q{ThemeColors.Reset} salir");
            }
            else
            {
                string helpKey = filter == ExplorerFilter.OnlyFiles ? "Enter" : "Space/Enter";
                buffer.Append($"{ThemeColors.Warning}{helpKey}{ThemeColors.Reset} elegir   {ThemeColors.Warning}h/j/k/l/←→{ThemeColors.Reset} navegar   {ThemeColors.Warning}Esc/q{ThemeColors.Reset} salir");
            }
            buffer.Append("\x1b[K\n");
            buffer.Append("\x1b[K");

            Console.Write(buffer.ToString());
        }

        #endregion
    }
}