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
    /// Utilidades para capturar entrada de texto del usuario (string, sí/no, presionar para continuar).
    /// Compatible con modo inline y con el panel dinámico <see cref="LivePanel"/>.
    /// </summary>
    public static class TextInput
    {
        /// <summary>
        /// Bool interno para prevenir la ejecución concurrente de múltiples inputs a la vez.
        /// </summary>
        private static bool isInputRunning = false;

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
            StringBuilder inputBuffer = new StringBuilder();
            int cursorPos = 0;
            int promptLength = prompt.GetVisualLength();
            string result = null;
            bool finished = false;

            if (LivePanel.IsActive)
            {
                dynamicId = LivePanel.AddDynamic(prompt);
                LivePanel.FocusEntryId = dynamicId;
                LivePanel.FocusVisualCol = promptLength;
            }
            else
            {
                Console.CursorVisible = true;
                Console.Write(prompt);
            }

            // Unificación del renderizado para no repetir código
            void Render()
            {
                if (LivePanel.IsActive)
                {
                    LivePanel.FocusVisualCol = promptLength + cursorPos;
                    LivePanel.UpdateLine(dynamicId.Value, prompt + inputBuffer.ToString());
                }
                else
                {
                    Console.SetCursorPosition(promptLength, Console.CursorTop);
                    Console.Write(inputBuffer.ToString() + " ");
                    Console.SetCursorPosition(promptLength + cursorPos, Console.CursorTop);
                }
            }

            ConsoleKeyInfo currentKey = default;
            var router = new InputRouter(false);


            router.BindConfirm(() =>
            {
                result = inputBuffer.ToString();
                finished = true;
            });

            router.Bind("", "", () =>
            {
                if ((currentKey.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    int i = cursorPos - 1;
                    while (i > 0 && (char.IsWhiteSpace(inputBuffer[i]) || inputBuffer[i] == '/' || inputBuffer[i] == '\\' || inputBuffer[i] == '.' || inputBuffer[i] == '-')) i--;
                    while (i > 0 && !(char.IsWhiteSpace(inputBuffer[i - 1]) || inputBuffer[i - 1] == '/' || inputBuffer[i - 1] == '\\' || inputBuffer[i - 1] == '.' || inputBuffer[i - 1] == '-')) i--;
                    cursorPos = Math.Max(0, i);
                }
                else cursorPos = Math.Max(0, cursorPos - 1);
                Render();
            }, ConsoleKey.LeftArrow);

            router.Bind("", "", () =>
            {
                if ((currentKey.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    int i = cursorPos;
                    while (i < inputBuffer.Length && (char.IsWhiteSpace(inputBuffer[i]) || inputBuffer[i] == '/' || inputBuffer[i] == '\\' || inputBuffer[i] == '.' || inputBuffer[i] == '-')) i++;
                    while (i < inputBuffer.Length && !(char.IsWhiteSpace(inputBuffer[i]) || inputBuffer[i] == '/' || inputBuffer[i] == '\\' || inputBuffer[i] == '.' || inputBuffer[i] == '-')) i++;
                    cursorPos = i;
                }
                else cursorPos = Math.Min(inputBuffer.Length, cursorPos + 1);
                Render();
            }, ConsoleKey.RightArrow);

            router.Bind("", "", () => { cursorPos = 0; Render(); }, ConsoleKey.Home);
            router.Bind("", "", () => { cursorPos = inputBuffer.Length; Render(); }, ConsoleKey.End);

            router.Bind("", "", () =>
            {
                if (cursorPos > 0) { inputBuffer.Remove(cursorPos - 1, 1); cursorPos--; Render(); }
            }, ConsoleKey.Backspace);

            router.Bind("", "", () =>
            {
                if (cursorPos < inputBuffer.Length) { inputBuffer.Remove(cursorPos, 1); Render(); }
            }, ConsoleKey.Delete);

            router.BindUnhandled("", "", (k) =>
            {
                if (!char.IsControl(k.KeyChar))
                {
                    inputBuffer.Insert(cursorPos, k.KeyChar);
                    cursorPos++;
                    Render();
                }
            });

            try
            {
                while (!finished && !token.IsCancellationRequested)
                {
                    currentKey = LivePanel.IsActive ? await LivePanel.WaitForKeyAsync(token) : InputReader.ReadInput().KeyInfo;

                    // Enrutamos el evento a través de InputRouter
                    var evt = new ConsoleInputEvent { Type = InputEventType.Key, KeyInfo = currentKey };
                    router.Handle(evt);
                }

                if (finished)
                {
                    if (LivePanel.IsActive) { LivePanel.FocusEntryId = null; LivePanel.UpdateLine(dynamicId.Value, prompt + inputBuffer.ToString()); }
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
            while ((LivePanel.IsActive ? LivePanel.WaitForKey().Key : Console.ReadKey(true).Key) != ConsoleKey.Enter) { }
        }
    }
}
