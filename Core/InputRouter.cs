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
        private Action _onScrollUp;
        private Action _onScrollDown;

        // Estructura interna para encapsular y precalcular la agrupación de la UI
        private class FooterGroup
        {
            public string Description { get; }
            public List<string> KeyLabels { get; } = new();
            public string CachedKeysLabel { get; set; } = string.Empty;

            public FooterGroup(string description)
            {
                Description = description;
            }
        }

        private readonly List<FooterGroup> _footerGroups = new();
        private readonly Dictionary<string, FooterGroup> _groupMap = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Procesa y agrupa las etiquetas visuales de las teclas en base al nombre de la acción.
        /// </summary>
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
        public InputRouter Bind(ConsoleKey key, string keyDisplay, string actionDisplay, Action handler)
        {
            _keyHandlers[key] = handler;
            RegisterFooterLabel(keyDisplay, actionDisplay);
            return this;
        }

        /// <summary>
        /// Vincula un carácter exacto (sensible a mayúsculas) a una acción, registrando su comportamiento en el footer.
        /// </summary>
        public InputRouter BindChar(char keyChar, string keyDisplay, string actionDisplay, Action handler)
        {
            _charHandlers[keyChar] = handler;
            RegisterFooterLabel(keyDisplay, actionDisplay);
            return this;
        }

        /// <summary>
        /// Vincula las interacciones físicas de la rueda del ratón (No se imprimen en el footer).
        /// </summary>
        public InputRouter BindScroll(Action onUp, Action onDown)
        {
            _onScrollUp = onUp;
            _onScrollDown = onDown;
            return this;
        }

        // --- PRESETS SEMÁNTICOS (CON DESCRIPCIÓN MODIFICABLE) ---

        public InputRouter BindNavigate(Action onUp, Action onDown, string description = "navegar")
        {
            Bind(ConsoleKey.UpArrow, "↑↓", description, onUp);
            Bind(ConsoleKey.DownArrow, "", description, onDown); // El string vacío evita duplicar flechas en el Join
            BindChar('j', "j/k", description, onDown);
            BindChar('k', "", description, onUp);
            return this;
        }

        public InputRouter BindSelect(Action onSelect, string description = "marcar")
        {
            return Bind(ConsoleKey.Spacebar, "Space", description, onSelect);
        }

        public InputRouter BindConfirm(Action onConfirm, string description = "confirmar")
        {
            Bind(ConsoleKey.Enter, "Enter", description, onConfirm);
            BindChar('c', "c", description, onConfirm);
            return this;
        }

        public InputRouter BindCancel(Action onCancel, string description = "salir")
        {
            Bind(ConsoleKey.Escape, "Esc", description, onCancel);
            BindChar('q', "q", description, onCancel);
            return this;
        }

        public InputRouter BindUnhandled(string keyDisplay, string actionDisplay, Action<ConsoleKeyInfo> handler)
        {
            _unhandledHandler = handler;
            RegisterFooterLabel(keyDisplay, actionDisplay);
            return this;
        }

        // --- PROCESADOR INTERNO DE INPUT ---

        /// <summary>
        /// Intercepta el evento nativo de TermFlow y dispara el delegado correspondiente con máxima velocidad de resolución.
        /// </summary>
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