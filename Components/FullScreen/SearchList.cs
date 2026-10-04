/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Dev;
using TermFlow.Dev.CanvasExt;
using TermFlow.Core;
using System.Collections.ObjectModel;
using System.Linq;

namespace TermFlow.Components.FullScreen
{
    /// <summary>
    /// Estado del buscador expuesto al callback de render.
    /// </summary>
    public readonly struct SearchListState
    {
        /// <summary>Cursor dentro de los ítems filtrados.</summary>
        public int Cursor { get; internal init; }
        /// <summary>Texto actual de la búsqueda.</summary>
        public string Query { get; internal init; }
        /// <summary>Posición del cursor dentro del texto de búsqueda.</summary>
        public int QueryCursorPos { get; internal init; }
        /// <summary>Última línea visible del prompt.</summary>
        public string LastPromptLine { get; internal init; }
        /// <summary>Longitud visual del prompt.</summary>
        public int PromptLength { get; internal init; }
        /// <summary>Indica si el buscador está en modo selección múltiple.</summary>
        public bool IsMultiSelect { get; internal init; }
        /// <summary>Ítems filtrados con su estado de selección.</summary>
        public ReadOnlyCollection<(string Text, bool IsSelected)> Filtered { get; internal init; }
    }

    /// <summary>
    /// Componente full-screen de lista con buscador en vivo.
    /// Filtra ítems a medida que el usuario escribe y permite selección única o múltiple.
    /// </summary>
    public static class SearchList
    {
        private static volatile bool isSearchListRunning = false;
        private const int ReservedRows = 8;

        private static bool _exit = false;
        private static bool _shouldRender = false;
        private static SearchListState? _state;

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un buscador de selección única.
        /// </summary>
        /// <param name="items">Lista de ítems originales sobre los que filtrar.</param>
        /// <param name="prompt">Texto del prompt para el input de búsqueda.</param>
        /// <param name="startIndex">Índice inicial donde empezará el cursor antes de filtrar.</param>
        /// <param name="onState">Callback invocado cuando el cursor o el query cambian.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe el índice original del ítem elegido.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <returns>El router configurado.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="items"/>, <paramref name="prompt"/> o cualquier Action es <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="startIndex"/> está fuera de rango.</exception>
        public static InputRouter AddBindings(IReadOnlyList<string> items, string prompt, int startIndex, Action<SearchListState> onState, Action<int> onSuccess, Action onCancel)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(prompt);
            ArgumentNullException.ThrowIfNull(onState);

            if (items.Count > 0 && (startIndex < 0 || startIndex >= items.Count))
                throw new ArgumentOutOfRangeException(nameof(startIndex));

            var router = new InputRouter(false);
            var filtered = new List<(string Text, int OriginalIndex)>();
            var searchEdit = new LineEdit(prompt, router);
            int cursor = items.Count > 0 ? startIndex : 0;
            string query = "";
            int queryCursorPos = 0;

            void Notify()
            {
                onState(new SearchListState
                {
                    Cursor = cursor,
                    Query = query,
                    QueryCursorPos = queryCursorPos,
                    LastPromptLine = searchEdit.LastPromptLine,
                    PromptLength = searchEdit.PromptLength,
                    IsMultiSelect = false,
                    Filtered = filtered.ConvertAll(f => (f.Text, false)).AsReadOnly()
                });
            }
            void ApplyFilter(string q)
            {
                query = q;
                filtered.Clear();
                for (int i = 0; i < items.Count; i++)
                    if (string.IsNullOrEmpty(q) || items[i].Contains(q, StringComparison.OrdinalIgnoreCase))
                        filtered.Add((items[i], i));
                if (cursor >= filtered.Count) cursor = Math.Max(0, filtered.Count - 1);
                Notify();
            }

            searchEdit.SetRenderHandler((text, c, _) =>
            {
                queryCursorPos = c;
                ApplyFilter(text);
            });

            void MoveUp() { if (filtered.Count > 0) { cursor = (cursor - 1 + filtered.Count) % filtered.Count; Notify(); } }
            void MoveDown() { if (filtered.Count > 0) { cursor = (cursor + 1) % filtered.Count; Notify(); } }

            router.BindNavigate(MoveUp, MoveDown).BindScroll(MoveUp, MoveDown).BindCancel(onCancel)
                  .BindConfirm(() => { if (filtered.Count > 0) onSuccess(filtered[cursor].OriginalIndex); });

            ApplyFilter("");
            return router;
        }

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un buscador de selección múltiple.
        /// </summary>
        /// <param name="items">Lista de ítems originales sobre los que filtrar.</param>
        /// <param name="prompt">Texto del prompt para el input de búsqueda.</param>
        /// <param name="startIndex">Índice inicial donde empezará el cursor antes de filtrar.</param>
        /// <param name="onState">Callback invocado cuando el cursor, la selección o el query cambian.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe los índices originales elegidos.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <param name="preselected">Mapa de índices originales preseleccionados. Si es <c>null</c>, arranca vacío.</param>
        /// <returns>El router configurado.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="items"/>, <paramref name="prompt"/> o cualquier Action es <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="startIndex"/> está fuera de rango.</exception>
        public static InputRouter AddBindingsMulti(IReadOnlyList<string> items, string prompt, int startIndex, Action<SearchListState> onState, Action<ReadOnlyCollection<int>> onSuccess, Action onCancel, HashSet<int> preselected = null)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(prompt);
            ArgumentNullException.ThrowIfNull(onState);

            if (items.Count > 0 && (startIndex < 0 || startIndex >= items.Count))
                throw new ArgumentOutOfRangeException(nameof(startIndex));

            var router = new InputRouter(false);
            var filtered = new List<(string Text, int OriginalIndex)>();
            var searchEdit = new LineEdit(prompt, router);
            var selectedMap = preselected?.ToHashSet() ?? new HashSet<int>();
            int cursor = items.Count > 0 ? startIndex : 0;
            string query = "";
            int queryCursorPos = 0;

            void Notify()
            {
                onState(new SearchListState
                {
                    Cursor = cursor,
                    Query = query,
                    QueryCursorPos = queryCursorPos,
                    LastPromptLine = searchEdit.LastPromptLine,
                    PromptLength = searchEdit.PromptLength,
                    IsMultiSelect = true,
                    Filtered = filtered.ConvertAll(f => (f.Text, selectedMap.Contains(f.OriginalIndex))).AsReadOnly()
                });
            }
            void ApplyFilter(string q)
            {
                query = q;
                filtered.Clear();
                for (int i = 0; i < items.Count; i++)
                    if (string.IsNullOrEmpty(q) || items[i].Contains(q, StringComparison.OrdinalIgnoreCase))
                        filtered.Add((items[i], i));
                if (cursor >= filtered.Count) cursor = Math.Max(0, filtered.Count - 1);
                Notify();
            }

            searchEdit.SetRenderHandler((text, c, _) =>
            {
                queryCursorPos = c;
                ApplyFilter(text);
            });

            void MoveUp() { if (filtered.Count > 0) { cursor = (cursor - 1 + filtered.Count) % filtered.Count; Notify(); } }
            void MoveDown() { if (filtered.Count > 0) { cursor = (cursor + 1) % filtered.Count; Notify(); } }

            router.BindNavigate(MoveUp, MoveDown).BindScroll(MoveUp, MoveDown).BindCancel(onCancel)
                  .BindSelect(() =>
                  {
                      if (filtered.Count > 0)
                      {
                          int originalIdx = filtered[cursor].OriginalIndex;
                          if (selectedMap.Contains(originalIdx)) selectedMap.Remove(originalIdx);
                          else selectedMap.Add(originalIdx);
                          Notify();
                      }
                  })
                  .BindConfirm(() =>
                  {
                      int[] result = new int[selectedMap.Count];
                      selectedMap.CopyTo(result);
                      Array.Sort(result);
                      onSuccess(result.AsReadOnly());
                  });

            ApplyFilter("");
            return router;
        }

        /// <summary>
        /// Buscador de selección ÚNICA. Retorna el índice original del elemento o -1 si cancela.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="items">Lista de ítems sobre los que filtrar.</param>
        /// <param name="startIndex">Índice inicial donde empezará el cursor antes de filtrar.</param>
        /// <param name="token">Token para cancelar la operación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Índice original del ítem elegido, o -1 si el usuario cancela.</returns>
        public static async Task<int> FilterOneAsync(string title, IReadOnlyList<string> items, int startIndex = 0, CancellationToken token = default, Styles? style = null)
        {
            if (isSearchListRunning) throw new InvalidOperationException("Ya hay un SearchList activo");
            else isSearchListRunning = true;

            if (startIndex < 0 || startIndex >= items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));

            Engine.EnterFullScreen();
            try
            {
                int result = -1;

                var router = AddBindings(items, "  Buscar: » ", startIndex,
                    onState: st => { _state = st; _shouldRender = true; },
                    onSuccess: idx => { result = idx; _exit = true; },
                    onCancel: () => { result = -1; _exit = true; });

                await RunSearchEngine(title, router, token, style);
                return result;
            }
            catch (OperationCanceledException) { return -1; }
            finally { Engine.ExitFullScreen(); isSearchListRunning = false; }
        }

        /// <summary>
        /// Buscador de selección MÚLTIPLE con Checkboxes. Retorna los índices originales marcados.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="items">Lista de ítems sobre los que filtrar.</param>
        /// <param name="preselected">Arreglo opcional de bools alineado con <paramref name="items"/> para marcar ítems por defecto.</param>
        /// <param name="startIndex">Índice inicial donde empezará el cursor antes de filtrar.</param>
        /// <param name="token">Token para cancelar la operación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Arreglo con los índices originales marcados al confirmar, o vacío si el usuario cancela.</returns>
        public static async Task<ReadOnlyCollection<int>> FilterMultiAsync(string title, IReadOnlyList<string> items, bool[] preselected = null, int startIndex = 0, CancellationToken token = default, Styles? style = null)
        {
            if (isSearchListRunning) throw new InvalidOperationException("Ya hay un SearchList activo");
            else isSearchListRunning = true;

            if (startIndex < 0 || startIndex >= items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));

            Engine.EnterFullScreen();
            try
            {
                ReadOnlyCollection<int> result = ReadOnlyCollection<int>.Empty;
                HashSet<int> selectedMap = new();
                if (preselected != null)
                    for (int i = 0; i < preselected.Length; i++)
                        if (i < items.Count && preselected[i]) selectedMap.Add(i);

                var router = AddBindingsMulti(items, "  Buscar: » ", startIndex,
                    onState: st => { _state = st; _shouldRender = true; },
                    onSuccess: r => { result = r; _exit = true; },
                    onCancel: () => { result = ReadOnlyCollection<int>.Empty; _exit = true; },
                    preselected: selectedMap);

                await RunSearchEngine(title, router, token, style);
                return result;
            }
            catch (OperationCanceledException) { return ReadOnlyCollection<int>.Empty; }
            finally { Engine.ExitFullScreen(); isSearchListRunning = false; }
        }

        /// <summary>
        /// Motor central compartido que maneja el bucle de filtrado, renderizado y input.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="router">Enrutador de input configurado.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="style">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        private static async Task RunSearchEngine(string title, InputRouter router, CancellationToken token, Styles? style)
        {
            var s = style ?? new();
            ScrollState layout = new ScrollState();
            _shouldRender = true;
            _exit = false;
            using var canvas = new TermCanvas(true, false, 100, onResize: (_, _) => _shouldRender = true);
            var canvas2 = canvas.CreateSubCanvas(0, 0, 0, 0);
            canvas.CursorVisible = true;

            while (!token.IsCancellationRequested && !_exit)
            {
                if (layout.Update(_state.Value.Cursor, _state.Value.Filtered.Count, ReservedRows + s.AdditionalRows))
                    _shouldRender = true;

                if (_shouldRender)
                {
                    RenderSearch(canvas, canvas2, title, _state.Value, layout.Cursor, layout.Scroll, layout.VisibleRows, router, s);
                    _shouldRender = false;
                }

                var inputEvent = InputReader.ReadInput();
                if (inputEvent.Type != InputEventType.None)
                    router.Handle(inputEvent);
                await Task.Delay(15, token);
            }
            canvas.CursorVisible = false;
        }
        /// <summary>
        /// Dibuja el buscador completo (cabecera, query, ítems filtrados, indicadores de scroll, footer y cursor).
        /// </summary>
        /// <param name="canvas">TermCanvas reutilizable.</param>
        /// <param name="title">Título a mostrar.</param>
        /// <param name="cursor">Índice del cursor dentro de los filtrados.</param>
        /// <param name="scroll">Índice del primer ítem visible.</param>
        /// <param name="visibleRows">Cantidad máxima de filas visibles.</param>
        /// <param name="state">El estado del filtro y el query.</param>
        /// <param name="router">Enrutador que renderiza el footer contextual.</param>
        private static void RenderSearch(TermCanvas canvas, VirtualCanvas canvas2, string title, SearchListState state, int cursor, int scroll, int visibleRows, InputRouter router, Styles style)
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
            canvas2.WriteAtAndClear(2, 3, $"Buscar: {ThemeColors.Selector}»{ThemeColors.Reset} {AnsiColor.Bold}{state.Query}{ThemeColors.Reset}");

            int end = Math.Min(state.Filtered.Count, scroll + visibleRows);

            if (scroll > 0) canvas2.WriteAtAndClear(2, 4, $"↑ ({scroll} más arriba)", ThemeColors.Dim);
            else canvas2.ClearLine(4);

            if (state.Filtered.Count == 0)
            {
                canvas2.WriteAtAndClear(2, 5, $"  (No se encontraron resultados)", ThemeColors.Dim);
                for (int i = 1; i < visibleRows; i++) canvas2.ClearLine(5 + i);
            }
            else
            {
                canvas2.DrawList(state.Filtered, 2, 5, visibleRows, scroll, false, (item, i) =>
                {
                    string checkPrefix = "";
                    if (state.IsMultiSelect)
                    {
                        checkPrefix = item.IsSelected ? $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} "
                                                      : $"{ThemeColors.Dim}{ConsoleGlyphs.Unchecked}{ThemeColors.Reset} ";
                    }

                    if (i == cursor)
                        return $"{ThemeColors.Selector}{ConsoleGlyphs.Indicator}{ThemeColors.Reset} {checkPrefix}{AnsiColor.Bold}{ThemeColors.Selector}{item.Text}{ThemeColors.Reset}";
                    else
                        return $"  {checkPrefix}{ThemeColors.Dim}{item.Text}{ThemeColors.Reset}";
                });
            }

            int remaining = state.Filtered.Count - end;
            if (remaining > 0) canvas2.WriteAtAndClear(2, canvas2.Height - 3, $"↓ ({remaining} más abajo)", ThemeColors.Dim);
            else canvas2.ClearLine(canvas2.Height - 3);

            canvas2.WriteAt(2, canvas2.Height - 2, router.RenderFooter());

            int width = canvas2.Width;
            var wrappedQueryLines = (state.LastPromptLine + state.Query).WrapText(width);
            var (targetLine, targetCol) = LineEdit.MapPositionTo2D(wrappedQueryLines, state.PromptLength + state.QueryCursorPos, width);
            int cursorRow = 4 + targetLine;
            canvas.CursorPos = (X: targetCol - 1 + x1, Y: cursorRow - 1 + y1);

            canvas.Flush();
        }
    }
}
