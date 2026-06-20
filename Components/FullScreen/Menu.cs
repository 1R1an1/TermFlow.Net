using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    public static class Menu
    {
        // FIX: Cambiado a 6 para empujar las instrucciones hacia arriba y dejar la última línea libre
        private const int ReservedRows = 7;

        public static async Task<int> SelectOneAsync(string title, string[] items, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                int cursor = 0;
                int scroll = 0;
                StringBuilder buffer = new StringBuilder(2048);

                int lastHeight = Console.WindowHeight;
                int lastWidth = Console.WindowWidth;
                bool shouldRender = true;

                while (!token.IsCancellationRequested)
                {
                    if (Console.WindowHeight != lastHeight || Console.WindowWidth != lastWidth)
                    {
                        lastHeight = Console.WindowHeight;
                        lastWidth = Console.WindowWidth;
                        shouldRender = true;
                        Console.Write("\x1b[2J");
                    }

                    int visibleRows = Math.Max(1, Console.WindowHeight - ReservedRows);

                    if (cursor < scroll) { scroll = cursor; shouldRender = true; }
                    if (cursor >= scroll + visibleRows) { scroll = cursor - visibleRows + 1; shouldRender = true; }

                    if (shouldRender)
                    {
                        RenderMenu(buffer, title, items, cursor, scroll, visibleRows, selectedMap: null);
                        shouldRender = false;
                    }

                    var inputEvent = InputReader.ReadInput();

                    if (inputEvent.Type != InputEventType.None)
                    {
                        shouldRender = true;

                        if (inputEvent.Type == InputEventType.Key)
                        {
                            var key = inputEvent.KeyInfo;

                            if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q' || key.KeyChar == 'Q') return -1;
                            if (key.Key == ConsoleKey.Enter) return cursor;

                            if (key.Key == ConsoleKey.UpArrow || key.KeyChar == 'k' || key.KeyChar == 'K')
                            {
                                if (cursor > 0) cursor--;
                            }
                            if (key.Key == ConsoleKey.DownArrow || key.KeyChar == 'j' || key.KeyChar == 'J')
                            {
                                if (cursor < items.Length - 1) cursor++;
                            }
                            if (key.KeyChar == 'g') cursor = 0;
                            if (key.KeyChar == 'G') cursor = items.Length - 1;
                        }
                        else if (inputEvent.Type == InputEventType.ScrollUp)
                        {
                            if (cursor > 0) cursor--;
                        }
                        else if (inputEvent.Type == InputEventType.ScrollDown)
                        {
                            if (cursor < items.Length - 1) cursor++;
                        }
                    }

                    await Task.Delay(15, token);
                }

                return -1;
            }
            catch (OperationCanceledException)
            {
                return -1;
            }
            finally
            {
                Engine.ExitFullScreen();
            }
        }

        public static async Task<int[]> SelectMultiAsync(string title, string[] items, bool[] preselected = null, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                int cursor = 0;
                StringBuilder buffer = new StringBuilder(2048);

                ScrollState layout = new ScrollState();
                bool shouldRender = true;

                HashSet<int> selectedMap = new HashSet<int>();
                if (preselected != null)
                {
                    for (int i = 0; i < preselected.Length; i++)
                    {
                        if (i < items.Length && preselected[i]) selectedMap.Add(i);
                    }
                }

                while (!token.IsCancellationRequested)
                {
                    if (layout.Update(cursor, items.Length, ReservedRows))
                    {
                        shouldRender = true;
                        Console.Write("\x1b[2J");
                    }
                    cursor = layout.Cursor;
                    if (shouldRender)
                    {
                        RenderMenu(buffer, title, items, layout.Cursor, layout.Scroll, layout.VisibleRows, selectedMap);
                        shouldRender = false;
                    }

                    var inputEvent = InputReader.ReadInput();

                    if (inputEvent.Type != InputEventType.None)
                    {
                        shouldRender = true;

                        if (inputEvent.Type == InputEventType.Key)
                        {
                            var key = inputEvent.KeyInfo;

                            if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q' || key.KeyChar == 'Q') return Array.Empty<int>();

                            if (key.KeyChar == 'c' || key.KeyChar == 'C' || key.Key == ConsoleKey.Enter)
                            {
                                int[] result = new int[selectedMap.Count];
                                selectedMap.CopyTo(result);
                                Array.Sort(result);
                                return result;
                            }
                            if (key.Key == ConsoleKey.Spacebar)
                            {
                                if (selectedMap.Contains(cursor)) selectedMap.Remove(cursor);
                                else selectedMap.Add(cursor);
                            }
                            if (key.Key == ConsoleKey.UpArrow || key.KeyChar == 'k' || key.KeyChar == 'K')
                            {
                                if (cursor > 0) cursor--;
                            }
                            if (key.Key == ConsoleKey.DownArrow || key.KeyChar == 'j' || key.KeyChar == 'J')
                            {
                                if (cursor < items.Length - 1) cursor++;
                            }
                            if (key.KeyChar == 'g') cursor = 0;
                            if (key.KeyChar == 'G') cursor = items.Length - 1;
                        }
                        else if (inputEvent.Type == InputEventType.ScrollUp)
                        {
                            if (cursor > 0) cursor--;
                        }
                        else if (inputEvent.Type == InputEventType.ScrollDown)
                        {
                            if (cursor < items.Length - 1) cursor++;
                        }
                    }

                    await Task.Delay(15, token);
                }

                return Array.Empty<int>();
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<int>();
            }
            finally
            {
                Engine.ExitFullScreen();
            }
        }

        private static void RenderMenu(StringBuilder buffer, string title, string[] items, int cursor, int scroll, int visibleRows, HashSet<int> selectedMap)
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
                {
                    buffer.Append($"  {ThemeColors.Selector}{ConsoleGlyphs.Indicator}{ThemeColors.Reset} {checkPrefix}{AnsiColor.Bold}{ThemeColors.Selector}{items[i]}{ThemeColors.Reset}\x1b[K\n");
                }
                else
                {
                    buffer.Append($"    {checkPrefix}{ThemeColors.Dim}{items[i]}{ThemeColors.Reset}\x1b[K\n");
                }
            }

            // Relleno estricto limpiando residuos del fondo
            for (int i = (end - scroll); i < visibleRows; i++)
            {
                buffer.Append("\x1b[K\n");
            }

            // Indicador inferior
            int remaining = items.Length - end;
            if (remaining > 0) buffer.Append($"  {ThemeColors.Dim}↓ ({remaining} más abajo){ThemeColors.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            // Barra de instrucciones (Se imprime en la penúltima línea)
            buffer.Append("  ");
            if (selectedMap != null) buffer.Append($"{ThemeColors.Warning}Space{ThemeColors.Reset} marcar  ");
            buffer.Append($"{ThemeColors.Warning}↑↓/j/k{ThemeColors.Reset} navegar  {ThemeColors.Warning}MouseWheel{ThemeColors.Reset} scroll  {ThemeColors.Warning}Enter/c{ThemeColors.Reset} confirmar  {ThemeColors.Warning}Esc/q{ThemeColors.Reset} salir");
            buffer.Append("\x1b[K\n"); // Salta a la última línea absoluta

            // Última línea: la limpiamos pero NO ponemos \n para evitar el scroll del buffer.
            // Esto la mantiene vacía y genera la separación del fondo.
            buffer.Append("\x1b[K");

            Console.Write(buffer.ToString());
        }
    }
}