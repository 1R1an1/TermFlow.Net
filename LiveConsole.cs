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
            Task inputTask = Task.Run(() => ProcessInput(internalCts.Token, onInputSubmitted), internalCts.Token);

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
            catch (OperationCanceledException)
            {
                // Aborto fino al microsegundo
            }
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

        private async Task ProcessInput(CancellationToken token, Func<string, Task> onInputSubmitted)
        {
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
                                if (!string.IsNullOrWhiteSpace(_inputBuffer))
                                {
                                    string msg = _inputBuffer;
                                    _inputBuffer = "";
                                    _scrollOffset = 0; // Al enviar algo, el scroll se pega abajo al 100%

                                    // Disparamos la lógica de red sin frenar el render
                                    Task.Run(() => onInputSubmitted(msg), token);
                                }
                            }
                            else if (key.Key == ConsoleKey.Escape) // Salir con Esc limpio
                            {
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

                // Calculamos cuántas filas va a ocupar el input elástico
                int inputRows = ((prompt.Length + currentInput.Length) / width) + 1;
                int logRowsAvailable = Math.Max(1, height - inputRows - 1); // -1 por la barra divisoria

                // Procesamos las líneas wrapeadas de abajo hacia arriba para el scroll
                visibleLines = GetVisibleLogLines(width, logRowsAvailable, currentScroll);
            }

            // 1. Dibujamos los logs
            foreach (var line in visibleLines)
            {
                buffer.Append(line).Append("\x1b[K\n");
            }

            // 2. Dibujamos la barra divisoria
            buffer.Append($"{theme.Dim}{new string(theme.BorderHorizontal, width)}{theme.Reset}\x1b[K\n");

            // 3. Dibujamos el prompt y el texto actual (el cursor físico se quedará al final exacto)
            buffer.Append($"{theme.Primary}{theme.Bold}{prompt}{theme.Reset}{currentInput}\x1b[K");

            // Volcamos todo a la consola de un solo golpe
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

            for (int i = 0; i < text.Length; i += width)
            {
                lines.Add(text.Substring(i, Math.Min(width, text.Length - i)));
            }
            return lines;
        }
    }
}