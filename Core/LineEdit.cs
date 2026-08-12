/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.Core
{
    /// <summary>
    /// Editor de línea interactivo para entrada de texto en consola.
    /// Administra el buffer, la posición del cursor y los eventos de teclado.
    /// </summary>
    internal sealed class LineEdit
    {
        private readonly string _lastPromptLine;
        private readonly int _promptLength;
        private readonly StringBuilder _buffer = new();
        private int _cursorPos = 0;
        private bool _isFinished = false;
        private ConsoleModifiers _currentModifiers;
        private readonly InputRouter _router;

        /// <summary>
        /// Handler usado para actualizar la representación visual del editor.
        /// </summary>
        private Action<string, int, bool> _renderHandler;

        /// <summary>
        /// Obtiene la última línea visible del prompt.
        /// </summary>
        public string LastPromptLine => _lastPromptLine;

        /// <summary>
        /// Obtiene la longitud visual del prompt.
        /// </summary>
        public int PromptLength => _promptLength;

        /// <summary>
        /// Inicializa una nueva instancia de <see cref="LineEdit"/>.
        /// </summary>
        /// <param name="prompt">Texto mostrado antes de la entrada.</param>
        /// <param name="router">
        /// Router opcional para inyectar bindings personalizados.
        /// </param>
        public LineEdit(string prompt, InputRouter router = null)
        {
            _lastPromptLine = prompt.Contains('\n') ? prompt.Substring(prompt.LastIndexOf('\n') + 1) : prompt;
            _promptLength = _lastPromptLine.GetVisualLength();

            _router = router ?? new InputRouter(false);
            SetupRouter();
        }

        /// <summary>
        /// Configura los bindings internos del editor.
        /// </summary>
        private void SetupRouter()
        {
            _router.BindConfirm(() =>
            {
                _isFinished = true;
                _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
            })

            .BeforeKey((cki) =>
                _currentModifiers = cki.Modifiers
            )

            .Bind("", "", () =>
            {
                if ((_currentModifiers & ConsoleModifiers.Control) != 0)
                {
                    int i = _cursorPos - 1;
                    while (i > 0 && (char.IsWhiteSpace(_buffer[i]) || _buffer[i] == '/' || _buffer[i] == '\\' || _buffer[i] == '.' || _buffer[i] == '-')) i--;
                    while (i > 0 && !(_buffer[i - 1] == ' ' || _buffer[i - 1] == '/' || _buffer[i - 1] == '\\' || _buffer[i - 1] == '.' || _buffer[i - 1] == '-')) i--;
                    _cursorPos = Math.Max(0, i);
                }
                else _cursorPos = Math.Max(0, _cursorPos - 1);

                _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
            }, ConsoleKey.LeftArrow)

            .Bind("", "", () =>
            {
                if ((_currentModifiers & ConsoleModifiers.Control) != 0)
                {
                    int i = _cursorPos;
                    while (i < _buffer.Length && (char.IsWhiteSpace(_buffer[i]) || _buffer[i] == '/' || _buffer[i] == '\\' || _buffer[i] == '.' || _buffer[i] == '-')) i++;
                    while (i < _buffer.Length && !(_buffer[i] == ' ' || _buffer[i] == '/' || _buffer[i] == '\\' || _buffer[i] == '.' || _buffer[i] == '-')) i++;
                    _cursorPos = i;
                }
                else _cursorPos = Math.Min(_buffer.Length, _cursorPos + 1);

                _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
            }, ConsoleKey.RightArrow)

            .Bind("", "", () =>
            {
                _cursorPos = 0;
                _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
            }, ConsoleKey.Home)

            .Bind("", "", () =>
            {
                _cursorPos = _buffer.Length;
                _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
            }, ConsoleKey.End)

            .Bind("", "", () =>
            {
                if (_cursorPos > 0) { _buffer.Remove(_cursorPos - 1, 1); _cursorPos--; }
                _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
            }, ConsoleKey.Backspace)

            .Bind("", "", () =>
            {
                if (_cursorPos < _buffer.Length) { _buffer.Remove(_cursorPos, 1); }
                _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
            }, ConsoleKey.Delete)

            .BindUnhandled("", "", (k) =>
            {
                if (!char.IsControl(k.KeyChar))
                {
                    _buffer.Insert(_cursorPos, k.KeyChar);
                    _cursorPos++;
                    _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
                }
            });
        }

        /// <summary>
        /// Asigna el callback utilizado para renderizar cambios.
        /// </summary>
        /// <param name="handler">
        /// Callback que recibe el texto, posición del cursor y estado final.
        /// </param>
        public void SetRenderHandler(Action<string, int, bool> handler)
        {
            _renderHandler = handler;
            _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
        }

        /// <summary>
        /// Limpia el contenido actual y reinicia el estado del editor.
        /// </summary>
        public void Clear()
        {
            _buffer.Clear();
            _cursorPos = 0;
            _isFinished = false;
            _renderHandler?.Invoke(_buffer.ToString(), _cursorPos, _isFinished);
        }

        /// <summary>
        /// Ejecuta el editor esperando entrada del usuario.
        /// </summary>
        /// <param name="renderHandler">Callback utilizado para actualizar la interfaz.</param>
        /// <param name="token">Token de cancelación.</param>
        /// <returns>El texto ingresado o <c>null</c> si fue cancelado.</returns>
        public async Task<string> ExecuteAsync(Action<string, int, bool> renderHandler, CancellationToken token = default)
        {
            SetRenderHandler(renderHandler);

            while (!_isFinished && !token.IsCancellationRequested)
            {
                var currentKey = LivePanel.IsActive ? await LivePanel.WaitForKeyAsync(token) : InputReader.ReadInput().KeyInfo;
                var evt = new ConsoleInputEvent { Type = InputEventType.Key, KeyInfo = currentKey };
                _router.Handle(evt);
            }

            return _isFinished ? _buffer.ToString() : null;
        }

        /// <summary>
        /// Mapea una posición 1D absoluta del buffer de texto a coordenadas 2D (fila y columna)
        /// basándose en las líneas físicas generadas por el envoltorio (wrap) de la terminal.
        /// Considera los saltos de línea explícitos (\n) y el auto-wrap de la terminal (borde exacto).
        /// </summary>
        /// <param name="wrappedLines">La lista de líneas físicas resultante de aplicar <c>WrapText</c> al texto completo. No debe ser nula.</param>
        /// <param name="absolutePos">La posición absoluta del cursor (0-indexed) contando desde el inicio lógico del texto.</param>
        /// <param name="width">El ancho actual de la consola en columnas, utilizado para detectar si una línea terminó por un auto-wrap de la terminal (su longitud iguala el ancho).</param>
        /// <returns>
        /// Una tupla <c>(int targetLine, int targetCol)</c> donde:
        /// <c>targetLine</c> es el índice de la línea (0-indexed) dentro de <paramref name="wrappedLines"/>.
        /// <c>targetCol</c> es la columna física (1-indexed, lista para ANSI) donde debe situarse el cursor.
        /// </returns>
        public static (int targetLine, int targetCol) MapPositionTo2D(List<string> wrappedLines, int absolutePos, int width)
        {
            if (wrappedLines == null || wrappedLines.Count == 0)
                return (0, 1);

            int targetLine = 0;
            int targetCol = 1; // ANSI es 1-indexed
            int remaining = absolutePos;
            bool found = false;

            for (int i = 0; i < wrappedLines.Count; i++)
            {
                int lineLen = wrappedLines[i].GetVisualLength();

                if (remaining < lineLen)
                {
                    targetLine = i;
                    targetCol = remaining + 1;
                    found = true;
                    break;
                }
                else if (remaining == lineLen)
                {
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

            // Seguridad: si se pasa del final
            if (!found)
            {
                targetLine = wrappedLines.Count - 1;
                targetCol = wrappedLines[^1].GetVisualLength() + 1;
            }

            return (targetLine, targetCol);
        }
    }
}
