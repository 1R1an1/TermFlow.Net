/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        // FIX: Cambiado a 6 para empujar las instrucciones hacia arriba y dejar la última línea libre
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
        public static async Task<int> SelectOneAsync(string title, string[] items, int startIndex = 0, CancellationToken token = default)
        {
            if (isMenuRunning) throw new InvalidOperationException("Ya hay un Menu activo");
            else isMenuRunning = true;

            if (startIndex < 0 || startIndex >= items.Length) throw new ArgumentOutOfRangeException(nameof(startIndex));

            Engine.EnterFullScreen();
            try
            {
                int result = -1;

                var router = new InputRouter()
                    .BindCancel(() => { result = -1; _exit = true; })
                    .BindConfirm(() => { result = _cursor; _exit = true; });

                await RunMenuEngine(title, items, null, router, token, startIndex);
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
        public static async Task<int[]> SelectMultiAsync(string title, string[] items, bool[] preselected = null, int startIndex = 0, CancellationToken token = default)
        {
            if (isMenuRunning) throw new InvalidOperationException("Ya hay un Menu activo");
            else isMenuRunning = true;

            if (startIndex < 0 || startIndex >= items.Length) throw new ArgumentOutOfRangeException(nameof(startIndex));

            Engine.EnterFullScreen();
            try
            {
                int[] result = Array.Empty<int>();

                HashSet<int> selectedMap = new HashSet<int>();
                if (preselected != null)
                    for (int i = 0; i < preselected.Length; i++)
                        if (i < items.Length && preselected[i]) selectedMap.Add(i);

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

                await RunMenuEngine(title, items, selectedMap, router, token, startIndex);
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
        private static async Task RunMenuEngine(string title, string[] items, HashSet<int> selectedMap, InputRouter router, CancellationToken token, int startIndex)
        {
            ScrollState layout = new ScrollState();
            StringBuilder buffer = new StringBuilder(2048);
            bool shouldRender = true;

            _cursor = startIndex;
            _exit = false;

            router.BindNavigate(
                        () => { if (items.Length > 0) _cursor = (_cursor - 1 + items.Length) % items.Length; },
                        () => { if (items.Length > 0) _cursor = (_cursor + 1) % items.Length; }
                    )
                    .BindScroll(
                        () => { if (items.Length > 0) _cursor = (_cursor - 1 + items.Length) % items.Length; },
                        () => { if (items.Length > 0) _cursor = (_cursor + 1) % items.Length; }
                    )
                    .BindChar("g/G", "extremos", () => _cursor = 0, 'g')
                    .BindChar("", "", () => _cursor = items.Length - 1, 'G');

            while (!token.IsCancellationRequested && !_exit)
            {
                if (layout.Update(_cursor, items.Length, ReservedRows))
                {
                    shouldRender = true;
                    Console.Write("\x1b[2J");
                }

                if (shouldRender)
                {
                    RenderMenu(buffer, title, items, layout.Cursor, layout.Scroll, layout.VisibleRows, selectedMap, router);
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
        /// Dibuja el menú completo (cabecera, ítems visibles, indicadores de scroll y footer) en el buffer.
        /// </summary>
        /// <param name="buffer">StringBuilder reutilizable para ensamblar la salida.</param>
        /// <param name="title">Título a mostrar.</param>
        /// <param name="items">Lista completa de ítems.</param>
        /// <param name="cursor">Índice del _cursor actual.</param>
        /// <param name="scroll">Índice del primer ítem visible.</param>
        /// <param name="visibleRows">Cantidad máxima de filas visibles.</param>
        /// <param name="selectedMap">Si no es <c>null</c>, activa el modo checkbox y marca los ítems incluidos.</param>
        /// <param name="router">Enrutador de input encargado de renderizar el footer contextual.</param>
        private static void RenderMenu(StringBuilder buffer, string title, string[] items, int cursor, int scroll, int visibleRows, HashSet<int> selectedMap, InputRouter router)
        {
            buffer.Clear();

            // Mover al origen (0,0)
            buffer.Append("\x1b[H");

            // Cabecera optimizada: quitamos el \n extra. Agregamos \x1b[K para limpiar fantasmas.
            buffer.Append("\x1b[K\n");
            buffer.Append($"  {title}\x1b[K\n");
            buffer.Append($"  {ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, title.GetVisualLength())}{ThemeColors.Reset}\x1b[K\n");

            int end = Math.Min(items.Length, scroll + visibleRows);

            // Indicador superior: si es 0, deja exactamente una línea en blanco limpia
            if (scroll > 0) buffer.Append($"  {ThemeColors.Dim}↑ ({scroll} más arriba){ThemeColors.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            // Elementos con borrado de línea individual (\x1b[K) para matar el bug de "pepeo"
            for (int i = scroll; i < end; i++)
            {
                string checkPrefix = "";
                if (selectedMap != null)
                {
                    bool isChecked = selectedMap.Contains(i);
                    checkPrefix = isChecked ? $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} "
                                            : $"{ThemeColors.Dim}{ConsoleGlyphs.Unchecked}{ThemeColors.Reset} ";
                }

                if (i == cursor)
                    buffer.Append($"  {ThemeColors.Selector}{ConsoleGlyphs.Indicator}{ThemeColors.Reset} {checkPrefix}{AnsiColor.Bold}{ThemeColors.Selector}{items[i]}{ThemeColors.Reset}\x1b[K\n");
                else
                    buffer.Append($"    {checkPrefix}{ThemeColors.Dim}{items[i]}{ThemeColors.Reset}\x1b[K\n");
            }

            // Relleno estricto limpiando residuos del fondo
            for (int i = end - scroll; i < visibleRows; i++)
                buffer.Append("\x1b[K\n");

            // Indicador inferior
            int remaining = items.Length - end;
            if (remaining > 0) buffer.Append($"  {ThemeColors.Dim}↓ ({remaining} más abajo){ThemeColors.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            router.RenderFooter(buffer);
            buffer.Append("\x1b[K\n");
            buffer.Append("\x1b[K");

            Console.Write(buffer.ToString());
        }
    }
}
