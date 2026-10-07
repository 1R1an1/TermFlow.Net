/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Dev;
using TermFlow.Dev.CanvasExt;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen.TreeExplorer
{
    /// <summary>
    /// Estado del explorador expuesto al callback de render.
    /// </summary>
    public readonly struct TreeExplorerState
    {
        /// <summary>Cursor dentro de los ítems visibles.</summary>
        public int Cursor { get; internal init; }
        /// <summary>Ruta del nodo actualmente abierto.</summary>
        public string CurrentNode { get; internal init; }
        /// <summary>Entradas visibles del nodo actual.</summary>
        public ReadOnlyCollection<ExplorerEntry> Entries { get; internal init; }
        /// <summary>Indica si el explorador está en modo selección múltiple.</summary>
        public bool IsMultiSelect { get; internal init; }
        /// <summary>Filtro activo.</summary>
        public ExplorerFilter Filter { get; internal init; }
        /// <summary>Indica si la ruta actual está bloqueada para acceso.</summary>
        public bool IsBlocked { get; internal init; }
        /// <summary>Entradas marcadas (incluyendo herencia).</summary>
        public ImmutableHashSet<ExplorerEntry> Marked { get; internal init; }
    }

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
            return false;
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
            return resolved.Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToArray();
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
        /// <param name="options">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Ruta elegida o <see cref="string.Empty"/> si se cancela.</returns>
        public static async Task<string> ExploreOneAsync(string title, string rootDir, ExplorerOptions? options = null, CancellationToken token = default, Styles? style = null)
            => await ExploreOneAsync(title, dataSource: new PhysicalDataSource(rootDir, options), options, token: token, style: style);

        /// <summary>
        /// Atajo para explorar un directorio físico con selección múltiple.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="rootDir">Ruta física raíz a explorar.</param>
        /// <param name="options">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Array de rutas marcadas o vacío si se cancela.</returns>
        public static async Task<ReadOnlyCollection<string>> ExploreMultiAsync(string title, string rootDir, ExplorerOptions? options = null, CancellationToken token = default, Styles? style = null)
            => await ExploreMultiAsync(title, dataSource: new PhysicalDataSource(rootDir, options), options, token: token, style: style);

        /// <summary>
        /// Atajo para explorar un conjunto de rutas virtuales con selección única.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="virtualPaths">Enumerables de rutas virtuales estilo Unix ("a/b/c").</param>
        /// <param name="virtualRoot">Nombre a usar como nodo raíz virtual.</param>
        /// <param name="options">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Ruta virtual elegida o <see cref="string.Empty"/> si se cancela.</returns>
        public static async Task<string> ExploreOneAsync(string title, IEnumerable<string> virtualPaths, string virtualRoot = "Root", ExplorerOptions? options = null, CancellationToken token = default, Styles? style = null)
            => await ExploreOneAsync(title, dataSource: new VirtualDataSource(virtualPaths, options, virtualRoot), options, token: token, style: style);

        /// <summary>
        /// Atajo para explorar un conjunto de rutas virtuales con selección múltiple.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="virtualPaths">Enumerables de rutas virtuales estilo Unix ("a/b/c").</param>
        /// <param name="virtualRoot">Nombre a usar como nodo raíz virtual.</param>
        /// <param name="options">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Array de rutas virtuales marcadas o vacío si se cancela.</returns>
        public static async Task<ReadOnlyCollection<string>> ExploreMultiAsync(string title, IEnumerable<string> virtualPaths, string virtualRoot = "Root", ExplorerOptions? options = null, CancellationToken token = default, Styles? style = null)
            => await ExploreMultiAsync(title, dataSource: new VirtualDataSource(virtualPaths, options, virtualRoot), options, token: token, style: style);

        /// <summary>
        /// Exploración de selección única contra un <see cref="IExplorerDataSource"/> arbitrario.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="dataSource">Origen de datos a explorar.</param>
        /// <param name="options">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="initialPath">Subruta inicial opcional dentro de <paramref name="dataSource"/>.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Ruta elegida o <see cref="string.Empty"/> si se cancela.</returns>
        public static async Task<string> ExploreOneAsync(string title, IExplorerDataSource dataSource, ExplorerOptions? options = null, string initialPath = null, CancellationToken token = default, Styles? style = null)
        {
            Engine.EnterFullScreen();
            try
            {
                string result = string.Empty;
                var router = AddBindings(dataSource, options, initialPath,
                    onState: st => { _state = st; _shouldRender = true; },
                    onSuccess: path => { result = path; _exit = true; },
                    onCancel: () => { result = string.Empty; _exit = true; });

                await RunEngineAsync(title, router, token, style);
                return result;
            }
            catch (OperationCanceledException) { return string.Empty; }
            finally { Engine.ExitFullScreen(); }
        }

        /// <summary>
        /// Exploración de selección múltiple contra un <see cref="IExplorerDataSource"/> arbitrario.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="dataSource">Origen de datos a explorar.</param>
        /// <param name="options">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="initialPath">Subruta inicial opcional dentro de <paramref name="dataSource"/>.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Array de rutas marcadas o vacío si se cancela.</returns>
        public static async Task<ReadOnlyCollection<string>> ExploreMultiAsync(string title, IExplorerDataSource dataSource, ExplorerOptions? options = null, string initialPath = null, CancellationToken token = default, Styles? style = null)
        {
            Engine.EnterFullScreen();
            try
            {
                ReadOnlyCollection<string> result = ReadOnlyCollection<string>.Empty;
                var router = AddBindingsMulti(dataSource, options, initialPath,
                    onState: st => { _state = st; _shouldRender = true; },
                    onSuccess: paths => { result = paths; _exit = true; },
                    onCancel: () => { result = ReadOnlyCollection<string>.Empty; _exit = true; });

                await RunEngineAsync(title, router, token, style);
                return result;
            }
            catch (OperationCanceledException) { return ReadOnlyCollection<string>.Empty; }
            finally { Engine.ExitFullScreen(); }
        }

        #endregion

        #region Estado estático del motor

        private static bool _exit = false;
        private static bool _shouldRender = false;
        private static TreeExplorerState _state;

        #endregion

        #region AddBindings

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un explorador de selección única sobre un directorio físico.
        /// </summary>
        /// <param name="rootDir">Ruta física raíz a explorar.</param>
        /// <param name="optionsNull">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="initialPath">Subruta inicial opcional.</param>
        /// <param name="onState">Callback invocado cuando el estado cambia.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe la ruta elegida.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <returns>El router configurado.</returns>
        public static InputRouter AddBindings(string rootDir, ExplorerOptions? optionsNull, string initialPath, Action<TreeExplorerState> onState, Action<string> onSuccess, Action onCancel)
            => AddBindings(new PhysicalDataSource(rootDir, optionsNull), optionsNull, initialPath, onState, onSuccess, onCancel);

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un explorador de selección única sobre rutas virtuales.
        /// </summary>
        /// <param name="virtualPaths">Rutas virtuales estilo Unix ("a/b/c").</param>
        /// <param name="virtualRoot">Nombre lógico de la raíz virtual.</param>
        /// <param name="optionsNull">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="initialPath">Subruta inicial opcional.</param>
        /// <param name="onState">Callback invocado cuando el estado cambia.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe la ruta elegida.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <returns>El router configurado.</returns>
        public static InputRouter AddBindings(IEnumerable<string> virtualPaths, string virtualRoot, ExplorerOptions? optionsNull, string initialPath, Action<TreeExplorerState> onState, Action<string> onSuccess, Action onCancel)
            => AddBindings(new VirtualDataSource(virtualPaths, optionsNull, virtualRoot), optionsNull, initialPath, onState, onSuccess, onCancel);

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un explorador de selección única.
        /// El motor del explorador (navegación, fetch, memoria de cursor/scroll) vive dentro de los bindings.
        /// </summary>
        /// <param name="dataSource">Origen de datos a explorar.</param>
        /// <param name="optionsNull">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="initialPath">Subruta inicial opcional.</param>
        /// <param name="onState">Callback invocado cuando el estado cambia.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe la ruta elegida.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <returns>El router configurado.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="dataSource"/> o cualquier Action es <c>null</c>.</exception>
        public static InputRouter AddBindings(IExplorerDataSource dataSource, ExplorerOptions? optionsNull, string initialPath, Action<TreeExplorerState> onState, Action<string> onSuccess, Action onCancel)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(onState);

            var options = optionsNull ?? new();
            ExplorerFilter filter = options.Filter;

            string currentNode = !string.IsNullOrEmpty(initialPath) ? Path.Combine(dataSource.RootPath, initialPath) : dataSource.RootPath;
            bool isBlocked = options.DeniedPaths.Contains(currentNode);
            int cursor = 0;
            List<ExplorerEntry> entries = isBlocked ? new() : LoadEntries(dataSource, currentNode, options);
            var cursorMemory = new Dictionary<string, int>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            string pendingBackTarget = null;

            void Notify()
            {
                onState(new TreeExplorerState
                {
                    Cursor = cursor,
                    CurrentNode = currentNode,
                    Entries = entries.AsReadOnly(),
                    IsMultiSelect = false,
                    Filter = filter,
                    IsBlocked = isBlocked,
                    Marked = null
                });
            }

            void MoveUp() { if (cursor > 0) { cursor--; Notify(); } }
            void MoveDown() { if (entries.Count > 0 && cursor < entries.Count - 1) { cursor++; Notify(); } }

            var router = new InputRouter();

            router.BindCancel(onCancel).BindNavigate(MoveUp, MoveDown).BindScroll(MoveUp, MoveDown)
                  .Bind("l/→/Enter", "elegir/entrar", () =>
                  {
                      if (entries.Count == 0) return;
                      ExplorerEntry selected = entries[cursor];
                      if (selected.IsDirectory)
                      {
                          cursorMemory[currentNode] = cursor;
                          currentNode = selected.Id;
                          isBlocked = options.DeniedPaths.Contains(currentNode);
                          entries = isBlocked ? new() : LoadEntries(dataSource, currentNode, options);
                          cursor = RestoreCursor(currentNode, entries, cursorMemory, ref pendingBackTarget);
                          Notify();
                      }
                      else if (filter != ExplorerFilter.OnlyFolders)
                      {
                          onSuccess(selected.Id);
                      }
                  }, ConsoleKey.L, ConsoleKey.RightArrow, ConsoleKey.Enter)
                  .Bind("h/←", "volver", () =>
                  {
                      string parent = dataSource.GetParent(currentNode);
                      if (string.IsNullOrEmpty(parent)) return;
                      if (options.MinDepth > 0 && GetDepth(parent) < options.MinDepth) return;

                      cursorMemory[currentNode] = cursor;
                      string childNode = currentNode;
                      currentNode = parent;
                      pendingBackTarget = childNode;
                      isBlocked = options.DeniedPaths.Contains(parent);
                      entries = isBlocked ? new() : LoadEntries(dataSource, currentNode, options);
                      cursor = RestoreCursor(currentNode, entries, cursorMemory, ref pendingBackTarget);
                      Notify();
                  }, ConsoleKey.H, ConsoleKey.LeftArrow)
                  .BindSelect(() =>
                  {
                      if (entries.Count == 0) return;
                      ExplorerEntry target = entries[cursor];
                      if (target.IsDirectory && filter != ExplorerFilter.OnlyFiles) onSuccess(target.Id);
                      else if (!target.IsDirectory && filter != ExplorerFilter.OnlyFolders) onSuccess(target.Id);
                  }, "elegir");

            Notify();
            return router;
        }

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un explorador de selección múltiple sobre un directorio físico.
        /// </summary>
        /// <param name="rootDir">Ruta física raíz a explorar.</param>
        /// <param name="optionsNull">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="initialPath">Subruta inicial opcional.</param>
        /// <param name="onState">Callback invocado cuando el estado cambia.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe las rutas marcadas.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <returns>El router configurado.</returns>
        public static InputRouter AddBindingsMulti(string rootDir, ExplorerOptions? optionsNull, string initialPath, Action<TreeExplorerState> onState, Action<ReadOnlyCollection<string>> onSuccess, Action onCancel)
            => AddBindingsMulti(new PhysicalDataSource(rootDir, optionsNull), optionsNull, initialPath, onState, onSuccess, onCancel);

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un explorador de selección múltiple sobre rutas virtuales.
        /// </summary>
        /// <param name="virtualPaths">Rutas virtuales estilo Unix ("a/b/c").</param>
        /// <param name="virtualRoot">Nombre lógico de la raíz virtual.</param>
        /// <param name="optionsNull">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="initialPath">Subruta inicial opcional.</param>
        /// <param name="onState">Callback invocado cuando el estado cambia.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe las rutas marcadas.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <returns>El router configurado.</returns>
        public static InputRouter AddBindingsMulti(IEnumerable<string> virtualPaths, string virtualRoot, ExplorerOptions? optionsNull, string initialPath, Action<TreeExplorerState> onState, Action<ReadOnlyCollection<string>> onSuccess, Action onCancel)
            => AddBindingsMulti(new VirtualDataSource(virtualPaths, optionsNull, virtualRoot), optionsNull, initialPath, onState, onSuccess, onCancel);

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un explorador de selección múltiple.
        /// El motor del explorador (navegación, fetch, memoria de cursor/scroll, marcas con herencia) vive dentro de los bindings.
        /// </summary>
        /// <param name="dataSource">Origen de datos a explorar.</param>
        /// <param name="optionsNull">Configuraciones de navegación, filtros y restricciones.</param>
        /// <param name="initialPath">Subruta inicial opcional.</param>
        /// <param name="onState">Callback invocado cuando el estado cambia.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe las rutas marcadas.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <returns>El router configurado.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="dataSource"/> o cualquier Action es <c>null</c>.</exception>
        public static InputRouter AddBindingsMulti(IExplorerDataSource dataSource, ExplorerOptions? optionsNull, string initialPath, Action<TreeExplorerState> onState, Action<ReadOnlyCollection<string>> onSuccess, Action onCancel)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(onState);

            var options = optionsNull ?? new();
            ExplorerFilter filter = options.Filter;

            string currentNode = !string.IsNullOrEmpty(initialPath) ? Path.Combine(dataSource.RootPath, initialPath) : dataSource.RootPath;
            bool isBlocked = options.DeniedPaths.Contains(currentNode);
            int cursor = 0;
            List<ExplorerEntry> entries = isBlocked ? new() : LoadEntries(dataSource, currentNode, options);
            HashSet<string> marked = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            HashSet<string> unmarkedExceptions = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var cursorMemory = new Dictionary<string, int>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            string pendingBackTarget = null;

            void Notify()
            {
                onState(new TreeExplorerState
                {
                    Cursor = cursor,
                    CurrentNode = currentNode,
                    Entries = entries.AsReadOnly(),
                    IsMultiSelect = true,
                    Filter = filter,
                    IsBlocked = isBlocked,
                    Marked = entries.Where(e => IsPathMarked(e.Id, marked, unmarkedExceptions, dataSource)).ToImmutableHashSet()
                });
            }

            void MoveUp() { if (cursor > 0) { cursor--; Notify(); } }
            void MoveDown() { if (entries.Count > 0 && cursor < entries.Count - 1) { cursor++; Notify(); } }

            var router = new InputRouter();

            router.BindCancel(onCancel).BindNavigate(MoveUp, MoveDown).BindScroll(MoveUp, MoveDown)
                  .Bind("l/→/Enter", "entrar", () =>
                  {
                      if (entries.Count == 0) return;
                      ExplorerEntry selected = entries[cursor];
                      if (selected.IsDirectory)
                      {
                          cursorMemory[currentNode] = cursor;
                          currentNode = selected.Id;
                          isBlocked = options.DeniedPaths.Contains(currentNode);
                          entries = isBlocked ? new() : LoadEntries(dataSource, currentNode, options);
                          cursor = RestoreCursor(currentNode, entries, cursorMemory, ref pendingBackTarget);
                          Notify();
                      }
                  }, ConsoleKey.L, ConsoleKey.RightArrow, ConsoleKey.Enter)
                  .Bind("h/←", "volver", () =>
                  {
                      string parent = dataSource.GetParent(currentNode);
                      if (string.IsNullOrEmpty(parent)) return;
                      if (options.MinDepth > 0 && GetDepth(parent) < options.MinDepth) return;

                      cursorMemory[currentNode] = cursor;
                      string childNode = currentNode;
                      currentNode = parent;
                      pendingBackTarget = childNode;
                      isBlocked = options.DeniedPaths.Contains(parent);
                      entries = isBlocked ? new() : LoadEntries(dataSource, currentNode, options);
                      cursor = RestoreCursor(currentNode, entries, cursorMemory, ref pendingBackTarget);
                      Notify();
                  }, ConsoleKey.H, ConsoleKey.LeftArrow)
                  .BindSelect(() =>
                  {
                      if (entries.Count == 0) return;
                      ExplorerEntry target = entries[cursor];
                      if (filter == ExplorerFilter.OnlyFolders && !target.IsDirectory) return;
                      if (filter == ExplorerFilter.OnlyFiles && target.IsDirectory) return;
                      ToggleSelection(target.Id, marked, unmarkedExceptions, dataSource);
                      Notify();
                  })
                  .Bind("c", "confirmar", () =>
                  {
                      var optimized = dataSource.ResolveMarkedEntries(marked, unmarkedExceptions, filter);
                      onSuccess((optimized ?? ResolveMarkedEntriesUniversal(dataSource, marked, unmarkedExceptions, filter)).AsReadOnly());
                  }, ConsoleKey.C);

            Notify();
            return router;
        }

        /// <summary>
        /// Carga las entradas de un nodo aplicando el filtro de HiddenPaths.
        /// </summary>
        /// <param name="dataSource">Origen de datos.</param>
        /// <param name="nodeId">ID del nodo a cargar.</param>
        /// <param name="options">Opciones con <see cref="ExplorerOptions.HiddenPaths"/>.</param>
        /// <returns>Lista de entradas visibles.</returns>
        private static List<ExplorerEntry> LoadEntries(IExplorerDataSource dataSource, string nodeId, ExplorerOptions options)
        {
            var entries = dataSource.FetchAndSortEntries(nodeId);
            if (options.HiddenPaths.Count > 0)
                entries = entries.Where(e => !options.HiddenPaths.Contains(e.Id)).ToList();
            return entries;
        }

        /// <summary>
        /// Restaura el cursor según la memoria y el pendingBackTarget.
        /// </summary>
        /// <param name="currentNode">Nodo actual.</param>
        /// <param name="entries">Entradas del nodo actual.</param>
        /// <param name="cursorMemory">Mapa de cursor recordado por nodo.</param>
        /// <param name="pendingBackTarget">Referencia al hijo a resaltar tras un back.</param>
        /// <returns>Índice del cursor restaurado.</returns>
        private static int RestoreCursor(string currentNode, List<ExplorerEntry> entries, Dictionary<string, int> cursorMemory, ref string pendingBackTarget)
        {
            if (pendingBackTarget != null)
            {
                string targetId = pendingBackTarget;
                pendingBackTarget = null;
                int foundIndex = entries.FindIndex(e => string.Equals(e.Id, targetId, StringComparison.OrdinalIgnoreCase));
                return foundIndex >= 0 ? foundIndex : 0;
            }
            if (cursorMemory.TryGetValue(currentNode, out int savedCursor))
                return Math.Clamp(savedCursor, 0, Math.Max(0, entries.Count - 1));
            return 0;
        }

        #endregion

        #region Motor central

        /// <summary>
        /// Calcula la profundidad de una ruta absoluta contando sus separadores.
        /// </summary>
        /// <param name="path">Ruta absoluta a evaluar.</param>
        /// <returns>Nivel de profundidad.</returns>
        private static int GetDepth(string path)
            => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Skip(path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':' ? 1 : 0)
                .Count();

        /// <summary>
        /// Loop central del explorador. Recibe el router ya configurado por AddBindings.
        /// </summary>
        /// <param name="title">Título a mostrar.</param>
        /// <param name="router">Router configurado por AddBindings/AddBindingsMulti.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="styleNull">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        private static async Task RunEngineAsync(string title, InputRouter router, CancellationToken token, Styles? styleNull)
        {
            var style = styleNull ?? new();
            ScrollState layout = new();
            _shouldRender = true;
            _exit = false;
            using var canvas = new TermCanvas(true, false, 100, onResize: (_, _) => _shouldRender = true);
            var canvas2 = canvas.CreateSubCanvas(0, 0, 0, 0);

            while (!token.IsCancellationRequested && !_exit)
            {
                if (layout.Update(_state.Cursor, _state.Entries.Count, ReservedRows + style.AdditionalRows))
                    _shouldRender = true;

                if (_shouldRender)
                {
                    RenderTree(canvas, canvas2, title, _state, layout.Cursor, layout.Scroll, layout.VisibleRows, router, style);
                    _shouldRender = false;
                }

                var inputEvent = InputReader.ReadInput();
                if (inputEvent.Type != InputEventType.None)
                    router.Handle(inputEvent);
                await Task.Delay(15, token);
            }
        }

        /// <summary>
        /// Render default del explorador.
        /// </summary>
        /// <param name="canvas">TermCanvas reutilizable.</param>
        /// <param name="canvas2">Sub-canvas reutilizable.</param>
        /// <param name="title">Título a mostrar.</param>
        /// <param name="state">Estado actual del explorador.</param>
        /// <param name="cursor">Cursor clampado por <see cref="ScrollState"/>.</param>
        /// <param name="scroll">Índice del primer ítem visible.</param>
        /// <param name="visibleRows">Cantidad máxima de filas visibles.</param>
        /// <param name="router">Enrutador que renderiza el footer.</param>
        /// <param name="style">Estilo visual.</param>
        private static void RenderTree(TermCanvas canvas, VirtualCanvas canvas2, string title, TreeExplorerState state, int cursor, int scroll, int visibleRows, InputRouter router, Styles style)
        {
            int W = Console.WindowWidth, H = Console.WindowHeight;
            canvas.Resize(W, H);
            canvas.Fill(0, 0, W - 1, H - 1, style.BackgroundChar == '\0' ? ' ' : style.BackgroundChar, style.BackgroundColor);

            var margin = style.Margin?.Invoke(W, H) ?? default;
            int x1 = margin.Left, y1 = margin.Top, x2 = W - margin.Right - 1, y2 = H - margin.Bottom - 1;

            if (style.DrawBorder)
            {
                canvas.DrawBorder(x1, y1, x2, y2, style.BorderColor);
                x1++; y1++; x2--; y2--;
            }

            canvas2.Resize(x1, y1, x2, y2);
            canvas2.Clear();

            canvas2.WriteHeader(2, 1, title, lineColor: ThemeColors.Dim);
            canvas2.WriteAtAndClear(2, 3, $"Ruta: {ThemeColors.Dim}{state.CurrentNode}{ThemeColors.Reset}");

            if (scroll > 0) canvas2.WriteAtAndClear(2, 4, $"↑ ({scroll} más arriba)", ThemeColors.Dim);
            else canvas2.ClearLine(4);

            int end = Math.Min(state.Entries.Count, scroll + visibleRows);

            if (state.Entries.Count == 0)
            {
                canvas2.WriteAtAndClear(2, 5, $"  {(state.IsBlocked ? "(Carpeta bloqueada)" : "(Carpeta vacía o sin accesos)")}", ThemeColors.Dim);
                for (int i = 1; i < visibleRows; i++) canvas2.ClearLine(5 + i);
            }
            else
            {
                canvas2.DrawList(state.Entries, 2, 5, visibleRows, scroll, false, (entry, i) =>
                {
                    string displayName = entry.IsDirectory ? $"{entry.Name}/" : entry.Name;

                    string checkPrefix = "";
                    if (state.IsMultiSelect)
                    {
                        bool showCheckbox = true;
                        if (state.Filter == ExplorerFilter.OnlyFolders && !entry.IsDirectory) showCheckbox = false;
                        if (state.Filter == ExplorerFilter.OnlyFiles && entry.IsDirectory) showCheckbox = false;

                        if (showCheckbox)
                        {
                            checkPrefix = state.Marked.Contains(entry) ? $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} "
                                                                       : $"{ThemeColors.Dim}{ConsoleGlyphs.Unchecked}{ThemeColors.Reset} ";
                        }
                        else
                        {
                            int visualWidth = (ConsoleGlyphs.Unchecked ?? "[ ]").Length + 1;
                            checkPrefix = new string(' ', visualWidth);
                        }
                    }

                    if (i == cursor)
                        return $"{ThemeColors.Selector}{ConsoleGlyphs.Indicator}{ThemeColors.Reset} {checkPrefix}{(entry.IsDirectory ? ThemeColors.Selector + AnsiColor.Bold : ThemeColors.Selector)}{displayName}{ThemeColors.Reset}";
                    else
                        return $"  {checkPrefix}{(entry.IsDirectory ? AnsiColor.White + AnsiColor.Bold : ThemeColors.Dim)}{displayName}{ThemeColors.Reset}";
                });
            }

            int remaining = state.Entries.Count - end;
            if (remaining > 0) canvas2.WriteAtAndClear(2, canvas2.Height - 3, $"↓ ({remaining} más abajo)", ThemeColors.Dim);
            else canvas2.ClearLine(canvas2.Height - 3);

            canvas2.WriteAt(2, canvas2.Height - 2, router.RenderFooter());

            canvas.Flush();
        }

        #endregion
    }
}
