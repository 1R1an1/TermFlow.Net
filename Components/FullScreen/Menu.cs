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
using System.Collections.Immutable;
using System.Linq;

namespace TermFlow.Components.FullScreen
{
    /// <summary>
    /// Componente full-screen de menú de opciones. Soporta selección única y múltiple
    /// con scroll, navegación por teclado y rueda del mouse, y footer contextual.
    /// </summary>
    public static class Menu
    {
        private static volatile bool isMenuRunning = false;
        private const int ReservedRows = 7;
        private static int _cursor = 0;
        private static bool _exit = false;
        private static bool _shouldRender = false;

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un menú de selección única.
        /// </summary>
        /// <param name="items">Lista de opciones del menú.</param>
        /// <param name="startIndex">Índice inicial del cursor.</param>
        /// <param name="onState">Callback invocado cuando el cursor cambia. Recibe el índice actual.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe el índice elegido.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <returns>El router configurado.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="items"/> o <paramref name="onState"/> son <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="startIndex"/> está fuera de rango.</exception>
        public static InputRouter AddBindings(IReadOnlyList<string> items, int startIndex, Action<int> onState, Action<int> onSuccess, Action onCancel)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(onState);
            if (items.Count > 0 && (startIndex < 0 || startIndex >= items.Count))
                throw new ArgumentOutOfRangeException(nameof(startIndex));

            var router = new InputRouter();
            int cursor = items.Count > 0 ? startIndex : 0;

            void MoveUp() { if (items.Count > 0) { cursor = (cursor - 1 + items.Count) % items.Count; onState(cursor); } }
            void MoveDown() { if (items.Count > 0) { cursor = (cursor + 1) % items.Count; onState(cursor); } }

            router.BindNavigate(MoveUp, MoveDown).BindScroll(MoveUp, MoveDown).BindCancel(onCancel).BindConfirm(() => { onSuccess(cursor); })
                  .BindChar("g/G", "extremos", () => { if (items.Count > 0) { cursor = 0; onState(cursor); } }, 'g')
                  .BindChar("", "", () => { if (items.Count > 0) { cursor = items.Count - 1; onState(cursor); } }, 'G');
            return router;
        }

        /// <summary>
        /// Configura un <see cref="InputRouter"/> con los bindings de un menú de selección múltiple.
        /// </summary>
        /// <param name="items">Lista de opciones del menú.</param>
        /// <param name="startIndex">Índice inicial del cursor.</param>
        /// <param name="onState">Callback invocado cuando el cursor o la selección cambian. Recibe el cursor y el mapa de seleccionados.</param>
        /// <param name="onSuccess">Callback invocado al confirmar. Recibe los índices elegidos.</param>
        /// <param name="onCancel">Callback invocado al cancelar.</param>
        /// <param name="preselected">Mapa de índices preseleccionados. Si es <c>null</c>, arranca vacío.</param>
        /// <returns>El router configurado.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="items"/> o cualquier Action es <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="startIndex"/> está fuera de rango.</exception>
        public static InputRouter AddBindingsMulti(IReadOnlyList<string> items, int startIndex, Action<int, ImmutableHashSet<int>> onState, Action<ReadOnlyCollection<int>> onSuccess, Action onCancel, HashSet<int> preselected = null)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(onState);
            if (items.Count > 0 && (startIndex < 0 || startIndex >= items.Count))
                throw new ArgumentOutOfRangeException(nameof(startIndex));

            var router = new InputRouter();
            int cursor = items.Count > 0 ? startIndex : 0;
            HashSet<int> selectedMap = preselected?.ToHashSet() ?? new HashSet<int>();

            void Notify() => onState(cursor, selectedMap.ToImmutableHashSet());
            void MoveUp() { if (items.Count > 0) { cursor = (cursor - 1 + items.Count) % items.Count; Notify(); } }
            void MoveDown() { if (items.Count > 0) { cursor = (cursor + 1) % items.Count; Notify(); } }

            router.BindNavigate(MoveUp, MoveDown).BindScroll(MoveUp, MoveDown).BindCancel(onCancel)
                  .BindChar("g/G", "extremos", () => { if (items.Count > 0) { cursor = 0; Notify(); } }, 'g')
                  .BindChar("", "", () => { if (items.Count > 0) { cursor = items.Count - 1; Notify(); } }, 'G')
                  .BindSelect(() =>
                  {
                      if (items.Count > 0)
                      {
                          if (selectedMap.Contains(cursor)) selectedMap.Remove(cursor);
                          else selectedMap.Add(cursor);
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

            return router;
        }

        /// <summary>
        /// Muestra un menú de selección única a pantalla completa y espera la elección del usuario.
        /// </summary>
        /// <param name="title">Título a mostrar en la cabecera.</param>
        /// <param name="items">Lista de opciones a elegir.</param>
        /// <param name="startIndex">Índice inicial del cursor.</param>
        /// <param name="token">Token para cancelar la selección.</param>
        /// <param name="style">Estilo visual del menú, o <c>null</c> para usar el por defecto.</param>
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

                var router = AddBindings(items, startIndex,
                    onState: c => { _cursor = c; _shouldRender = true; },
                    onSuccess: c => { result = c; _exit = true; },
                    onCancel: () => { result = -1; _exit = true; });

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
        /// <param name="startIndex">Índice inicial del cursor.</param>
        /// <param name="token">Token para cancelar la selección.</param>
        /// <param name="style">Estilo visual del menú, o <c>null</c> para usar el por defecto.</param>
        /// <returns>Arreglo con los índices marcados al confirmar (ordenado), o vacío si el usuario cancela.</returns>
        public static async Task<ReadOnlyCollection<int>> SelectMultiAsync(string title, IReadOnlyList<string> items, bool[] preselected = null, int startIndex = 0, CancellationToken token = default, Styles? style = null)
        {
            if (isMenuRunning) throw new InvalidOperationException("Ya hay un Menu activo");
            else isMenuRunning = true;

            if (startIndex < 0 || startIndex >= items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));

            Engine.EnterFullScreen();
            try
            {
                ReadOnlyCollection<int> result = ReadOnlyCollection<int>.Empty;

                HashSet<int> selectedMapTmp = new();
                if (preselected != null)
                    for (int i = 0; i < preselected.Length; i++)
                        if (i < items.Count && preselected[i]) selectedMapTmp.Add(i);
                ImmutableHashSet<int> selectedMap = selectedMapTmp.ToImmutableHashSet();

                var router = AddBindingsMulti(items, startIndex,
                    onState: (c, sel) => { _cursor = c; selectedMap = sel; _shouldRender = true; },
                    onSuccess: r => { result = r; _exit = true; },
                    onCancel: () => { result = ReadOnlyCollection<int>.Empty; _exit = true; }, selectedMapTmp);

                await RunMenuEngine(title, items, selectedMap, router, token, style, startIndex);
                return result;
            }
            catch (OperationCanceledException) { return ReadOnlyCollection<int>.Empty; }
            finally { Engine.ExitFullScreen(); isMenuRunning = false; }
        }

        private static async Task RunMenuEngine(string title, IReadOnlyList<string> items, ImmutableHashSet<int> selectedMap, InputRouter router, CancellationToken token, Styles? style, int startIndex)
        {
            var s = style ?? new();
            ScrollState layout = new ScrollState();
            _shouldRender = true;
            using var canvas = new TermCanvas(true, false, 100, onResize: (_, _) => _shouldRender = true);
            var canvas2 = canvas.CreateSubCanvas(0, 0, 0, 0);

            _cursor = startIndex;
            _exit = false;
            while (!token.IsCancellationRequested && !_exit)
            {
                if (layout.Update(_cursor, items.Count, ReservedRows + s.AdditionalRows))
                    _shouldRender = true;

                if (_shouldRender)
                {
                    RenderMenu(canvas, canvas2, title, items, layout.Cursor, layout.Scroll, layout.VisibleRows, selectedMap, router, s);
                    _shouldRender = false;
                }

                var inputEvent = InputReader.ReadInput();
                if (inputEvent.Type != InputEventType.None)
                    router.Handle(inputEvent);
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
        /// <param name="style">Estilo visual del menú, o <c>null</c> para usar el por defecto.</param>
        private static void RenderMenu(TermCanvas canvas, VirtualCanvas canvas2, string title, IReadOnlyList<string> items, int cursor, int scroll, int visibleRows, ImmutableHashSet<int> selectedMap, InputRouter router, Styles style)
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

            int end = Math.Min(items.Count, scroll + visibleRows);

            if (scroll > 0) canvas2.WriteAtAndClear(2, 3, $"↑ ({scroll} más arriba)", ThemeColors.Dim);
            else canvas2.ClearLine(3);

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

            canvas2.WriteAt(2, canvas2.Height - 2, router.RenderFooter());

            canvas.Flush();
        }
    }
}
