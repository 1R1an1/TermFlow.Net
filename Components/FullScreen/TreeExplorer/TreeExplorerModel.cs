using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TermFlow.Components.FullScreen.TreeExplorer;

public enum ExplorerFilter
{
    All,
    OnlyFolders,
    OnlyFiles
}

/// <summary>
/// Tipo de valor compacto para minimizar objetos auxiliares durante renderizado y navegación.
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

//Interfaces públicas del origen de datos
public interface IExplorerDataSource
{
    string RootPath { get; }
    bool IsDirectory(string id);
    string GetParent(string id);
    string GetSubPathPrefix(string id);
    List<ExplorerEntry> FetchAndSortEntries(string id);
    /// <summary>
    /// Implementación opcional optimizada para la resolución de marcados.
    /// Si no se implementa (devuelve null), el motor usará la versión genérica.
    /// </summary>
    string[] ResolveMarkedEntries(HashSet<string> marked, HashSet<string> unmarkedExceptions, ExplorerFilter filter) => null;
}

/// <summary>
/// Extensión opcional para proveedores que realizan I/O asíncrona (red, etc.).
/// El motor usará automáticamente esta versión si está disponible.
/// </summary>
public interface IAsyncExplorerDataSource : IExplorerDataSource
{
    ValueTask<List<ExplorerEntry>> FetchAndSortEntriesAsync(string id, CancellationToken token);
}