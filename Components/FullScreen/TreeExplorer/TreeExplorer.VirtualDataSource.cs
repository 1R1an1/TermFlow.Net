/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace TermFlow.Components.FullScreen.TreeExplorer;

public static partial class TreeExplorer
{
    /// <summary>
    /// Implementación de <see cref="IExplorerDataSource"/> sobre un conjunto de rutas virtuales
    /// (strings estilo Unix "a/b/c") cargadas en memoria. Construye una jerarquía in-memory
    /// reutilizable. Es una clase privada anidada dentro del partial <see cref="TreeExplorer"/>.
    /// </summary>
    private sealed class VirtualDataSource : IExplorerDataSource
    {
        private readonly Dictionary<string, List<ExplorerEntry>> _hierarchy = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _globalDirs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ruta virtual raíz (siempre comienza con "/").</summary>
        public string RootPath { get; }

        /// <summary>
        /// Construye la jerarquía virtual a partir de una colección de rutas.
        /// Las rutas pueden usar "/" o "\" como separador; se normalizan a "/".
        /// </summary>
        /// <param name="paths">Rutas virtuales a cargar.</param>
        /// <param name="virtualRoot">Nombre lógico a usar como raíz.</param>
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

        /// <summary>
        /// Indica si el ID virtual corresponde a un directorio (carpeta en la jerarquía).
        /// </summary>
        /// <param name="id">Ruta virtual a evaluar.</param>
        /// <returns><c>true</c> si es un directorio conocido.</returns>
        public bool IsDirectory(string id) => _globalDirs.Contains(id);

        /// <summary>
        /// Calcula el padre virtual de una ruta dividiendo por el último "/".
        /// </summary>
        /// <param name="id">Ruta virtual actual.</param>
        /// <returns>Ruta del padre, la raíz, o <see cref="string.Empty"/> si no tiene padre.</returns>
        public string GetParent(string id)
        {
            // Lógica específica para rutas virtuales con '/'
            if (string.IsNullOrEmpty(id) || id == RootPath) return string.Empty;
            int idx = id.LastIndexOf('/');
            if (idx <= 0) return RootPath;
            string parent = id.Substring(0, idx);
            return string.IsNullOrEmpty(parent) ? "/" : parent;
        }

        /// <summary>
        /// Devuelve el ID virtual garantizando que termine con "/".
        /// </summary>
        /// <param name="id">Ruta virtual a normalizar.</param>
        /// <returns>Ruta con "/" final.</returns>
        public string GetSubPathPrefix(string id) => id.EndsWith('/') ? id : id + "/";

        /// <summary>
        /// Devuelve las entradas hijas del ID virtual indicado, ordenadas alfabéticamente.
        /// </summary>
        /// <param name="id">Ruta virtual del nodo padre.</param>
        /// <returns>Lista de entradas hijas o lista vacía si el nodo no existe.</returns>
        public List<ExplorerEntry> FetchAndSortEntries(string id) =>
            _hierarchy.TryGetValue(id, out var list) ? list : new List<ExplorerEntry>();

        /// <summary>
        /// Resolución optimizada de marcados que aprovecha la jerarquía en memoria
        /// sin tocar disco ni re-construir estructuras.
        /// </summary>
        /// <param name="marked">Rutas marcadas.</param>
        /// <param name="unmarkedExceptions">Excepciones de unmark.</param>
        /// <param name="filter">Filtro de tipo de entrada.</param>
        /// <returns>Array de rutas resueltas sin duplicados y ordenado.</returns>
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

        /// <summary>
        /// Recursión sobre la jerarquía virtual que recolecta archivos y subcarpetas marcadas.
        /// </summary>
        /// <param name="dir">Carpeta virtual a recorrer.</param>
        /// <param name="filter">Filtro de tipo de entrada.</param>
        /// <param name="marked">Rutas marcadas.</param>
        /// <param name="unmarkedExceptions">Excepciones.</param>
        /// <param name="resolved">Lista acumuladora.</param>
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
