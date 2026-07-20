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
        /// Solicita una cadena de texto al usuario de manera asíncrona, interceptando el teclado
        /// y respondiendo inmediatamente al CancellationToken.
        /// </summary>
        /// <param name="prompt">Texto a mostrar antes del cursor de entrada.</param>
        /// <param name="token">Token para cancelar la lectura.</param>
        /// <returns>Texto ingresado al presionar Enter, o <c>null</c> si fue cancelado.</returns>
        public static async Task<string> ReadStringAsync(string prompt, CancellationToken token = default)
        {
            long? dynamicId = null;
            StringBuilder inputBuffer = new StringBuilder();

            if (LivePanel.IsActive)
                dynamicId = LivePanel.AddDynamic(prompt);
            else
                Console.Write(prompt);

            while (!token.IsCancellationRequested)
            {
                ConsoleKeyInfo keyInfo = LivePanel.IsActive ? await LivePanel.WaitForKeyAsync(token) : InputReader.ReadInput().KeyInfo;

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    if (!LivePanel.IsActive) Console.WriteLine();
                    return inputBuffer.ToString();
                }

                if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (inputBuffer.Length > 0)
                    {
                        inputBuffer.Remove(inputBuffer.Length - 1, 1);

                        // 2. Actualizamos la vista al borrar
                        if (LivePanel.IsActive)
                            LivePanel.UpdateLine(dynamicId.Value, prompt + inputBuffer.ToString());
                        else
                            Console.Write("\b \b");
                    }
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    inputBuffer.Append(keyInfo.KeyChar);

                    // 3. Actualizamos la vista al escribir
                    if (LivePanel.IsActive)
                        LivePanel.UpdateLine(dynamicId.Value, prompt + inputBuffer.ToString());
                    else
                        Console.Write(keyInfo.KeyChar);
                }
                await Task.Delay(15, token);
            }
            return null;
        }

        /// <summary>
        /// Realiza una pregunta de sí/no al usuario. Acepta Y, N o Escape.
        /// </summary>
        /// <param name="prompt">Pregunta a mostrar (se le agregará automáticamente " [y/n] ").</param>
        /// <returns><c>true</c> si presiona Y; <c>false</c> si presiona N o Escape.</returns>
        public static async Task<bool> AskAsync(string prompt)
        {
            string fullPrompt = $"{prompt} {AnsiColor.Cyan}[y/n]{ThemeColors.Reset} ";
            long? dynamicId = null;

            if (LivePanel.IsActive)
                dynamicId = LivePanel.AddDynamic(fullPrompt);
            else
                Console.Write(fullPrompt);

            while (true)
            {
                var key = LivePanel.IsActive ? (await LivePanel.WaitForKeyAsync()).Key : Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.Y)
                {
                    if (LivePanel.IsActive)
                        LivePanel.UpdateLine(dynamicId.Value, fullPrompt + "y");
                    else
                        Console.WriteLine("y");
                    return true;
                }
                if (key == ConsoleKey.N || key == ConsoleKey.Escape)
                {
                    if (LivePanel.IsActive)
                        LivePanel.UpdateLine(dynamicId.Value, fullPrompt + "n");
                    else
                        Console.WriteLine("n");
                    return false;
                }
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
