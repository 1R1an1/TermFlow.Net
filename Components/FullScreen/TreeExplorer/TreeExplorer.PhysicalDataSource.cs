/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TermFlow.Components.FullScreen.TreeExplorer
{
    public static partial class TreeExplorer
    {
        /// <summary>
        /// Implementación de <see cref="IExplorerDataSource"/> sobre el sistema de archivos físico.
        /// Es una clase privada anidada dentro del partial <see cref="TreeExplorer"/>.
        /// </summary>
        private sealed class PhysicalDataSource : IExplorerDataSource
        {
            /// <summary>Ruta absoluta normalizada que actúa como raíz de la exploración.</summary>
            public string RootPath { get; }

            /// <summary>
            /// Crea el origen de datos físico.
            /// </summary>
            /// <param name="rootDir">Ruta raíz a explorar (se normaliza con <see cref="Path.GetFullPath"/>).</param>
            public PhysicalDataSource(string rootDir) => RootPath = Path.GetFullPath(rootDir);

            /// <summary>
            /// Indica si el ID corresponde a un directorio existente en disco.
            /// </summary>
            /// <param name="id">Ruta a evaluar.</param>
            /// <returns><c>true</c> si es un directorio.</returns>
            public bool IsDirectory(string id) => Directory.Exists(id);

            /// <summary>
            /// Devuelve la ruta del padre usando <see cref="DirectoryInfo.Parent"/>.
            /// </summary>
            /// <param name="id">Ruta actual.</param>
            /// <returns>Ruta del padre o <see cref="string.Empty"/> si está en la raíz del FS.</returns>
            public string GetParent(string id) => Directory.GetParent(id)?.FullName ?? string.Empty;

            /// <summary>
            /// Devuelve el ID garantizando que termina con <see cref="Path.DirectorySeparatorChar"/>.
            /// </summary>
            /// <param name="id">Ruta a normalizar.</param>
            /// <returns>Ruta con separador final.</returns>
            public string GetSubPathPrefix(string id) =>
                id.EndsWith(Path.DirectorySeparatorChar) ? id : id + Path.DirectorySeparatorChar;

            /// <summary>
            /// Enumera el contenido del directorio indicado, ordenado por nombre.
            /// Omite <see cref="FileAttributes.ReparsePoint"/> (symlinks) por seguridad.
            /// </summary>
            /// <param name="id">Ruta del directorio a enumerar.</param>
            /// <returns>Lista de entradas ordenada alfabéticamente (vacía si no existe o hay error de permisos).</returns>
            public List<ExplorerEntry> FetchAndSortEntries(string id)
            {
                var list = new List<ExplorerEntry>();
                try
                {
                    var di = new DirectoryInfo(id);
                    if (!di.Exists) return list;

                    var infos = di.GetFileSystemInfos()
                    .Where(i => (i.Attributes & FileAttributes.ReparsePoint) == 0)
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
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

            /// <summary>
            /// Versión optimizada de la resolución de marcados que recorre el disco una sola vez
            /// evitando re-escanear carpetas ya exploradas por la UI.
            /// </summary>
            /// <param name="marked">Rutas marcadas.</param>
            /// <param name="unmarkedExceptions">Excepciones de unmark.</param>
            /// <param name="filter">Filtro de tipo de entrada.</param>
            /// <returns>Array de rutas resueltas sin duplicados y ordenado.</returns>
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

            /// <summary>
            /// Recursión física que desciende por las carpetas marcadas recolectando archivos y subcarpetas.
            /// Omite reparse points para no caer en loops de symlinks.
            /// </summary>
            /// <param name="dir">Carpeta a recorrer.</param>
            /// <param name="filter">Filtro de tipo de entrada.</param>
            /// <param name="marked">Rutas marcadas.</param>
            /// <param name="unmarkedExceptions">Excepciones.</param>
            /// <param name="resolved">Lista acumuladora.</param>
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
                    {
                        var attr = File.GetAttributes(subDir);
                        if ((attr & FileAttributes.ReparsePoint) != 0)
                            continue;

                        TraversePhysical(subDir, filter, marked, unmarkedExceptions, resolved);
                    }
                }
                catch { }
            }
        }
    }
}
