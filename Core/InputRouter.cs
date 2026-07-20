/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Text;

namespace TermFlow.Core
{
    /// <summary>
    /// Motor de enrutamiento y unificación de entrada para TermFlow.
    /// Registra acciones físicas, agrupa semánticamente el Footer mediante "/" y procesa eventos sin allocs.
    /// </summary>
    internal sealed class InputRouter
    {
        private readonly Dictionary<ConsoleKey, Action> _keyHandlers = new();
        private readonly Dictionary<char, Action> _charHandlers = new();
        private Action<ConsoleKeyInfo> _unhandledHandler;
        private bool _enableDefaultChars;
        private Action _onScrollUp;
        private Action _onScrollDown;

        // Estructura interna para encapsular y precalcular la agrupación de la UI
        private class FooterGroup
        {
            /// <summary>Descripción de la acción a mostrar (ej. "navegar").</summary>
            public string Description { get; }
            /// <summary>Lista de etiquetas visuales de teclas asociadas (ej. "↑", "j").</summary>
            public List<string> KeyLabels { get; } = new();
            /// <summary>Cadena precalculada con todas las teclas unidas por "/" para evitar allocs en el render.</summary>
            public string CachedKeysLabel { get; set; } = string.Empty;

            public FooterGroup(string description)
            {
                Description = description;
            }
        }

        private readonly List<FooterGroup> _footerGroups = new();
        private readonly Dictionary<string, FooterGroup> _groupMap = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Crea una nueva instancia del enrutador.
        /// </summary>
        /// <param name="enableDefaultChars">Si <c>true</c>, los presets (navegar, confirmar, etc.) vinculan también atajos de letra (j/k/c/q).</param>
        public InputRouter(bool enableDefaultChars = true)
            => _enableDefaultChars = enableDefaultChars;

        /// <summary>
        /// Procesa y agrupa las etiquetas visuales de las teclas en base al nombre de la acción.
        /// </summary>
        /// <param name="keyDisplay">Etiqueta visual de la tecla (ej. "Enter").</param>
        /// <param name="actionDisplay">Descripción semántica de la acción (ej. "confirmar").</param>
        private void RegisterFooterLabel(string keyDisplay, string actionDisplay)
        {
            if (string.IsNullOrEmpty(actionDisplay) || string.IsNullOrEmpty(keyDisplay))
                return;

            if (!_groupMap.TryGetValue(actionDisplay, out var group))
            {
                group = new FooterGroup(actionDisplay);
                _footerGroups.Add(group);
                _groupMap[actionDisplay] = group;
            }

            if (!group.KeyLabels.Contains(keyDisplay))
            {
                group.KeyLabels.Add(keyDisplay);
                // Precalculamos la unión aquí para no fragmentar memoria en el Render-Loop
                group.CachedKeysLabel = string.Join("/", group.KeyLabels);
            }
        }

        // --- BINDS BÁSICOS CONFIGURABLES ---

        /// <summary>
        /// Vincula una tecla física (ConsoleKey) a una acción, registrando su comportamiento en el footer.
        /// </summary>
        /// <param name="keyDisplay">Etiqueta visual de la tecla (ej. "↑↓").</param>
        /// <param name="actionDisplay">Descripción de la acción a mostrar en el footer.</param>
        /// <param name="handler">Delegado a ejecutar cuando se presione la tecla.</param>
        /// <param name="key">Tecla principal a vincular.</param>
        /// <param name="keys">Teclas adicionales equivalentes (alias).</param>
        /// <returns>La misma instancia para encadenar binds en estilo fluent.</returns>
        public InputRouter Bind(string keyDisplay, string actionDisplay, Action handler, ConsoleKey key, params ConsoleKey[] keys)
        {
            _keyHandlers[key] = handler;
            foreach (var kei in keys)
                _keyHandlers[kei] = handler;
            RegisterFooterLabel(keyDisplay, actionDisplay);
            return this;
        }

        /// <summary>
        /// Vincula un carácter exacto (sensible a mayúsculas) a una acción, registrando su comportamiento en el footer.
        /// </summary>
        /// <param name="keyDisplay">Etiqueta visual del carácter (ej. "g/G").</param>
        /// <param name="actionDisplay">Descripción de la acción a mostrar en el footer.</param>
        /// <param name="handler">Delegado a ejecutar cuando se presione el carácter.</param>
        /// <param name="keyChar">Carácter principal a vincular.</param>
        /// <param name="keysChar">Caracteres adicionales equivalentes.</param>
        /// <returns>La misma instancia para encadenar binds.</returns>
        public InputRouter BindChar(string keyDisplay, string actionDisplay, Action handler, char keyChar, params char[] keysChar)
        {
            _charHandlers[keyChar] = handler;
            foreach (var keiChar in keysChar)
                _charHandlers[keiChar] = handler;
            RegisterFooterLabel(keyDisplay, actionDisplay);
            return this;
        }

        /// <summary>
        /// Vincula las interacciones físicas de la rueda del ratón (No se imprimen en el footer).
        /// </summary>
        /// <param name="onUp">Acción al hacer scroll hacia arriba.</param>
        /// <param name="onDown">Acción al hacer scroll hacia abajo.</param>
        /// <returns>La misma instancia para encadenar binds.</returns>
        public InputRouter BindScroll(Action onUp, Action onDown)
        {
            _onScrollUp = onUp;
            _onScrollDown = onDown;
            return this;
        }

        // --- PRESETS SEMÁNTICOS (CON DESCRIPCIÓN MODIFICABLE) ---

        /// <summary>
        /// Vincula flechas y (opcionalmente) j/k para navegación vertical.
        /// </summary>
        /// <param name="onUp">Acción al navegar hacia arriba.</param>
        /// <param name="onDown">Acción al navegar hacia abajo.</param>
        /// <param name="description">Etiqueta a mostrar en el footer (por defecto "navegar").</param>
        /// <returns>La misma instancia para encadenar binds.</returns>
        public InputRouter BindNavigate(Action onUp, Action onDown, string description = "navegar")
        {
            Bind("↑↓", description, onUp, ConsoleKey.UpArrow);
            Bind("", description, onDown, ConsoleKey.DownArrow); // El string vacío evita duplicar flechas en el Join
            if (!_enableDefaultChars) return this;
            Bind("j", description, onDown, ConsoleKey.J);
            Bind("k", description, onUp, ConsoleKey.K);
            return this;
        }

        /// <summary>
        /// Vincula la barra espaciadora para marcar/desmarcar elementos.
        /// </summary>
        /// <param name="onSelect">Acción al presionar espacio.</param>
        /// <param name="description">Etiqueta del footer (por defecto "marcar").</param>
        /// <returns>La misma instancia para encadenar binds.</returns>
        public InputRouter BindSelect(Action onSelect, string description = "marcar")
        {
            return Bind("Space", description, onSelect, ConsoleKey.Spacebar);
        }

        /// <summary>
        /// Vincula Enter (y opcionalmente 'c') como acción de confirmación.
        /// </summary>
        /// <param name="onConfirm">Acción al confirmar.</param>
        /// <param name="description">Etiqueta del footer (por defecto "confirmar").</param>
        /// <returns>La misma instancia para encadenar binds.</returns>
        public InputRouter BindConfirm(Action onConfirm, string description = "confirmar")
        {
            Bind("Enter", description, onConfirm, ConsoleKey.Enter);
            if (!_enableDefaultChars) return this;
            Bind("c", description, onConfirm, ConsoleKey.C);
            return this;
        }

        /// <summary>
        /// Vincula Escape (y opcionalmente 'q') como acción de salida/cancelación.
        /// </summary>
        /// <param name="onCancel">Acción al cancelar.</param>
        /// <param name="description">Etiqueta del footer (por defecto "salir").</param>
        /// <returns>La misma instancia para encadenar binds.</returns>
        public InputRouter BindCancel(Action onCancel, string description = "salir")
        {
            Bind("Esc", description, onCancel, ConsoleKey.Escape);
            if (!_enableDefaultChars) return this;
            Bind("q", description, onCancel, ConsoleKey.Q);
            return this;
        }

        /// <summary>
        /// Registra un handler fallback para teclas no vinculadas explícitamente.
        /// Útil para capturar texto libre (escritura de usuario).
        /// </summary>
        /// <param name="keyDisplay">Etiqueta visual a mostrar en el footer (ej. "Letras").</param>
        /// <param name="actionDisplay">Descripción semántica (ej. "filtrar").</param>
        /// <param name="handler">Callback que recibe la tecla cruda no manejada.</param>
        /// <returns>La misma instancia para encadenar binds.</returns>
        public InputRouter BindUnhandled(string keyDisplay, string actionDisplay, Action<ConsoleKeyInfo> handler)
        {
            _unhandledHandler = handler;
            RegisterFooterLabel(keyDisplay, actionDisplay);
            return this;
        }

        // --- PROCESADOR INTERNO DE INPUT ---

        /// <summary>
        /// Intercepta el evento nativo de TermFlow y dispara el delegado correspondiente con máxima velocidad de resolución.
        /// Prioridad: caracteres exactos &gt; teclas virtuales &gt; fallback.
        /// </summary>
        /// <param name="evt">Evento crudo producido por <see cref="InputReader.ReadInput"/>.</param>
        public void Handle(ConsoleInputEvent evt)
        {
            if (evt.Type == InputEventType.ScrollUp) { _onScrollUp?.Invoke(); return; }
            if (evt.Type == InputEventType.ScrollDown) { _onScrollDown?.Invoke(); return; }

            if (evt.Type == InputEventType.Key)
            {
                // 1. Prioridad: Caracteres exactos (Captura sutil de navegación Vim o letras directas)
                if (_charHandlers.TryGetValue(evt.KeyInfo.KeyChar, out var charAction))
                {
                    charAction.Invoke();
                    return;
                }

                // 2. Teclas virtuales genéricas de la consola
                if (_keyHandlers.TryGetValue(evt.KeyInfo.Key, out var keyAction))
                {
                    keyAction.Invoke();
                    return;
                }

                // Si ninguna tecla coincidió, mandamos el input crudo al fallback (ideal para escribir)
                _unhandledHandler?.Invoke(evt.KeyInfo);
            }
        }

        // --- RENDERIZADOR ESTRUCTURAL DE ALTA VELOCIDAD ---

        /// <summary>
        /// Inyecta las instrucciones formateadas y agrupadas directamente en el buffer central del componente.
        /// </summary>
        /// <param name="buffer">StringBuilder donde se appendizará el footer renderizado.</param>
        public void RenderFooter(StringBuilder buffer)
        {
            if (_footerGroups.Count == 0) return;

            buffer.Append("  ");
            for (int i = 0; i < _footerGroups.Count; i++)
            {
                var group = _footerGroups[i];

                buffer.Append(ThemeColors.Warning)
                      .Append(group.CachedKeysLabel)
                      .Append(ThemeColors.Reset)
                      .Append(' ')
                      .Append(group.Description);

                if (i < _footerGroups.Count - 1)
                    buffer.Append("   "); // Espaciado limpio entre bloques de comandos
            }
        }
    }
}
