/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Base;
using TermFlow.Base.CanvasExt;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    /// <summary>
    /// Componente full-screen de menú de opciones. Soporta selección única y múltiple
    /// con scroll, navegación por teclado y rueda del mouse, y footer contextual.
    /// </summary>
    public static class Menu
    {
        /// <summary>
        /// Bool interno para prevenir la ejecución de múltiples menus a la vez.
        /// </summary>
        private static volatile bool isMenuRunning = false;

        private const int ReservedRows = 7;

        private static int _cursor = 0;
        private static bool _exit = false;


        /// <summary>
        /// Muestra un menú de selección única a pantalla completa y espera la elección del usuario.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="items">Lista de opciones a elegir.</param>
        /// <param name="token">Token para cancelar la selección.</param>
        /// <returns>Índice del item elegido, o -1 si el usuario cancela (Esc/q).</returns>
        public static async Task<int> SelectOneAsync(string title, IReadOnlyList<string> items, int startIndex = 0, CancellationToken token = default, Styles? style = null)
        {
            if (isMenuRunning) throw new InvalidOperationException("Ya hay un Menu activo");
            else isMenuRunning = true;

            if (startIndex < 0 || startIndex >= items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));

            Engine.EnterFullScreen();
            try
            {
                int result = -1;

                var router = new InputRouter()
                    .BindCancel(() => { result = -1; _exit = true; })
                    .BindConfirm(() => { result = _cursor; _exit = true; });

                await RunMenuEngine(title, items, null, router, token, style, startIndex);
                return result;
            }
            catch (OperationCanceledException) { return -1; }
            finally { Engine.ExitFullScreen(); isMenuRunning = false; }
        }

        /// <summary>
        /// Muestra un menú de selección múltiple con checkboxes a pantalla completa.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="items">Lista de opciones a elegir.</param>
        /// <param name="preselected">Arreglo opcional de bools alineado con <paramref name="items"/> para marcar ítems por defecto.</param>
        /// <param name="token">Token para cancelar la selección.</param>
        /// <returns>Arreglo con los índices marcados al confirmar (ordenado), o vacío si el usuario cancela.</returns>
        public static async Task<IReadOnlyList<int>> SelectMultiAsync(string title, IReadOnlyList<string> items, bool[] preselected = null, int startIndex = 0, CancellationToken token = default, Styles? style = null)
        {
            if (isMenuRunning) throw new InvalidOperationException("Ya hay un Menu activo");
            else isMenuRunning = true;

            if (startIndex < 0 || startIndex >= items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));

            Engine.EnterFullScreen();
            try
            {
                int[] result = Array.Empty<int>();

                HashSet<int> selectedMap = new HashSet<int>();
                if (preselected != null)
                    for (int i = 0; i < preselected.Length; i++)
                        if (i < items.Count && preselected[i]) selectedMap.Add(i);

                var router = new InputRouter()
                    .BindSelect(() =>
                    {
                        if (selectedMap.Contains(_cursor)) selectedMap.Remove(_cursor);
                        else selectedMap.Add(_cursor);
                    })
                    .BindCancel(() => { result = Array.Empty<int>(); _exit = true; })
                    .BindConfirm(() =>
                    {
                        result = new int[selectedMap.Count];
                        selectedMap.CopyTo(result);
                        Array.Sort(result);
                        _exit = true;
                    });

                await RunMenuEngine(title, items, selectedMap, router, token, style, startIndex);
                return result;
            }
            catch (OperationCanceledException) { return Array.Empty<int>(); }
            finally { Engine.ExitFullScreen(); isMenuRunning = false; }
        }

        /// <summary>
        /// Motor central compartido que maneja el bucle de renderizado e input.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="items">Lista completa de ítems.</param>
        /// <param name="selectedMap">Mapa de índices seleccionados (null si es selección única).</param>
        /// <param name="router">Enrutador de input configurado.</param>
        /// <param name="token">Token de cancelación.</param>
        private static async Task RunMenuEngine(string title, IReadOnlyList<string> items, HashSet<int> selectedMap, InputRouter router, CancellationToken token, Styles? styleNull, int startIndex)
        {
            var style = styleNull ?? new Styles();
            ScrollState layout = new ScrollState();
            bool shouldRender = true;
            using var canvas = new TermCanvas(true, false, 100, onResize: (_, _) => { shouldRender = true; });
            var canvas2 = canvas.CreateSubCanvas(0, 0, 0, 0);

            _cursor = startIndex;
            _exit = false;

            router.BindNavigate(
                        () => { if (items.Count > 0) _cursor = (_cursor - 1 + items.Count) % items.Count; },
                        () => { if (items.Count > 0) _cursor = (_cursor + 1) % items.Count; }
                    )
                    .BindScroll(
                        () => { if (items.Count > 0) _cursor = (_cursor - 1 + items.Count) % items.Count; },
                        () => { if (items.Count > 0) _cursor = (_cursor + 1) % items.Count; }
                    )
                    .BindChar("g/G", "extremos", () => _cursor = 0, 'g')
                    .BindChar("", "", () => _cursor = items.Count - 1, 'G');

            while (!token.IsCancellationRequested && !_exit)
            {
                if (layout.Update(_cursor, items.Count, ReservedRows + style.AdditionalRows))
                    shouldRender = true;

                if (shouldRender)
                {
                    RenderMenu(canvas, canvas2, title, items, layout.Cursor, layout.Scroll, layout.VisibleRows, selectedMap, router, style);
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
        }

        /// <summary>
        /// Dibuja el menú completo (cabecera, ítems visibles, indicadores de scroll y footer) en el canvas.
        /// </summary>
        /// <param name="canvas">Canvas virtual para renderizar.</param>
        /// <param name="title">Título a mostrar.</param>
        /// <param name="items">Lista completa de ítems.</param>
        /// <param name="cursor">Índice del _cursor actual.</param>
        /// <param name="scroll">Índice del primer ítem visible.</param>
        /// <param name="visibleRows">Cantidad máxima de filas visibles.</param>
        /// <param name="selectedMap">Si no es <c>null</c>, activa el modo checkbox y marca los ítems incluidos.</param>
        /// <param name="router">Enrutador de input encargado de renderizar el footer contextual.</param>
        private static void RenderMenu(TermCanvas canvas, VirtualCanvas canvas2, string title, IReadOnlyList<string> items, int cursor, int scroll, int visibleRows, HashSet<int> selectedMap, InputRouter router, Styles style)
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

            int end = Math.Min(items.Count, scroll + visibleRows);

            // Indicador superior
            if (scroll > 0) canvas2.WriteAtAndClear(2, 3, $"↑ ({scroll} más arriba)", ThemeColors.Dim);
            else canvas2.ClearLine(3);

            // Elementos
            canvas2.DrawList(items, 2, 4, visibleRows, scroll, false, (item, i) =>
            {
                string checkPrefix = "";
                if (selectedMap != null)
                {
                    bool isChecked = selectedMap.Contains(i);
                    checkPrefix = isChecked ? $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} "
                                            : $"{ThemeColors.Dim}{ConsoleGlyphs.Unchecked}{ThemeColors.Reset} ";
                }

                if (i == cursor)
                    return $"{ThemeColors.Selector}{ConsoleGlyphs.Indicator}{ThemeColors.Reset} {checkPrefix}{AnsiColor.Bold}{ThemeColors.Selector}{item}{ThemeColors.Reset}";
                else
                    return $"  {checkPrefix}{ThemeColors.Dim}{item}{ThemeColors.Reset}";
            });

            int remaining = items.Count - end;
            if (remaining > 0) canvas2.WriteAtAndClear(2, canvas2.Height - 3, $"↓ ({remaining} más abajo)", ThemeColors.Dim);
            else canvas2.ClearLine(canvas2.Height - 3);

            // Footer
            canvas2.WriteAt(2, canvas2.Height - 2, router.RenderFooter());

            canvas.Flush();
        }
    }
}
