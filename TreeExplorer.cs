using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleUtils
{
    public static class TreeExplorer
    {
        // 7 líneas estructurales fijas para el dibujo simétrico
        private const int ReservedRows = 7;

        /// <summary>
        /// Explora el sistema de archivos de forma interactiva. Retorna las rutas absolutas marcadas.
        /// </summary>
        public static async Task<string[]> ExploreAsync(string title, string rootDir, CancellationToken token = default)
        {
            // Normalizar a ruta absoluta real
            string currentDir = Path.GetFullPath(rootDir);
            int cursor = 0;
            int scroll = 0;
            StringBuilder buffer = new StringBuilder(4096);

            int lastHeight = Console.WindowHeight;
            int lastWidth = Console.WindowWidth;
            bool shouldRender = true;

            // Almacena las rutas absolutas de los elementos seleccionados
            HashSet<string> marked = new HashSet<string>();
            List<string> entries = FetchAndSortEntries(currentDir);

            while (!token.IsCancellationRequested)
            {
                // Detección de redimensionado de terminal
                if (Console.WindowHeight != lastHeight || Console.WindowWidth != lastWidth)
                {
                    lastHeight = Console.WindowHeight;
                    lastWidth = Console.WindowWidth;
                    shouldRender = true;
                    Console.Write("\x1b[2J");
                }

                int visibleRows = Math.Max(1, Console.WindowHeight - ReservedRows);

                // Control estricto de límites de scroll y cursor
                if (cursor < scroll) { scroll = cursor; shouldRender = true; }
                if (cursor >= scroll + visibleRows) { scroll = cursor - visibleRows + 1; shouldRender = true; }

                if (shouldRender)
                {
                    RenderTree(buffer, title, currentDir, entries, cursor, scroll, visibleRows, marked);
                    shouldRender = false;
                }

                var inputEvent = InputReader.ReadInput();

                if (inputEvent.Type != InputEventType.None)
                {
                    shouldRender = true;

                    if (inputEvent.Type == InputEventType.Key)
                    {
                        var key = inputEvent.KeyInfo;

                        // Cancelar operación
                        if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q' || key.KeyChar == 'Q')
                            return Array.Empty<string>();

                        // Confirmar todos los elementos marcados hasta el momento
                        if (key.KeyChar == 'c' || key.KeyChar == 'C')
                        {
                            string[] result = new string[marked.Count];
                            marked.CopyTo(result);
                            return result;
                        }

                        // Navegación Vertical: Abajo
                        if (key.Key == ConsoleKey.DownArrow || key.KeyChar == 'j' || key.KeyChar == 'J')
                        {
                            if (entries.Count > 0 && cursor < entries.Count - 1) cursor++;
                        }
                        // Navegación Vertical: Arriba
                        else if (key.Key == ConsoleKey.UpArrow || key.KeyChar == 'k' || key.KeyChar == 'K')
                        {
                            if (cursor > 0) cursor--;
                        }
                        // Navegación Horizontal: Entrar a Carpeta (Derecha / l / Enter)
                        else if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.RightArrow || key.KeyChar == 'l' || key.KeyChar == 'L')
                        {
                            if (entries.Count > 0)
                            {
                                string selectedPath = entries[cursor];
                                if (Directory.Exists(selectedPath))
                                {
                                    currentDir = selectedPath;
                                    entries = FetchAndSortEntries(currentDir);
                                    cursor = 0;
                                    scroll = 0;
                                }
                            }
                        }
                        // Navegación Horizontal: Retroceder a Padre (Izquierda / h)
                        else if (key.Key == ConsoleKey.LeftArrow || key.KeyChar == 'h' || key.KeyChar == 'H')
                        {
                            DirectoryInfo? parent = Directory.GetParent(currentDir);
                            if (parent != null)
                            {
                                currentDir = parent.FullName;
                                entries = FetchAndSortEntries(currentDir);
                                cursor = 0;
                                scroll = 0;
                            }
                        }
                        // Marcar / Desmarcar elemento con Barra Espaciadora
                        else if (key.Key == ConsoleKey.Spacebar && entries.Count > 0)
                        {
                            string targetPath = entries[cursor];
                            if (marked.Contains(targetPath)) marked.Remove(targetPath);
                            else marked.Add(targetPath);
                        }
                    }
                    else if (inputEvent.Type == InputEventType.ScrollUp)
                    {
                        if (cursor > 0) cursor--;
                    }
                    else if (inputEvent.Type == InputEventType.ScrollDown)
                    {
                        if (entries.Count > 0 && cursor < entries.Count - 1) cursor++;
                    }
                }

                await Task.Delay(15, token);
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Escanea el directorio, filtrando y ordenando: primero carpetas, luego archivos.
        /// </summary>
        private static List<string> FetchAndSortEntries(string dir)
        {
            var list = new List<string>();
            try
            {
                // 1. Obtener y ordenar carpetas alfabéticamente
                var dirs = Directory.GetDirectories(dir).OrderBy(d => d);
                list.AddRange(dirs);

                // 2. Obtener y ordenar archivos alfabéticamente
                var files = Directory.GetFiles(dir).OrderBy(f => f);
                list.AddRange(files);
            }
            catch (UnauthorizedAccessException) { /* Protección silenciosa ante carpetas del sistema sin permisos */ }
            catch (IOException) { /* Captura de errores físicos de hardware o paths rotos */ }

            return list;
        }

        private static void RenderTree(StringBuilder buffer, string title, string currentDir, List<string> entries, int cursor, int scroll, int visibleRows, HashSet<string> marked)
        {
            var theme = Engine.Theme;
            buffer.Clear();

            // Retorno al origen ANSI (0,0)
            buffer.Append("\x1b[H");

            // Cabecera principal
            buffer.Append($"  {theme.Primary}{theme.Bold}{title}{theme.Reset}\x1b[K\n");
            buffer.Append($"  {theme.Dim}{new string(theme.BorderHorizontal, Math.Max(20, title.Length))}{theme.Reset}\x1b[K\n");

            // Ruta de navegación superior
            buffer.Append($"  Ruta: {theme.Dim}{currentDir}{theme.Reset}\x1b[K\n");

            int end = Math.Min(entries.Count, scroll + visibleRows);

            // Indicador de scroll superior
            if (scroll > 0) buffer.Append($"  {theme.Dim}↑ ({scroll} más arriba){theme.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            // Cuerpo del listado
            if (entries.Count == 0)
            {
                buffer.Append($"    {theme.Dim}(Carpeta vacía o sin accesos){theme.Reset}\x1b[K\n");
                for (int i = 1; i < visibleRows; i++) buffer.Append("\x1b[K\n");
            }
            else
            {
                for (int i = scroll; i < end; i++)
                {
                    string fullPath = entries[i];
                    bool isDir = Directory.Exists(fullPath);
                    string name = Path.GetFileName(fullPath);

                    // Notación Linux: Las carpetas terminan con /
                    string displayName = isDir ? $"{name}/" : name;

                    // Evaluar checkbox de selección múltiple
                    bool isChecked = marked.Contains(fullPath);
                    string checkPrefix = isChecked ? $"{theme.Success}{theme.Checked}{theme.Reset} "
                                                    : $"{theme.Dim}{theme.Unchecked}{theme.Reset} ";

                    // Resaltado de color: Carpetas usan color primario, archivos van plano/atenuado
                    string itemStyle = isDir ? $"{theme.Primary}" : "";

                    if (i == cursor)
                    {
                        buffer.Append($"  {theme.Primary}{theme.Indicator}{theme.Reset} {checkPrefix}{theme.Bold}{itemStyle}{displayName}{theme.Reset}\x1b[K\n");
                    }
                    else
                    {
                        string normalStyle = isDir ? $"{theme.Primary}" : $"{theme.Dim}";
                        buffer.Append($"    {checkPrefix}{normalStyle}{displayName}{theme.Reset}\x1b[K\n");
                    }
                }

                // Relleno simétrico riguroso
                for (int i = (end - scroll); i < visibleRows; i++)
                {
                    buffer.Append("\x1b[K\n");
                }
            }

            // Indicador de scroll inferior
            int remaining = entries.Count - end;
            if (remaining > 0) buffer.Append($"  {theme.Dim}↓ ({remaining} más abajo){theme.Reset}\x1b[K\n");
            else buffer.Append("\x1b[K\n");

            // Barra de estado interactiva (Muestra el contador en tiempo real de marcados)
            buffer.Append("  ");
            buffer.Append($"{theme.Warning}Space{theme.Reset} marcar  {theme.Warning}h/j/k/l/←→{theme.Reset} navegar  {theme.Warning}c{theme.Reset} confirmar ({theme.Success}{marked.Count}{theme.Reset})  {theme.Warning}Esc/q{theme.Reset} salir");
            buffer.Append("\x1b[K\n");

            // Margen final libre de desborde
            buffer.Append("\x1b[K");

            Console.Write(buffer.ToString());
        }
    }
}