/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Dev;
using TermFlow.Dev.CanvasExt;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    /// <summary>
    /// Componente full-screen de lista con buscador en vivo.
    /// Filtra ítems a medida que el usuario escribe y permite selección única o múltiple.
    /// </summary>
    public static class SearchList
    {
        /// <summary>Bool interno para prevenir la ejecución de múltiples searchlist a la vez.</summary>
        private static volatile bool isSearchListRunning = false;
        private const int ReservedRows = 8;

        private static int _cursor = 0;
        private static bool _exit = false;

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
                var filtered = new List<(string Text, int OriginalIndex)>();
                int result = -1;

                var router = new InputRouter(false)
                    .BindCancel(() => { result = -1; _exit = true; });

                await RunSearchEngine(title, items, filtered, null, router, token, style, startIndex, () =>
                {
                    if (filtered.Count > 0)
                        result = filtered[_cursor].OriginalIndex; _exit = true;
                });
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
        public static async Task<int[]> FilterMultiAsync(string title, IReadOnlyList<string> items, bool[] preselected = null, int startIndex = 0, CancellationToken token = default, Styles? style = null)
        {
            if (isSearchListRunning) throw new InvalidOperationException("Ya hay un SearchList activo");
            else isSearchListRunning = true;

            if (startIndex < 0 || startIndex >= items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));

            Engine.EnterFullScreen();
            try
            {
                var filtered = new List<(string Text, int OriginalIndex)>();
                int[] result = Array.Empty<int>();

                HashSet<int> selectedMap = new HashSet<int>();
                if (preselected != null)
                    for (int i = 0; i < preselected.Length; i++)
                        if (i < items.Count && preselected[i]) selectedMap.Add(i);

                var router = new InputRouter(false)
                    .BindCancel(() => { result = Array.Empty<int>(); _exit = true; })
                    .BindSelect(() =>
                    {
                        if (filtered.Count > 0)
                        {
                            int originalIdx = filtered[_cursor].OriginalIndex;
                            if (selectedMap.Contains(originalIdx)) selectedMap.Remove(originalIdx);
                            else selectedMap.Add(originalIdx);
                        }
                    });

                await RunSearchEngine(title, items, filtered, selectedMap, router, token, style, startIndex, () =>
                    {
                        result = new int[selectedMap.Count];
                        selectedMap.CopyTo(result); Array.Sort(result); _exit = true;
                    });
                return result;
            }
            catch (OperationCanceledException) { return Array.Empty<int>(); }
            finally { Engine.ExitFullScreen(); isSearchListRunning = false; }
        }

        /// <summary>
        /// Motor central compartido que maneja el bucle de filtrado, renderizado y input.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="items">Lista completa de ítems originales.</param>
        /// <param name="filtered">Lista de ítems filtrados que se irá llenando en cada ciclo.</param>
        /// <param name="selectedMap">Mapa de índices seleccionados (null si es selección única).</param>
        /// <param name="router">Enrutador de input configurado.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <param name="styleNull">Estilo visual, o <c>null</c> para usar el por defecto.</param>
        /// <param name="startIndex">Índice inicial del cursor.</param>
        private static async Task RunSearchEngine(string title, IReadOnlyList<string> items, List<(string Text, int OriginalIndex)> filtered, HashSet<int> selectedMap, InputRouter router, CancellationToken token, Styles? styleNull, int startIndex, Action OnConfirm)
        {
            var style = styleNull ?? new();
            ScrollState layout = new ScrollState();
            bool shouldRender = true;
            using var canvas = new TermCanvas(true, false, 100, onResize: (_, _) => shouldRender = true);
            var canvas2 = canvas.CreateSubCanvas(0, 0, 0, 0);
            canvas.CursorVisible = true;
            var searchEdit = new LineEdit("  Buscar: » ", router);

            _cursor = startIndex;
            _exit = false;

            router.BindConfirm(OnConfirm)
                .BindNavigate(
                        () => { if (filtered.Count > 0) _cursor = (_cursor - 1 + filtered.Count) % filtered.Count; },
                        () => { if (filtered.Count > 0) _cursor = (_cursor + 1) % filtered.Count; }
                    )
                    .BindScroll(
                        () => { if (filtered.Count > 0) _cursor = (_cursor - 1 + filtered.Count) % filtered.Count; },
                        () => { if (filtered.Count > 0) _cursor = (_cursor + 1) % filtered.Count; }
                    );

            string currentQuery = "";
            int searchCursorPos = 0;

            // El handler de LineEdit actualiza nuestras variables locales para el renderizado
            searchEdit.SetRenderHandler((text, cursor, isFinished) =>
            {
                currentQuery = text;
                searchCursorPos = cursor;
                shouldRender = true;
            });

            while (!token.IsCancellationRequested && !_exit)
            {
                // Filtrado dinámico
                filtered.Clear();
                for (int i = 0; i < items.Count; i++)
                    if (string.IsNullOrEmpty(currentQuery) || items[i].Contains(currentQuery, StringComparison.OrdinalIgnoreCase))
                        filtered.Add((items[i], i));

                if (layout.Update(_cursor, filtered.Count, ReservedRows + style.AdditionalRows))
                    shouldRender = true;

                _cursor = layout.Cursor;

                if (shouldRender)
                {
                    RenderSearch(canvas, canvas2, title, currentQuery, searchCursorPos, filtered, layout.Cursor, layout.Scroll, layout.VisibleRows, selectedMap, router, searchEdit, style);
                    shouldRender = false;
                }

                var inputEvent = InputReader.ReadInput();
                if (inputEvent.Type != InputEventType.None)
                {
                    shouldRender = true;
                    router.Handle(inputEvent);
                }
                await Task.Delay(15, token);
            }
            canvas.CursorVisible = false;
        }

        /// <summary>
        /// Dibuja el buscador completo (cabecera, query, ítems filtrados, indicadores de scroll, footer y cursor).
        /// </summary>
        /// <param name="canvas">TermCanvas reutilizable.</param>
        /// <param name="title">Título a mostrar.</param>
        /// <param name="queryString">Texto actual de la búsqueda.</param>
        /// <param name="searchCursorPos">Posición del cursor dentro del texto de búsqueda.</param>
        /// <param name="filtered">Lista de ítems filtrados con su índice original.</param>
        /// <param name="cursor">Índice del cursor dentro de los filtrados.</param>
        /// <param name="scroll">Índice del primer ítem visible.</param>
        /// <param name="visibleRows">Cantidad máxima de filas visibles.</param>
        /// <param name="selectedMap">Si no es <c>null</c>, activa el modo checkbox marcando estos índices originales.</param>
        /// <param name="router">Enrutador que renderiza el footer contextual.</param>
        /// <param name="searchEdit">Instancia de <see cref="LineEdit"/> para acceder al largo visual del prompt.</param>
        private static void RenderSearch(TermCanvas canvas, VirtualCanvas canvas2, string title, string queryString, int searchCursorPos, List<(string Text, int OriginalIndex)> filtered, int cursor, int scroll, int visibleRows, HashSet<int> selectedMap, InputRouter router, LineEdit searchEdit, Styles style)
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

            // Cabecera
            canvas2.WriteHeader(2, 1, title, lineColor: ThemeColors.Dim);
            canvas2.WriteAtAndClear(2, 3, $"Buscar: {ThemeColors.Selector}»{ThemeColors.Reset} {AnsiColor.Bold}{queryString}{ThemeColors.Reset}");

            int end = Math.Min(filtered.Count, scroll + visibleRows);

            // Indicador de scroll superior
            if (scroll > 0) canvas2.WriteAtAndClear(2, 4, $"↑ ({scroll} más arriba)", ThemeColors.Dim);
            else canvas2.ClearLine(4);

            // Renderizado de ítems filtrados
            if (filtered.Count == 0)
            {
                canvas2.WriteAtAndClear(2, 5, $"  (No se encontraron resultados)", ThemeColors.Dim);
                for (int i = 1; i < visibleRows; i++) canvas2.ClearLine(5 + i);
            }
            else
            {
                canvas2.DrawList(filtered, 2, 5, visibleRows, scroll, false, (item, i) =>
                {
                    string checkPrefix = "";
                    if (selectedMap != null)
                    {
                        bool isChecked = selectedMap.Contains(item.OriginalIndex);
                        checkPrefix = isChecked ? $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} "
                                                : $"{ThemeColors.Dim}{ConsoleGlyphs.Unchecked}{ThemeColors.Reset} ";
                    }

                    if (i == cursor)
                        return $"{ThemeColors.Selector}{ConsoleGlyphs.Indicator}{ThemeColors.Reset} {checkPrefix}{AnsiColor.Bold}{ThemeColors.Selector}{item.Text}{ThemeColors.Reset}";
                    else
                        return $"  {checkPrefix}{ThemeColors.Dim}{item.Text}{ThemeColors.Reset}";
                });
            }

            // Indicador de scroll inferior
            int remaining = filtered.Count - end;
            if (remaining > 0) canvas2.WriteAtAndClear(2, canvas2.Height - 3, $"↓ ({remaining} más abajo)", ThemeColors.Dim);
            else canvas2.ClearLine(canvas2.Height - 3);

            // Footer
            canvas2.WriteAt(2, canvas2.Height - 2, router.RenderFooter());

            // --- POSICIONAMIENTO DEL CURSOR REAL ---
            int width = canvas2.Width;
            var wrappedQueryLines = (searchEdit.LastPromptLine + queryString).WrapText(width);
            var (targetLine, targetCol) = LineEdit.MapPositionTo2D(wrappedQueryLines, searchEdit.PromptLength + searchCursorPos, width);

            int cursorRow = 4 + targetLine; // La fila 4 es donde empieza el input de búsqueda
            canvas.CursorPos = (X: targetCol - 1 + x1, Y: cursorRow - 1 + y1);

            canvas.Flush();
        }
    }
}
