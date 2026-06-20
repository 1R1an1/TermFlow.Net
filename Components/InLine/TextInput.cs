using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    public static class TextInput
    {
        /// <summary>
        /// Solicita una cadena de texto al usuario de manera asíncrona, interceptando el teclado
        /// y respondiendo inmediatamente al CancellationToken.
        /// </summary>
        public static async Task<string> ReadStringAsync(string prompt, CancellationToken token = default)
        {
            // Imprimimos el prompt exacto sin espacios agregados artificialmente
            Console.Write(prompt);

            StringBuilder inputBuffer = new StringBuilder();
            Console.CursorVisible = true;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        // Interceptamos la tecla para controlarla nosotros manualmente
                        ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);

                        // Confirmación de entrada
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            Console.WriteLine(); // Salto de línea limpio para dejar el cursor listo abajo
                            return inputBuffer.ToString();
                        }

                        // Manejo manual de Backspace (Borrar último carácter)
                        if (keyInfo.Key == ConsoleKey.Backspace)
                        {
                            if (inputBuffer.Length > 0)
                            {
                                inputBuffer.Remove(inputBuffer.Length - 1, 1);
                                // Retrocedemos el cursor, pintamos un espacio en blanco y retrocedemos de nuevo
                                Console.Write("\b \b");
                            }
                        }
                        // Captura de caracteres imprimibles normales (evitando teclas de control/flechas)
                        else if (!char.IsControl(keyInfo.KeyChar))
                        {
                            inputBuffer.Append(keyInfo.KeyChar);
                            Console.Write(keyInfo.KeyChar);
                        }
                    }
                    else
                    {
                        // Pequeño delay para no incinerar el CPU mientras esperamos que el usuario escriba
                        await Task.Delay(15);
                    }
                }
            }
            finally
            {
                Console.CursorVisible = false;
            }

            // Si salimos por cancelación del token, devolvemos una cadena vacía
            return null;
        }

        public static async Task<bool> AskAsync(string prompt)
        {

            Console.Write($"{prompt} {AnsiColor.Cyan}[y/n]{ThemeColors.Reset} ");

            while (true)
            {
                var key = Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.Y)
                {
                    Console.WriteLine("y");
                    return true;
                }
                if (key == ConsoleKey.N || key == ConsoleKey.Escape)
                {
                    Console.WriteLine("n");
                    return false;
                }
            }
        }

        public static void PressToContinue(string message = "[Presiona enter para regresar]")
        {
            TextViewer.WritePlain($"{ThemeColors.Dim}  {message}{ThemeColors.Reset}");
            while (Console.ReadKey(true).Key != ConsoleKey.Enter) { }
        }
    }
}