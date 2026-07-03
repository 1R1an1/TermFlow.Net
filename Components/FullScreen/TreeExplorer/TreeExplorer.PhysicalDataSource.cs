using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TermFlow.Components.FullScreen.TreeExplorer
{
    public static partial class TreeExplorer
    {
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
                    if (!di.Exists) return list;

                    var infos = di.GetFileSystemInfos().OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
                    foreach (var info in infos)
                    {
                        bool isDir = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                        list.Add(new ExplorerEntry(info.FullName, info.Name, isDir));
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
                return list;
            }

            // Versión optimizada que no re‑escaneará el disco
            public string[] ResolveMarkedEntries(HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter)
            {
                var resolved = new List<string>();
                foreach (var path in marked)
                    if (!IsDirectory(path))
                    {
                        if (filter != ExplorerFilter.OnlyFolders && IsPathMarked(path, marked, unmarkedExceptions, this))
                            resolved.Add(path);
                    }
                    else
                        TraversePhysical(path, filter, marked, unmarkedExceptions, resolved);

                return resolved.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToArray();
            }

            private void TraversePhysical(string dir, ExplorerFilter filter,
                HashSet<string> marked, HashSet<string> unmarkedExceptions, List<string> resolved)
            {
                if (!IsPathMarked(dir, marked, unmarkedExceptions, this)) return;
                if (filter != ExplorerFilter.OnlyFiles) resolved.Add(dir);

                try
                {
                    if (filter != ExplorerFilter.OnlyFolders)
                        foreach (var file in Directory.GetFiles(dir))
                            if (IsPathMarked(file, marked, unmarkedExceptions, this))
                                resolved.Add(file);

                    foreach (var subDir in Directory.GetDirectories(dir))
                        TraversePhysical(subDir, filter, marked, unmarkedExceptions, resolved);
                }
                catch { }
            }
        }
    }
}