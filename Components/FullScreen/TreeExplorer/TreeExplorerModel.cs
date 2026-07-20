/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TermFlow.Components.FullScreen.TreeExplorer;

/// <summary>
/// Filtro aplicable al explorador para limitar qué tipos de entradas se pueden seleccionar.
/// </summary>
public enum ExplorerFilter
{
    /// <summary>Permite carpetas y archivos.</summary>
    All,
    /// <summary>Restringe la selección a carpetas únicamente.</summary>
    OnlyFolders,
    /// <summary>Restringe la selección a archivos únicamente.</summary>
    OnlyFiles
}

/// <summary>
/// Tipo de valor compacto para minimizar objetos auxiliares durante renderizado y navegación.
/// </summary>
public readonly struct ExplorerEntry
{
    /// <summary>Identificador único de la entrada (ruta completa o virtual).</summary>
    public string Id { get; }
    /// <summary>Nombre visible corto de la entrada.</summary>
    public string Name { get; }
    /// <summary><c>true</c> si la entrada es un directorio; <c>false</c> si es un archivo.</summary>
    public bool IsDirectory { get; }

    /// <summary>
    /// Crea una nueva entrada.
    /// </summary>
    /// <param name="id">Identificador único de la entrada.</param>
    /// <param name="name">Nombre visible corto.</param>
    /// <param name="isDirectory">Si es un directorio.</param>
    public ExplorerEntry(string id, string name, bool isDirectory)
    {
        Id = id;
        Name = name;
        IsDirectory = isDirectory;
    }
}

//Interfaces públicas del origen de datos

/// <summary>
/// Contrato que debe implementar cualquier origen de datos para el <see cref="TreeExplorer"/>.
/// Define operaciones para navegar y enumerar carpetas/archivos de forma síncrona.
/// </summary>
public interface IExplorerDataSource
{
    /// <summary>Ruta raíz del origen de datos.</summary>
    string RootPath { get; }

    /// <summary>
    /// Indica si el ID dado corresponde a un directorio.
    /// </summary>
    /// <param name="id">Identificador a evaluar.</param>
    /// <returns><c>true</c> si es un directorio.</returns>
    bool IsDirectory(string id);

    /// <summary>
    /// Devuelve el ID del padre de la entrada dada, o cadena vacía si está en la raíz.
    /// </summary>
    /// <param name="id">Identificador de la entrada.</param>
    /// <returns>ID del padre, o <see cref="string.Empty"/> si no tiene.</returns>
    string GetParent(string id);

    /// <summary>
    /// Devuelve el prefijo a usar para detectar subrutas de un ID (típicamente el ID + separador).
    /// </summary>
    /// <param name="id">Identificador de la entrada.</param>
    /// <returns>Prefijo para comparar subpaths.</returns>
    string GetSubPathPrefix(string id);

    /// <summary>
    /// Obtiene la lista ordenada de entradas hijas del ID dado.
    /// </summary>
    /// <param name="id">Identificador del nodo padre.</param>
    /// <returns>Lista de <see cref="ExplorerEntry"/> ordenada por nombre.</returns>
    List<ExplorerEntry> FetchAndSortEntries(string id);

    /// <summary>
    /// Implementación opcional optimizada para la resolución de marcados.
    /// Si no se implementa (devuelve null), el motor usará la versión genérica.
    /// </summary>
    /// <param name="marked">Conjunto de rutas marcadas explícitamente.</param>
    /// <param name="unmarkedExceptions">Conjunto de excepciones de unmark.</param>
    /// <param name="filter">Filtro de tipo de entrada a incluir.</param>
    /// <returns>Array de rutas resueltas, o <c>null</c> para usar la versión genérica del motor.</returns>
    string[] ResolveMarkedEntries(HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter) => null;
}

/// <summary>
/// Extensión opcional para proveedores que realizan I/O asíncrona (red, etc.).
/// El motor usará automáticamente esta versión si está disponible.
/// </summary>
public interface IAsyncExplorerDataSource : IExplorerDataSource
{
    /// <summary>
    /// Variante asíncrona de <see cref="IExplorerDataSource.FetchAndSortEntries"/>.
    /// </summary>
    /// <param name="id">Identificador del nodo padre.</param>
    /// <param name="token">Token de cancelación.</param>
    /// <returns>Lista de entradas hijas ordenada.</returns>
    ValueTask<List<ExplorerEntry>> FetchAndSortEntriesAsync(string id, CancellationToken token);
}
