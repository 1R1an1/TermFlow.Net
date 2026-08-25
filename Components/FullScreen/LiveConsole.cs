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

        private TermCanvas _canvas = null;

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

            _canvas = new TermCanvas(true, false, 100, (_, _) => RequestRender());

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
                    int width = Console.WindowWidth;
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
        /// Detiene la sesión activa de LiveConsole.
        /// </summary>
        public void Stop() => _internalCts?.Cancel();

        /// <summary>
        /// Levanta la interfaz de chat interactiva.
        /// </summary>
        /// <param name="prompt">El texto antes del cursor (ej. ">>> ")</param>
        /// <param name="onInputSubmitted">Callback que se ejecuta cuando el usuario presiona Enter</param>
        /// <param name="token">Token para cancelar la ejecución.</param>
        public async Task RunAsync(string prompt, Func<string, Task> onInputSubmitted, CancellationToken token = default)
        {
            Engine.EnterFullScreen(); // Nos adueñamos de la pantalla y activamos el mouse
            _canvas.CursorVisible = true;

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
                while (!_internalCts.Token.IsCancellationRequested)
                {
                    await _renderSignal.WaitAsync(_internalCts.Token);
                    Interlocked.Exchange(ref _renderPending, 0);

                    RenderScreen(prompt);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _internalCts.Cancel();
                _internalCts.Dispose();
                await inputTask; // Esperamos que cierre el lector
                Engine.ExitFullScreen(); // Devolvemos la consola a su estado natural
                _canvas.Clear();
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
        /// Construye y vuelca un frame completo usando TermCanvas: logs visibles, barra divisoria 
        /// inteligente (con aviso de mensajes nuevos si corresponde) y el bloque de input multilínea.
        /// </summary>
        /// <param name="prompt">Prefijo a mostrar antes del input.</param>
        private void RenderScreen(string prompt)
        {
            _canvas.Resize(Console.WindowWidth, Console.WindowHeight);
            int height = _canvas.Height, width = _canvas.Width;

            int currentScroll = 0, absoluteVisualPos = 0;
            string currentInput;
            lock (_stateLock) currentInput = _inputBuffer;

            string[] inputLines = currentInput.Split('\n');
            string[] promptParts = prompt.Split('\n');
            string promptLastLine = promptParts[^1];

            // --- 1. MATEMÁTICA 2D DEL INPUT ---
            int promptTopRows = 0;
            for (int i = 0; i < promptParts.Length - 1; i++)
                promptTopRows += Math.Max(1, promptParts[i].CountPhysicalLines(width));

            var wrappedInputLines = new List<string>();
            for (int i = 1; i < inputLines.Length; i++)
            {
                var w = inputLines[i].WrapText(width);
                wrappedInputLines.AddRange(w.Count == 0 ? new[] { "" } : w);
            }

            var firstLineWrapped = (promptLastLine + inputLines[0]).WrapText(width);
            wrappedInputLines.AddRange(firstLineWrapped.Count == 0 ? new[] { "" } : firstLineWrapped);

            // FIX: Si la última línea ocupa exactamente el ancho, añadir salto físico
            if (wrappedInputLines[^1].GetVisualLength() == width)
                wrappedInputLines.Add("");

            int inputRows = wrappedInputLines.Count + promptTopRows;
            int logRowsAvailable = Math.Max(1, height - inputRows - 1);

            // --- 2. ESTADO Y SCROLL ---
            int totalLogLines = 0;
            lock (_stateLock)
            {
                foreach (var log in _logs)
                    totalLogLines += log.CountPhysicalLines(width);

                _maxScroll = Math.Max(0, totalLogLines - logRowsAvailable);
                if (_scrollOffset > _maxScroll) _scrollOffset = _maxScroll;
                if (_scrollOffset == 0) _hasNewLogsBelow = false;

                currentScroll = _scrollOffset;
                absoluteVisualPos = promptLastLine.GetVisualLength() + _cursorPos;
            }

            // --- 3. DIBUJAR DE ABAJO HACIA ARRIBA ---
            int inputStartY = height - inputRows;
            int dividerY = inputStartY - 1;
            int currentY = dividerY - 1;
            int linesSkipped = 0;

            // A. LOGS
            lock (_stateLock)
            {
                for (int i = _logs.Count - 1; i >= 0 && currentY >= 0; i--)
                {
                    var wrappedLines = _logs[i].WrapText(width);
                    for (int j = wrappedLines.Count - 1; j >= 0; j--)
                    {
                        if (currentY < 0) break;
                        if (linesSkipped < currentScroll)
                        {
                            linesSkipped++;
                            continue;
                        }
                        _canvas.WriteAtAndClear(0, currentY, wrappedLines[j]);
                        currentY--;
                    }
                }
            }
            if (currentY >= 0) _canvas.ClearArea(0, 0, width - 1, currentY);

            // B. BARRA DIVISORIA INTELIGENTE
            string alertText = null, alertColor = ThemeColors.Dim;
            if (currentScroll > 0 && _hasNewLogsBelow)
            {
                alertText = " [ ↓ MENSAJES NUEVOS ABAJO ] ";
                alertColor = $"{ThemeColors.Warning}{AnsiColor.Bold}";
            }
            else if (currentScroll > 0)
                alertText = $" [ MODO HISTORIAL: -{currentScroll} LÍNEAS ] ";

            if (alertText != null && width > alertText.Length + 6)
            {
                int sideLen = (width - alertText.Length) / 2;
                _canvas.WriteAtAndClear(0, dividerY, $"{ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, sideLen)}{alertColor}{alertText}{ThemeColors.Reset}{ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, width - sideLen - alertText.Length)}{ThemeColors.Reset}");
            }
            else
            {
                string color = (currentScroll > 0 && _hasNewLogsBelow) ? ThemeColors.Warning : ThemeColors.Dim;
                _canvas.WriteAtAndClear(0, dividerY, $"{color}{new string(ConsoleGlyphs.Horizontal, width)}{ThemeColors.Reset}");
            }

            // C. INPUT
            int y = inputStartY;
            for (int i = 0; i < promptParts.Length - 1; i++)
                foreach (var w in promptParts[i].WrapText(width)) _canvas.WriteAtAndClear(0, y++, w);

            foreach (var w in wrappedInputLines) _canvas.WriteAtAndClear(0, y++, w);

            // D. CURSOR REAL
            var (targetLine, targetCol) = LineEdit.MapPositionTo2D(wrappedInputLines, absoluteVisualPos, width);
            int cursorRow = inputStartY + targetLine + promptTopRows;

            if (cursorRow >= height)
            {
                cursorRow = height - 1;
                targetCol = 1;
            }

            _canvas.CursorPos = (X: targetCol - 1, Y: cursorRow);
            _canvas.CursorVisible = true;
            _canvas.Flush();
        }
    }
}
