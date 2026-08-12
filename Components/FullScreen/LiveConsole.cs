/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.Core;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    /// <summary>
    /// Consola interactiva estilo chat (REPL) a pantalla completa.
    /// Mantiene un historial de logs scrollable con barra divisoria inteligente que avisa
    /// cuando hay mensajes nuevos abajo, y un input multilínea con soporte para Shift+Enter.
    /// </summary>
    public class LiveConsole
    {
        private readonly List<string> _logs = new();
        private readonly object _stateLock = new(); // Bloqueo unificado para variables de estado
        private readonly int _maxLogs;

        /// <summary>Máximo desplazamiento permitido del scroll en base al total de líneas y el alto de la consola.</summary>
        private int _maxScroll;

        private string _inputBuffer = "";
        private bool _hasNewLogsBelow = false;
        private int _scrollOffset = 0; // 0 = Enganchado al fondo (Sticky)

        private readonly SemaphoreSlim _renderSignal = new(0, 1);

        /// <summary>Enrutador de entrada compartido entre LiveConsole y LineEdit.</summary>
        private InputRouter _router = null;

        /// <summary>Componente de edición de línea con todos los bindings de teclado.</summary>
        private LineEdit _lineEdit = null;

        /// <summary>Token source interno para cancelar la sesión desde cualquier bind.</summary>
        private CancellationTokenSource _internalCts;

        private int _renderPending;
        private int _cursorPos;

        /// <summary> 
        /// Crea una nueva instancia de <see cref="LiveConsole"/>.
        /// </summary>
        /// <param name="maxLogs">Cantidad máxima de logs a retener en memoria (FIFO).</param>
        public LiveConsole(int maxLogs = 1000)
        {
            _maxLogs = maxLogs;

            // --- Configuración única del InputRouter con las acciones propias de LiveConsole ---
            _router = new InputRouter(false)

            // Scroll de teclado (PageUp/PageDown conservan el comportamiento sin chocar con las flechas)
            .BindNavigate(() =>
            {
                lock (_stateLock) _scrollOffset++;
                RequestRender();
            }, () =>
            {
                lock (_stateLock) _scrollOffset = Math.Max(0, _scrollOffset - 1);
                RequestRender();
            })
            .Bind("", "", () =>
            {
                lock (_stateLock) _scrollOffset = _maxScroll;
                RequestRender();
            }, ConsoleKey.PageUp)

            // Ir al final del historial
            .Bind("", "", () =>
            {
                lock (_stateLock) _scrollOffset = 0;
                RequestRender();
            }, ConsoleKey.PageDown)

            // Escape cancela la sesión
            .BindCancel(() => _internalCts?.Cancel())

            // Scroll de logs
            .BindScroll(() =>
            {
                lock (_stateLock) _scrollOffset += 3;
                RequestRender();
            }, () =>
            {
                lock (_stateLock)
                    _scrollOffset = Math.Max(0, _scrollOffset - 3);
                RequestRender();
            });
        }

        /// <summary>
        /// Agrega un nuevo log desde cualquier hilo (ej. red en segundo plano).
        /// </summary>
        /// <param name="message">Texto del log (puede contener ANSI y saltos de línea).</param>
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
                    int newLines = message.CountPhysicalLines(width);
                    _scrollOffset += newLines;

                    _hasNewLogsBelow = true;
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
        /// <param name="token">Token para cancelar la ejecución.</param>
        public async Task RunAsync(string prompt, Func<string, Task> onInputSubmitted, CancellationToken token = default)
        {
            Engine.EnterFullScreen(); // Nos adueñamos de la pantalla y activamos el mouse
            Console.CursorVisible = true;

            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            // --- Creación del LineEdit con el _router compartido ---
            _lineEdit = _lineEdit ?? new LineEdit(prompt, _router);
            _lineEdit.SetRenderHandler((text, cursor, isFinished) =>
            {
                lock (_stateLock)
                {
                    _cursorPos = cursor;
                    _inputBuffer = text;
                    if (isFinished)
                    {
                        if (text.Trim().Equals("/exit", StringComparison.OrdinalIgnoreCase))
                            _internalCts.Cancel();
                        else
                            Task.Run(() => onInputSubmitted(text));
                        _lineEdit.Clear();
                    }
                }
                RequestRender();
            });

            // Hilo 1: Lector reactivo de teclado y mouse
            Task inputTask = Task.Run(() => ProcessInput(_internalCts.Token), _internalCts.Token);
            RequestRender();

            try
            {
                // Hilo 2: Motor de Renderizado principal (Despertado por el semáforo)
                int lastWidth = Console.WindowWidth;
                int lastHeight = Console.WindowHeight;

                while (!_internalCts.Token.IsCancellationRequested)
                {
                    await _renderSignal.WaitAsync(_internalCts.Token);
                    Interlocked.Exchange(ref _renderPending, 0);

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
                _internalCts.Cancel();
                _internalCts.Dispose();
                await inputTask; // Esperamos que cierre el lector
                Engine.ExitFullScreen(); // Devolvemos la consola a su estado natural
            }
        }

        /// <summary>
        /// Solicita un frame de render al loop. Si ya hay uno pendiente, no hace nada
        /// (evita acumular señales en el semáforo).
        /// </summary>
        private void RequestRender()
        {
            if (Interlocked.Exchange(ref _renderPending, 1) == 0)
                _renderSignal.Release();
        }

        /// <summary>
        /// Loop de input que procesa teclado y rueda del mouse, delegando todo al <see cref="InputRouter"/>.
        /// </summary>
        /// <param name="token">Token de cancelación para detener el bucle.</param>
        private async Task ProcessInput(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var input = InputReader.ReadInput();
                    if (input.Type == InputEventType.None)
                    {
                        await Task.Delay(15, token);
                        continue;
                    }

                    // El router tiene los binds de LiveConsole y los del LineEdit.
                    _router.Handle(input);

                    await Task.Delay(7, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Construye y vuelca un frame completo: logs visibles, barra divisoria inteligente
        /// (con aviso de mensajes nuevos si corresponde) y el bloque de input multilínea.
        /// </summary>
        /// <param name="prompt">Prefijo a mostrar antes del input.</param>
        /// <param name="width">Ancho actual de la consola.</param>
        /// <param name="height">Alto actual de la consola.</param>
        private void RenderScreen(string prompt, int width, int height)
        {
            StringBuilder buffer = new StringBuilder(4096);
            buffer.Append("\x1b[H"); // Mover el cursor arriba a la izquierda

            string currentInput;
            int currentScroll;

            // Extraemos una copia ultrarrápida del estado
            lock (_stateLock)
            {
                currentInput = _inputBuffer;

                // --- CÁLCULO ELÁSTICO DE FILAS EXACTO ---
                List<string> wrappedInputLines = new List<string>();
                string[] inputLines = currentInput.Split('\n');
                string[] promptParts = prompt.Split('\n');
                string promptLastLine = promptParts[^1];

                // 1. Sumar las líneas del prompt anteriores al último \n
                int promptTopRows = 0;
                for (int i = 0; i < promptParts.Length - 1; i++)
                    promptTopRows += Math.Max(1, promptParts[i].CountPhysicalLines(width));

                // Las siguientes líneas se wrappean solas
                for (int i = 1; i < inputLines.Length; i++)
                {
                    var wrapped = inputLines[i].WrapText(width);
                    if (wrapped.Count == 0) wrapped.Add("");
                    wrappedInputLines.AddRange(wrapped);
                }

                // 2. La primera línea del input se concatena con la última línea del prompt
                var firstLineWrapped = (promptLastLine + inputLines[0]).WrapText(width);
                if (firstLineWrapped.Count == 0) firstLineWrapped.Add("");
                wrappedInputLines.AddRange(firstLineWrapped);

                int inputRows = wrappedInputLines.Count + promptTopRows;
                int logRowsAvailable = Math.Max(1, height - inputRows - 1);

                // Limitar el scroll al tope máximo
                int totalLogLines = 0;
                foreach (var log in _logs)
                    totalLogLines += log.CountPhysicalLines(width);

                _maxScroll = Math.Max(0, totalLogLines - logRowsAvailable);
                if (_scrollOffset > _maxScroll)
                    _scrollOffset = _maxScroll;

                // Si bajó al presente, apagamos la alerta
                if (_scrollOffset == 0)
                    _hasNewLogsBelow = false;

                currentScroll = _scrollOffset;
                var visibleLines = GetVisibleLogLines(width, logRowsAvailable, currentScroll);

                // 1. Dibujamos los logs
                foreach (var line in visibleLines)
                    buffer.Append(line).Append("\x1b[K\n");

                // 2. Dibujamos la barra divisoria inteligente
                string dividerLine = new string(ConsoleGlyphs.Horizontal, width);

                if (currentScroll > 0 && _hasNewLogsBelow)
                {
                    string alertText = " [ ↓ MENSAJES NUEVOS ABAJO ] ";
                    if (width > alertText.Length + 6)
                    {
                        int sideLength = (width - alertText.Length) / 2;
                        string sideBar = new string(ConsoleGlyphs.Horizontal, sideLength);
                        buffer.Append($"{ThemeColors.Dim}{sideBar}{ThemeColors.Warning}{AnsiColor.Bold}{alertText}{ThemeColors.Reset}{ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, width - sideLength - alertText.Length)}{ThemeColors.Reset}\x1b[K\n");
                    }
                    else
                        buffer.Append($"{ThemeColors.Warning}{dividerLine}{ThemeColors.Reset}\x1b[K\n");
                }
                else if (currentScroll > 0)
                {
                    string historyText = $" [ MODO HISTORIAL: -{currentScroll} LÍNEAS ] ";
                    if (width > historyText.Length + 6)
                    {
                        int sideLength = (width - historyText.Length) / 2;
                        string sideBar = new string(ConsoleGlyphs.Horizontal, sideLength);
                        buffer.Append($"{ThemeColors.Dim}{sideBar}{historyText}{new string(ConsoleGlyphs.Horizontal, width - sideLength - historyText.Length)}{ThemeColors.Reset}\x1b[K\n");
                    }
                    else
                        buffer.Append($"{ThemeColors.Dim}{dividerLine}{ThemeColors.Reset}\x1b[K\n");
                }
                else
                    buffer.Append($"{ThemeColors.Dim}{dividerLine}{ThemeColors.Reset}\x1b[K\n");

                // 3. Prompt + input (Se imprime tal cual)
                buffer.Append("\x1b[K");
                buffer.Append(prompt);
                buffer.Append(currentInput);
                buffer.Append("\x1b[J"); // Limpiar cualquier basura que quede debajo

                // 4. Posicionar el cursor usando coordenadas ABSOLUTAS (Matemática 2D)
                int promptLastLineLen = promptLastLine.GetVisualLength();
                int absoluteVisualPos = promptLastLineLen + _cursorPos;

                int targetLine = 0;
                int targetCol = 1; // ANSI es 1-indexed
                int remaining = absoluteVisualPos;
                bool found = false;

                for (int i = 0; i < wrappedInputLines.Count; i++)
                {
                    int lineLen = wrappedInputLines[i].GetVisualLength();

                    if (remaining < lineLen)
                    {
                        targetLine = i;
                        targetCol = remaining + 1;
                        found = true;
                        break;
                    }
                    else if (remaining == lineLen)
                    {
                        // El cursor cae EXACTAMENTE al final de una línea
                        if (lineLen == width)
                        {
                            // Auto-wrap: salta a la siguiente línea, columna 1
                            targetLine = i + 1;
                            targetCol = 1;
                        }
                        else
                        {
                            // Salto de línea explícito (\n): se queda al final de esta línea
                            targetLine = i;
                            targetCol = remaining + 1;
                        }
                        found = true;
                        break;
                    }
                    remaining -= lineLen;
                }

                // Seguridad: si se pasa del final del texto por algún desajuste
                if (!found)
                {
                    targetLine = wrappedInputLines.Count - 1;
                    targetCol = wrappedInputLines[^1].GetVisualLength() + 1;
                }

                // Calcular fila física real en la pantalla
                int inputStartRow = logRowsAvailable + 2; // +1 por divider, +1 por base-1 de ANSI
                int cursorRow = inputStartRow + targetLine + promptTopRows;

                // FIX: Auto-wrap en el borde inferior de la pantalla
                if (cursorRow > height)
                {
                    cursorRow = height;
                    targetCol = 1;
                }

                buffer.Append($"\x1b[{cursorRow};{targetCol}H");
            }

            Console.Write(buffer.ToString());
        }

        /// <summary>
        /// Devuelve las líneas físicas visibles a partir del historial, considerando el scrollOffset.
        /// Recorre el historial de abajo hacia arriba envolviendo texto a demanda.
        /// </summary>
        /// <param name="width">Ancho de consola para el wrapping.</param>
        /// <param name="maxLines">Cantidad máxima de líneas a devolver.</param>
        /// <param name="scrollOffset">Líneas a saltar desde el fondo (0 = pegado al presente).</param>
        /// <returns>Lista de líneas a mostrar en orden cronológico (la más reciente abajo).</returns>
        private List<string> GetVisibleLogLines(int width, int maxLines, int scrollOffset)
        {
            var result = new List<string>();
            int currentLogIndex = _logs.Count - 1;
            int linesSkipped = 0;

            // Retrocedemos en el historial envolviendo el texto a demanda
            while (currentLogIndex >= 0 && result.Count < maxLines)
            {
                string log = _logs[currentLogIndex];
                var wrappedLines = log.WrapText(width);

                // Los leemos de abajo hacia arriba para rellenar la pantalla
                for (int i = wrappedLines.Count - 1; i >= 0; i--)
                {
                    if (linesSkipped < scrollOffset)
                        linesSkipped++;
                    else if (result.Count < maxLines)
                        result.Add(wrappedLines[i]);
                }
                currentLogIndex--;
            }

            // Rellenamos el espacio vacío superior si hay muy pocos logs
            while (result.Count < maxLines)
                result.Add("");

            result.Reverse(); // Invertimos para que queden en orden cronológico correcto
            return result;
        }
    }
}
