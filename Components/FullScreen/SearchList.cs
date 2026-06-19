using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    public static class SearchList
    {
        // Reducido a 7 tras eliminar la línea en blanco sobrante debajo del cuadro de búsqueda
        private const int ReservedRows = 7;

        /// <summary>
        /// Buscador de selección ÚNICA. Retorna el índice original del elemento o -1 si cancela.
        /// </summary>
        public static async Task<int> FilterOneAsync(string title, string[] items, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                int cursor = 0;
                StringBuilder query = new StringBuilder();
                StringBuilder buffer = new StringBuilder(2048);

                ScrollState layout = new ScrollState();
                bool shouldRender = true;

                var filtered = new List<(string Text, int OriginalIndex)>();

                while (!token.IsCancellationRequested)
                {
                    filtered.Clear();
                    string currentQuery = query.ToString();
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (string.IsNullOrEmpty(currentQuery) || items[i].Contains(currentQuery, StringComparison.OrdinalIgnoreCase))
                        {
                            filtered.Add((items[i], i));
                        }
                    }

                    if (layout.Update(cursor, filtered.Count, ReservedRows))
                    {
                        shouldRender = true;
                        Console.Write("\x1b[2J");
                    }
                    cursor = layout.Cursor;
                    if (shouldRender)
                    {
                        RenderSearch(buffer, title, query.ToString(), filtered, layout.Cursor, layout.Scroll, layout.VisibleRows, selectedMap: null);
                        shouldRender = false;
                    }

                    var inputEvent = InputReader.ReadInput();

                    if (inputEvent.Type != InputEventType.None)
                    {
                        shouldRender = true;

                        if (inputEvent.Type == InputEventType.Key)
                        {
                            var key = inputEvent.KeyInfo;

                            if (key.Key == ConsoleKey.Escape) return -1;

                            if (key.Key == ConsoleKey.Enter)
                            {
                                return filtered.Count > 0 ? filtered[cursor].OriginalIndex : -1;
                            }

                            if (key.Key == ConsoleKey.UpArrow)
                            {
                                if (cursor > 0) cursor--;
                            }
                            else if (key.Key == ConsoleKey.DownArrow)
                            {
                                if (cursor < filtered.Count - 1) cursor++;
                            }
                            else if (key.Key == ConsoleKey.Backspace)
                            {
                                if (query.Length > 0)
                                {
                                    query.Remove(query.Length - 1, 1);
                                    cursor = 0;
                                }
                            }
                            else if (!char.IsControl(key.KeyChar))
                            {
                                query.Append(key.KeyChar);
                                cursor = 0;
                            }
                        }
                        else if (inputEvent.Type == InputEventType.ScrollUp)
                        {
                            if (cursor > 0) cursor--;
                        }
                        else if (inputEvent.Type == InputEventType.ScrollDown)
                        {
                            if (cursor < filtered.Count - 1) cursor++;
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

        /// <summary>
        /// Buscador de selección MÚLTIPLE con Checkboxes. Retorna los índices originales marcados.
        /// </summary>
        public static async Task<int[]> FilterMultiAsync(string title, string[] items, bool[] preselected = null, CancellationToken token = default)
        {
            Engine.EnterFullScreen();
            try
            {
                int cursor = 0;
                StringBuilder query = new StringBuilder();
                StringBuilder buffer = new StringBuilder(2048);

                ScrollState layout = new ScrollState();
                bool shouldRender = true;

                // Inicializar el mapa de seleccionados con los índices originales correspondientes
                HashSet<int> selectedMap = new HashSet<int>();
                if (preselected != null)
                {
                    for (int i = 0; i < preselected.Length; i++)
                    {
                        if (i < items.Length && preselected[i]) selectedMap.Add(i);
                    }
                }

                var filtered = new List<(string Text, int OriginalIndex)>();

                while (!token.IsCancellationRequested)
                {
                    filtered.Clear();
                    string currentQuery = query.ToString();
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (string.IsNullOrEmpty(currentQuery) || items[i].Contains(currentQuery, StringComparison.OrdinalIgnoreCase))
                        {
                            filtered.Add((items[i], i));
                        }
                    }

                    if (layout.Update(cursor, filtered.Count, ReservedRows))
                    {
                        shouldRender = true;
                        Console.Write("\x1b[2J");
                    }

                    if (shouldRender)
                    {
                        RenderSearch(buffer, title, query.ToString(), filtered, layout.Cursor, layout.Scroll, layout.VisibleRows, selectedMap: null);
                        shouldRender = false;
                    }

                    var inputEvent = InputReader.ReadInput();

                    if (inputEvent.Type != InputEventType.None)
                    {
                        shouldRender = true;

                        if (inputEvent.Type == InputEventType.Key)
                        {
                            var key = inputEvent.KeyInfo;

                            if (key.Key == ConsoleKey.Escape) return Array.Empty<int>();

                            // Confirmar selección (En modo buscador 'c' escribe, por lo tanto Enter es el único confirmador)
                            if (key.Key == ConsoleKey.Enter)
                            {
                                int[] result = new int[selectedMap.Count];
                                selectedMap.CopyTo(result);
                                Array.Sort(result);
                                return result;
                            }

                            // Marcar / Desmarcar con la Barra Espaciadora usando el índice original
                            if (key.Key == ConsoleKey.Spacebar && filtered.Count > 0)
                            {
                                int originalIdx = filtered[cursor].OriginalIndex;
                                if (selectedMap.Contains(originalIdx)) selectedMap.Remove(originalIdx);
                                else selectedMap.Add(originalIdx);
                            }
                            else if (key.Key == ConsoleKey.UpArrow)
                            {
                                if (cursor > 0) cursor--;
                            }
                            else if (key.Key == ConsoleKey.DownArrow)
                            {
                                if (cursor < filtered.Count - 1) cursor++;
                            }
                            else if (key.Key == ConsoleKey.Backspace)
                            {
                                if (query.Length > 0)
                                {
                                    query.Remove(query.Length - 1, 1);
                                    cursor = 0;
                                }
                            }
                            else if (!char.IsControl(key.KeyChar))
                            {
                                query.Append(key.KeyChar);
                                cursor = 0;
                            }
                        }
                        else if (inputEvent.Type == InputEventType.ScrollUp)
                        {
                            if (cursor > 0) cursor--;
                        }
                        else if (inputEvent.Type == InputEventType.ScrollDown)
                        {
                            if (cursor < filtered.Count - 1) cursor++;
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

        private static void RenderSearch(StringBuilder buffer, string title, string queryString, List<(string Text, int OriginalIndex)> filtered, int cursor, int scroll, int visibleRows, HashSet<int> selectedMap)
        {

            buffer.Clear();

            // Mover cursor a origen
            buffer.Append("\x1b[H");

            // Cabecera
            buffer.Append($"  {title}\x1b[K\n");
            buffer.Append($"  {ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, title.GetVisualLength())}{ThemeColors.Reset}\x1b[K\n");

            // Input de búsqueda predictiva (Ya no tiene el \n extra abajo, se une directo al indicador)
            buffer.Append($"  Buscar: {ThemeColors.Selector}»{ThemeColors.Reset} {AnsiColor.Bold}{queryString}{ThemeColors.Reset}_\x1b[K\n");

            int end = Math.Min(filtered.Count, scroll + visibleRows);

            // Indicador de scroll superior
            if (scroll > 0) buffer.Append($"  {ThemeColors.Dim}↑ ({scroll} más arriba){ThemeColors.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            // Renderizado de ítems filtrados
            if (filtered.Count == 0)
            {
                buffer.Append($"    {ThemeColors.Dim}(No se encontraron resultados){ThemeColors.Reset}\x1b[K\n");
                for (int i = 1; i < visibleRows; i++) buffer.Append("\x1b[K\n");
            }
            else
            {
                for (int i = scroll; i < end; i++)
                {
                    // Determinar prefijo checkbox si estamos en modo Selección Múltiple
                    string checkPrefix = "";
                    if (selectedMap != null)
                    {
                        bool isChecked = selectedMap.Contains(filtered[i].OriginalIndex);
                        checkPrefix = isChecked ? $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} "
                                                : $"{ThemeColors.Dim}{ConsoleGlyphs.Unchecked}{ThemeColors.Reset} ";
                    }

                    if (i == cursor)
                    {
                        buffer.Append($"  {ThemeColors.Selector}{ConsoleGlyphs.Indicator}{ThemeColors.Reset} {checkPrefix}{AnsiColor.Bold}{ThemeColors.Selector}{filtered[i].Text}{ThemeColors.Reset}\x1b[K\n");
                    }
                    else
                    {
                        buffer.Append($"    {checkPrefix}{ThemeColors.Dim}{filtered[i].Text}{ThemeColors.Reset}\x1b[K\n");
                    }
                }

                // Relleno de líneas vacías estricto
                for (int i = (end - scroll); i < visibleRows; i++)
                {
                    buffer.Append("\x1b[K\n");
                }
            }

            // Indicador de scroll inferior
            int remaining = filtered.Count - end;
            if (remaining > 0) buffer.Append($"  {ThemeColors.Dim}↓ ({remaining} más abajo){ThemeColors.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            // Barra de instrucciones contextual interactiva
            buffer.Append("  ");
            if (selectedMap != null) buffer.Append($"{ThemeColors.Warning}Space{ThemeColors.Reset} marcar  ");
            buffer.Append($"{ThemeColors.Warning}Letras{ThemeColors.Reset} filtrar  {ThemeColors.Warning}↑↓/Wheel{ThemeColors.Reset} navegar  {ThemeColors.Warning}Backspace{ThemeColors.Reset} borrar  {ThemeColors.Warning}Enter{ThemeColors.Reset} elegir  {ThemeColors.Warning}Esc{ThemeColors.Reset} salir");
            buffer.Append("\x1b[K\n");

            // Última línea libre (Separación estética del borde negro inferior)
            buffer.Append("\x1b[K");

            Console.Write(buffer.ToString());
        }
    }
}