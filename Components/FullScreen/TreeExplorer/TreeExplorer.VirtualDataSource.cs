using System;
using System.Collections.Generic;
using System.Linq;

namespace TermFlow.Components.FullScreen.TreeExplorer;

public static partial class TreeExplorer
{
    private sealed class VirtualDataSource : IExplorerDataSource
    {
        private readonly Dictionary<string, List<ExplorerEntry>> _hierarchy = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _globalDirs = new(StringComparer.OrdinalIgnoreCase);

        public string RootPath { get; }
        public VirtualDataSource(IEnumerable<string> paths, string virtualRoot)
        {
            string cleanRoot = virtualRoot.Replace('\\', '/').Trim('/');
            RootPath = string.IsNullOrEmpty(cleanRoot) ? "/" : "/" + cleanRoot;
            _globalDirs.Add(RootPath);

            var structuralHierarchy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
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
                    bool isLast = i == segments.Length - 1;
                    bool isExplicitDir = isLast && (rawPath.EndsWith('/') || rawPath.EndsWith('\\'));

                    if (!isLast || isExplicitDir) _globalDirs.Add(next);
                    if (!structuralHierarchy.TryGetValue(current, out var children))
                    {
                        children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        structuralHierarchy[current] = children;
                    }
                    children.Add(next);
                    current = next;
                }
            }

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
        public string GetParent(string id)
        {
            // Lógica específica para rutas virtuales con '/'
            if (string.IsNullOrEmpty(id) || id == RootPath) return string.Empty;
            int idx = id.LastIndexOf('/');
            if (idx <= 0) return RootPath;
            string parent = id.Substring(0, idx);
            return string.IsNullOrEmpty(parent) ? "/" : parent;
        }
        public string GetSubPathPrefix(string id) => id.EndsWith('/') ? id : id + "/";

        public List<ExplorerEntry> FetchAndSortEntries(string id) =>
            _hierarchy.TryGetValue(id, out var list) ? list : new List<ExplorerEntry>();

        // Versión optimizada que usa la jerarquía en memoria
        public string[] ResolveMarkedEntries(HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter)
        {
            var resolved = new List<string>();
            foreach (var path in marked)
                if (!_globalDirs.Contains(path))
                    if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(path, marked, unmarkedExceptions, this))
                        resolved.Add(path);
                    else
                        TraverseVirtual(path, filter, marked, unmarkedExceptions, resolved);


            return resolved.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToArray();
        }

        private void TraverseVirtual(string dir, ExplorerFilter filter,
            HashSet<string> marked, HashSet<string> unmarkedExceptions, List<string> resolved)
        {
            if (!IsPathMarked(dir, marked, unmarkedExceptions, this)) return;
            if (filter != ExplorerFilter.OnlyFiles) resolved.Add(dir);

            if (_hierarchy.TryGetValue(dir, out var children))
                foreach (var child in children)
                    if (!child.IsDirectory)
                        if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(child.Id, marked, unmarkedExceptions, this))
                            resolved.Add(child.Id);
                        else
                            TraverseVirtual(child.Id, filter, marked, unmarkedExceptions, resolved);
        }
    }
}
