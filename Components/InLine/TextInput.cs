/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.Core;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    /// <summary>
    /// Utilidades para capturar entrada de texto del usuario (string, sí/no, presionar para continuar).
    /// Compatible con modo inline y con el panel dinámico <see cref="LivePanel"/>.
    /// </summary>
    public static class TextInput
    {
        /// <summary>
        /// Bool interno para prevenir la ejecución concurrente de múltiples inputs a la vez.
        /// </summary>
        private static volatile bool isInputRunning = false;

        /// <summary>
        /// Solicita una cadena de texto al usuario de manera asíncrona, permitiendo edición estilo terminal.
        /// </summary>
        /// <remarks>
        /// Permite mover el cursor con Flechas, Ctrl+Flechas (salto de palabras considerando espacios, '/', '\', '.' y '-'), Inicio y Fin.
        /// Soporta borrado con Backspace y Suprimir (Delete). 
        /// Integra el cursor de la terminal tanto en modo consola directa como en el <see cref="LivePanel"/>.
        /// </remarks>
        /// <param name="prompt">Texto a mostrar antes del cursor de entrada.</param>
        /// <param name="token">Token para cancelar la lectura.</param>
        /// <returns>Texto ingresado al presionar Enter, o <c>null</c> si fue cancelado.</returns>
        /// <exception cref="InvalidOperationException">Se lanza si ya hay una entrada de texto en curso.</exception>
        public static async Task<string> ReadStringAsync(string prompt, CancellationToken token = default)
        {
            if (isInputRunning) throw new InvalidOperationException("Ya hay un input corriendo");
            else isInputRunning = true;

            long? dynamicId = null;
            var editor = new LineEdit(prompt);
            int lastHeight = 0;
            int fullPromptVisualLength = prompt.Replace("\r", "").Replace("\n", "").GetVisualLength();
            bool previousEndedOnExactWidth = false;
            int lastCursorTargetLine = 0;

            if (LivePanel.IsActive)
            {
                dynamicId = LivePanel.AddDynamic(prompt);
                LivePanel.FocusEntryId = dynamicId;
                LivePanel.FocusVisualCol = fullPromptVisualLength;
            }
            else
            {
                Console.CursorVisible = true;
                Console.Write(prompt);
            }

            // Unificación del renderizado para no repetir código
            void Render(string text, int cursorPos, bool isFinished)
            {
                if (isFinished) return;

                if (LivePanel.IsActive)
                {
                    LivePanel.FocusVisualCol = fullPromptVisualLength + cursorPos;
                    LivePanel.UpdateLine(dynamicId.Value, prompt + text);
                }
                else
                {
                    int w = Math.Max(1, Console.WindowWidth);
                    int absPos = editor.PromptLength + cursorPos;
                    var lines = (editor.LastPromptLine + text).WrapText(w);
                    int totalLines = lines.Count;

                    int targetLine = absPos / w;
                    int targetCol = absPos % w;
                    if (targetCol == w) { targetLine++; targetCol = 0; }

                    // Recuperar cursor al fondo físico antes de limpiar
                    int lastBottom = lastHeight - 1 + (previousEndedOnExactWidth ? 1 : 0);
                    if (lastCursorTargetLine < lastBottom)
                        Console.Write($"\x1b[{lastBottom - lastCursorTargetLine}B");

                    // Limpieza relativa
                    int physLines = lastHeight + (previousEndedOnExactWidth ? 1 : 0);
                    if (physLines > 1) Console.Write($"\x1b[{physLines - 1}F");
                    Console.Write("\r\x1b[0J");

                    // Impresión
                    bool endsExact = lines.Count > 0 && lines[^1].Length == w;
                    Console.Write(string.Join("\n", lines));
                    if (endsExact) Console.Write("\n");

                    // Posicionar cursor
                    int currentPhys = totalLines + (endsExact ? 1 : 0);
                    int move = targetLine - (currentPhys - 1);
                    if (move < 0) Console.Write($"\x1b[{-move}A");
                    else if (move > 0) Console.Write($"\x1b[{move}B");

                    Console.Write('\r');
                    if (targetCol > 0) Console.Write($"\x1b[{targetCol}C");

                    // Guardar estado
                    lastHeight = totalLines;
                    previousEndedOnExactWidth = endsExact;
                    lastCursorTargetLine = targetLine;
                }
            }

            try
            {
                if (LivePanel.IsActive) LivePanel.ClearKeysQueue();

                string result = await editor.ExecuteAsync(Render, token);

                if (result != null)
                {
                    if (LivePanel.IsActive) { LivePanel.FocusEntryId = null; LivePanel.UpdateLine(dynamicId.Value, prompt + result); }
                    else Console.WriteLine();
                }
                return result;
            }
            finally
            {
                if (LivePanel.IsActive) LivePanel.FocusEntryId = null;
                else Console.CursorVisible = false;
                isInputRunning = false;
            }
        }

        /// <summary>
        /// Realiza una pregunta de sí/no al usuario. Acepta únicamente Y o N y se confirma con Enter.
        /// </summary>
        /// <remarks>
        /// Permite borrar la letra ingresada con Backspace. Se cancela con Escape. 
        /// Respeta el cursor de la terminal y el <see cref="LivePanel"/>.
        /// </remarks>
        /// <param name="prompt">Pregunta a mostrar (se le agregará automáticamente " [y/n] ").</param>
        /// <param name="token">Token de cancelación opcional.</param>
        /// <returns><c>true</c> si presiona Y y luego Enter; <c>false</c> si presiona N y luego Enter, o Escape.</returns>
        /// <exception cref="InvalidOperationException">Se lanza si ya hay una entrada de texto en curso.</exception>
        public static async Task<bool> AskAsync(string prompt, CancellationToken token = default)
        {
            if (isInputRunning) throw new InvalidOperationException("Ya hay un input corriendo");
            else isInputRunning = true;

            string fullPrompt = $"{prompt} {AnsiColor.Cyan}[y/n]{ThemeColors.Reset} ";
            long? dynamicId = null;
            char currentChar = '\0';
            int promptLength = fullPrompt.GetVisualLength();
            bool? response = null;
            bool finished = false;

            if (LivePanel.IsActive)
            {
                dynamicId = LivePanel.AddDynamic(fullPrompt);
                LivePanel.FocusEntryId = dynamicId;
                LivePanel.FocusVisualCol = promptLength;
            }
            else
            {
                Console.CursorVisible = true;
                Console.Write(fullPrompt);
            }

            void Render()
            {
                if (LivePanel.IsActive)
                {
                    LivePanel.FocusVisualCol = promptLength + (currentChar != '\0' ? 1 : 0);
                    LivePanel.UpdateLine(dynamicId.Value, fullPrompt + currentChar);
                }
                else
                {
                    Console.SetCursorPosition(promptLength, Console.CursorTop);
                    Console.Write(currentChar + " ");
                    Console.SetCursorPosition(promptLength + (currentChar != '\0' ? 1 : 0), Console.CursorTop);
                }
            }

            ConsoleKeyInfo currentKey = default;
            var router = new InputRouter(false);

            router.BindConfirm(() =>
            {
                if (currentChar == 'y' || currentChar == 'Y') { response = true; finished = true; }
                else if (currentChar == 'n' || currentChar == 'N') { response = false; finished = true; }
            });
            router.BindCancel(() => { response = false; finished = true; });
            router.Bind("", "", () => { if (currentChar != '\0') { currentChar = '\0'; Render(); } }, ConsoleKey.Backspace);

            router.BindUnhandled("", "", (k) =>
            {
                if (k.KeyChar == 'y' || k.KeyChar == 'Y') { currentChar = k.KeyChar; Render(); }
                else if (k.KeyChar == 'n' || k.KeyChar == 'N') { currentChar = k.KeyChar; Render(); }
            });

            try
            {
                if (LivePanel.IsActive) LivePanel.ClearKeysQueue();
                while (!finished)
                {
                    currentKey = LivePanel.IsActive ? await LivePanel.WaitForKeyAsync(token) : InputReader.ReadInput().KeyInfo;

                    var evt = new ConsoleInputEvent { Type = InputEventType.Key, KeyInfo = currentKey };
                    router.Handle(evt);
                }

                if (response.HasValue)
                {
                    if (LivePanel.IsActive) { LivePanel.FocusEntryId = null; LivePanel.UpdateLine(dynamicId.Value, fullPrompt + currentChar); }
                    else Console.WriteLine();
                    return response.Value;
                }
                return false;
            }
            finally
            {
                if (LivePanel.IsActive) LivePanel.FocusEntryId = null;
                else Console.CursorVisible = false;
                isInputRunning = false;
            }
        }

        /// <summary>
        /// Bloquea la ejecución hasta que el usuario presiona Enter.
        /// </summary>
        /// <param name="message">Mensaje a mostrar antes de la pausa.</param>
        public static void PressToContinue(string message = "[Presiona enter para regresar]")
        {
            TextViewer.WritePlain($"{ThemeColors.Dim}  {message}{ThemeColors.Reset}");
            if (LivePanel.IsActive) LivePanel.ClearKeysQueue();
            while ((LivePanel.IsActive ? LivePanel.WaitForKey().Key : Console.ReadKey(true).Key) != ConsoleKey.Enter) { }
        }
    }
}
