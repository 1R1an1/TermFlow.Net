/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
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
                _currentModifiers = currentKey.Modifiers;

                _router.Handle(evt);
            }

            return _isFinished ? _buffer.ToString() : null;
        }
    }
}
