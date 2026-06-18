using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleUtils
{
    public class LiveConsole
    {
        private readonly List<string> _logs = new();
        private readonly object _stateLock = new(); // Bloqueo unificado para variables de estado
        private readonly int _maxLogs;

        private string _inputBuffer = "";
        private int _scrollOffset = 0; // 0 = Enganchado al fondo (Sticky)

        // El semáforo maestro que despierta a la pantalla solo cuando es necesario
        private readonly SemaphoreSlim _renderSignal = new(1, 1);

        public LiveConsole(int maxLogs = 1000)
        {
            _maxLogs = maxLogs;
        }

        /// <summary>
        /// Agrega un nuevo log desde cualquier hilo (ej. red en segundo plano).
        /// </summary>
        public void WriteLog(string message)
        {
            lock (_stateLock)
            {
                _logs.Add(message);

                // FIX: Si el usuario está scrolleando arriba, aumentamos el offset
                // en la cantidad exacta de líneas que ocupa el nuevo log para congelar la pantalla.
                if (_scrollOffset > 0)
                {
                    int width = 80;
                    try { width = Console.WindowWidth; } catch { }
                    int newLines = WrapText(message, width).Count;
                    _scrollOffset += newLines;
                }

                if (_logs.Count > _maxLogs)
                {
                    _logs.RemoveAt(0); // Mantenemos el consumo de memoria a raya
                }
            }
            RequestRender();
        }

        /// <summary>
        /// Levanta la interfaz de chat interactiva.
        /// </summary>
        /// <param name="prompt">El texto antes del cursor (ej. ">>> ")</param>
        /// <param name="onInputSubmitted">Callback que se ejecuta cuando el usuario presiona Enter</param>
        public async Task RunAsync(string prompt, Func<string, Task> onInputSubmitted, CancellationToken token = default)
        {
            Engine.EnterFullScreen(); // Nos adueñamos de la pantalla y activamos el mouse
            Console.CursorVisible = true;

            using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            // Hilo 1: Lector reactivo de teclado y mouse
            Task inputTask = Task.Run(() => ProcessInput(internalCts, onInputSubmitted), internalCts.Token);

            try
            {
                // Hilo 2: Motor de Renderizado principal (Despertado por el semáforo)
                int lastWidth = Console.WindowWidth;
                int lastHeight = Console.WindowHeight;

                while (!internalCts.Token.IsCancellationRequested)
                {
                    await _renderSignal.WaitAsync(internalCts.Token);

                    // Pequeña validación de Resize por si el usuario estira la ventana
                    if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
                    {
                        Console.Write("\x1b[2J"); // Limpieza de residuos por redimensionamiento
                        lastWidth = Console.WindowWidth;
                        lastHeight = Console.WindowHeight;
                    }

                    RenderScreen(prompt, lastWidth, lastHeight);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                internalCts.Cancel();
                await inputTask; // Esperamos que cierre el lector
                Engine.ExitFullScreen(); // Devolvemos la consola a su estado natural
            }
        }

        private void RequestRender()
        {
            // Libera el semáforo solo si está en 0 para evitar acumulaciones
            if (_renderSignal.CurrentCount == 0)
            {
                _renderSignal.Release();
            }
        }

        private async Task ProcessInput(CancellationTokenSource cts, Func<string, Task> onInputSubmitted)
        {
            var token = cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);

                        // Si es la tecla ESC, verificamos si es una secuencia ANSI de Mouse
                        if (key.KeyChar == '\x1b' && Console.KeyAvailable)
                        {
                            await Task.Delay(1, token); // Damos 1ms para que entre la ráfaga completa
                            string seq = "";
                            while (Console.KeyAvailable) seq += Console.ReadKey(intercept: true).KeyChar;

                            lock (_stateLock)
                            {
                                if (seq.StartsWith("[<64;")) _scrollOffset += 3; // Rueda Arriba
                                else if (seq.StartsWith("[<65;")) _scrollOffset = Math.Max(0, _scrollOffset - 3); // Rueda Abajo
                            }
                            RequestRender();
                            continue;
                        }

                        lock (_stateLock)
                        {
                            if (key.Key == ConsoleKey.Enter)
                            {
                                // Detectar Shift+Enter o Alt+Enter para salto de línea interno
                                bool WantsNewLine = (key.Modifiers & ConsoleModifiers.Shift) != 0 ||
                                                    (key.Modifiers & ConsoleModifiers.Alt) != 0;

                                if (WantsNewLine)
                                {
                                    _inputBuffer += "\n";
                                    _scrollOffset = 0; // Lleva la vista al presente al escribir
                                }
                                else
                                {
                                    // Enter común: Enviar mensaje
                                    if (!string.IsNullOrWhiteSpace(_inputBuffer))
                                    {
                                        string msg = _inputBuffer;
                                        _inputBuffer = "";
                                        _scrollOffset = 0; // Al enviar algo, el scroll se pega abajo al 100%

                                        // SOPORTE PARA /EXIT: Si escribe /exit, cancelamos el CTS y salimos
                                        if (msg.Trim().Equals("/exit", StringComparison.OrdinalIgnoreCase))
                                        {
                                            cts.Cancel();
                                            return;
                                        }

                                        // Disparamos la lógica de red sin frenar el render
                                        Task.Run(() => onInputSubmitted(msg), token);
                                    }
                                }
                            }
                            else if (key.Key == ConsoleKey.Escape) // Salir con Esc limpio
                            {
                                cts.Cancel();
                                return; // Rompe el bucle de input, el RunAsync() principal lo detectará
                            }
                            else if (key.Key == ConsoleKey.UpArrow) { _scrollOffset++; }
                            else if (key.Key == ConsoleKey.DownArrow) { _scrollOffset = Math.Max(0, _scrollOffset - 1); }
                            else if (key.Key == ConsoleKey.End) { _scrollOffset = 0; }
                            else if (key.Key == ConsoleKey.Backspace && _inputBuffer.Length > 0)
                            {
                                _inputBuffer = _inputBuffer[..^1];
                                _scrollOffset = 0; // Si escribe o borra, lo llevamos al presente
                            }
                            else if (!char.IsControl(key.KeyChar))
                            {
                                _inputBuffer += key.KeyChar;
                                _scrollOffset = 0; // Si escribe, lo llevamos al presente
                            }
                        }
                        RequestRender();
                    }
                    else
                    {
                        // Si no hay tecla, dormimos el hilo 15ms para no fundir el procesador
                        await Task.Delay(15, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void RenderScreen(string prompt, int width, int height)
        {
            var theme = Engine.Theme;
            StringBuilder buffer = new StringBuilder(4096);
            buffer.Append("\x1b[H"); // Mover el cursor arriba a la izquierda

            List<string> visibleLines;
            string currentInput;
            int currentScroll;

            // Extraemos una copia ultrarrápida del estado para no bloquear el hilo de red
            lock (_stateLock)
            {
                currentInput = _inputBuffer;
                currentScroll = _scrollOffset;

                // FIX: Cálculo elástico de filas soportando múltiples líneas reales (\n)
                int inputRows = 0;
                string[] inputLines = currentInput.Split('\n');

                // La primera línea cuenta junto con el prompt de la consola
                inputRows += Math.Max(1, WrapText(prompt + inputLines[0], width).Count);
                for (int i = 1; i < inputLines.Length; i++)
                {
                    inputRows += Math.Max(1, WrapText(inputLines[i], width).Count);
                }

                int logRowsAvailable = Math.Max(1, height - inputRows - 1);
                visibleLines = GetVisibleLogLines(width, logRowsAvailable, currentScroll);
            }

            // 1. Dibujamos los logs
            foreach (var line in visibleLines)
            {
                buffer.Append(line).Append("\x1b[K\n");
            }

            // 2. Dibujamos la barra divisoria
            buffer.Append($"{theme.Dim}{new string(theme.BorderHorizontal, width)}{theme.Reset}\x1b[K\n");

            // 3. FIX: Dibujamos el prompt y el bloque multilínea del input limpiando renglón por renglón
            buffer.Append($"{theme.Primary}{theme.Bold}{prompt}{theme.Reset}");
            string[] renderInputLines = currentInput.Split('\n');
            for (int i = 0; i < renderInputLines.Length; i++)
            {
                buffer.Append(renderInputLines[i]).Append("\x1b[K");
                if (i < renderInputLines.Length - 1)
                {
                    buffer.Append("\n");
                }
            }

            Console.Write(buffer.ToString());
        }

        private List<string> GetVisibleLogLines(int width, int maxLines, int scrollOffset)
        {
            var result = new List<string>();
            int currentLogIndex = _logs.Count - 1;
            int linesSkipped = 0;

            // Retrocedemos en el historial envolviendo el texto a demanda
            while (currentLogIndex >= 0 && result.Count < maxLines)
            {
                string log = _logs[currentLogIndex];
                var wrappedLines = WrapText(log, width);

                // Los leemos de abajo hacia arriba para rellenar la pantalla
                for (int i = wrappedLines.Count - 1; i >= 0; i--)
                {
                    if (linesSkipped < scrollOffset)
                    {
                        linesSkipped++;
                    }
                    else if (result.Count < maxLines)
                    {
                        result.Add(wrappedLines[i]);
                    }
                }
                currentLogIndex--;
            }

            // Rellenamos el espacio vacío superior si hay muy pocos logs
            while (result.Count < maxLines)
            {
                result.Add("");
            }

            result.Reverse(); // Invertimos para que queden en orden cronológico correcto
            return result;
        }

        private List<string> WrapText(string text, int width)
        {
            if (width <= 0) return new List<string> { text };
            var lines = new List<string>();

            // 1. Separamos primero por los saltos de línea reales (\n) del mensaje
            string[] paragraphs = text.Split('\n');

            foreach (var paragraph in paragraphs)
            {
                if (paragraph.Length == 0)
                {
                    lines.Add(""); // Línea vacía si el usuario metió un Enter doble
                    continue;
                }

                // 2. Envolvemos cada párrafo según el ancho de la ventana
                for (int i = 0; i < paragraph.Length; i += width)
                {
                    lines.Add(paragraph.Substring(i, Math.Min(width, paragraph.Length - i)));
                }
            }
            return lines;
        }
    }
}