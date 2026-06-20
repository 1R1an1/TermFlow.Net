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
    public static partial class TreeExplorer
    {
        private const int ReservedRows = 8;

        #region Lógica universal de selección (estática, agnóstica al origen)

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
                {
                    TraverseUniversal(path, source, filter, marked, unmarkedExceptions, resolved);
                }
            }
            return resolved.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToArray();
        }

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

        public static async Task<string> ExploreOneAsync(string title, string rootDir, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
            => await ExploreOneAsync(title, new PhysicalDataSource(rootDir), filter, token);

        public static async Task<string[]> ExploreMultiAsync(string title, string rootDir, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
            => await ExploreMultiAsync(title, new PhysicalDataSource(rootDir), filter, token);

        public static async Task<string> ExploreOneAsync(string title, IEnumerable<string> virtualPaths, string virtualRoot = "Root", ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
            => await ExploreOneAsync(title, new VirtualDataSource(virtualPaths, virtualRoot), filter, token);

        public static async Task<string[]> ExploreMultiAsync(string title, IEnumerable<string> virtualPaths, string virtualRoot = "Root", ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
            => await ExploreMultiAsync(title, new VirtualDataSource(virtualPaths, virtualRoot), filter, token);

        public static async Task<string> ExploreOneAsync(string title, IExplorerDataSource dataSource, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                var result = await InternalExploreAsync(title, dataSource, isMulti: false, filter, token);
                return result.FirstOrDefault() ?? string.Empty;
            }
            catch (OperationCanceledException) { return string.Empty; }
            finally { Engine.ExitFullScreen(); }
        }

        public static async Task<string[]> ExploreMultiAsync(string title, IExplorerDataSource dataSource, ExplorerFilter filter = ExplorerFilter.All, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                return await InternalExploreAsync(title, dataSource, isMulti: true, filter, token);
            }
            catch (OperationCanceledException) { return Array.Empty<string>(); }
            finally { Engine.ExitFullScreen(); }
        }

        #endregion

        #region Motor central

        private static async Task<List<ExplorerEntry>> FetchEntriesAsync(IExplorerDataSource dataSource, string nodeId, CancellationToken token)
        {
            if (dataSource is IAsyncExplorerDataSource asyncSource)
                return await asyncSource.FetchAndSortEntriesAsync(nodeId, token);
            // Fuentes síncronas (disco/virtual) son inmediatas, no necesitan Task.Run
            return dataSource.FetchAndSortEntries(nodeId);
        }

        private static async Task<string[]> InternalExploreAsync(string title, IExplorerDataSource dataSource, bool isMulti, ExplorerFilter filter, CancellationToken token)
        {
            string currentNode = dataSource.RootPath;
            int cursor = 0;
            StringBuilder buffer = new StringBuilder(4096);
            ScrollState layout = new ScrollState();
            bool shouldRender = true;

            HashSet<string> marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> unmarkedExceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<ExplorerEntry> entries = await FetchEntriesAsync(dataSource, currentNode, token);

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
                            // Usa la versión optimizada si el datasource la provee
                            var optimized = dataSource.ResolveMarkedEntries(marked, unmarkedExceptions, filter);
                            return optimized ?? ResolveMarkedEntriesUniversal(dataSource, marked, unmarkedExceptions, filter);
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
                                    entries = await FetchEntriesAsync(dataSource, currentNode, token);
                                    cursor = 0;
                                }
                                else if (!isMulti)
                                {
                                    if (!selected.IsDirectory && filter != ExplorerFilter.OnlyFolders)
                                        return new[] { selected.Id };
                                }
                            }
                        }
                        else if (key.Key == ConsoleKey.LeftArrow || key.KeyChar == 'h' || key.KeyChar == 'H')
                        {
                            string parent = dataSource.GetParent(currentNode);
                            if (!string.IsNullOrEmpty(parent))
                            {
                                currentNode = parent;
                                entries = await FetchEntriesAsync(dataSource, currentNode, token);
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

        private static void RenderTree(StringBuilder buffer, string title, string currentDir, List<ExplorerEntry> entries,
            int cursor, int scroll, int visibleRows, bool isMulti, ExplorerFilter filter,
            HashSet<string> marked, HashSet<string> unmarkedExceptions, IExplorerDataSource dataSource)
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