using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleUtils
{
    public static class TextInput
    {
        /// <summary>
        /// Lee una línea de texto directamente desde la posición actual del cursor sin alterar el resto de la pantalla.
        /// </summary>
        public static async Task<string> ReadAsync(string prompt, bool isPassword = false, CancellationToken token = default)
        {
            StringBuilder inputBuffer = new StringBuilder();
            var theme = Engine.Theme;

            // Imprimimos el prompt inicial una sola vez
            Console.Write(prompt);

            // Guardamos la posición horizontal donde arranca el texto del usuario para poder borrar correctamente
            int startCursorLeft = Console.CursorLeft;

            while (!token.IsCancellationRequested)
            {
                // Movemos el cursor al inicio del texto del usuario y limpiamos la línea hacia la derecha
                Console.SetCursorPosition(startCursorLeft, Console.CursorTop);

                string textToDisplay = isPassword ? new string('*', inputBuffer.Length) : inputBuffer.ToString();
                Console.Write($"{theme.Bold}{textToDisplay}{theme.Reset}_\x1b[K");

                var inputEvent = InputReader.ReadInput();
                if (inputEvent.Type == InputEventType.Key)
                {
                    var key = inputEvent.KeyInfo;

                    if (key.Key == ConsoleKey.Enter)
                    {
                        // Quitamos el guion bajo simulado, limpiamos la línea y saltamos de renglón
                        Console.SetCursorPosition(startCursorLeft, Console.CursorTop);
                        Console.Write($"{theme.Bold}{textToDisplay}{theme.Reset}\x1b[K\n");
                        return inputBuffer.ToString();
                    }
                    if (key.Key == ConsoleKey.Escape)
                    {
                        // Si cancela, limpiamos la línea completa del prompt y devolvemos null
                        Console.SetCursorPosition(startCursorLeft - prompt.Length, Console.CursorTop);
                        Console.Write("\x1b[K");
                        return null;
                    }
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (inputBuffer.Length > 0)
                        {
                            inputBuffer.Remove(inputBuffer.Length - 1, 1);
                        }
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        // Control básico para que no se desborde de la pantalla de la consola
                        if (Console.CursorLeft < Console.WindowWidth - 2)
                        {
                            inputBuffer.Append(key.KeyChar);
                        }
                    }
                }

                await Task.Delay(15, token);
            }

            return null;
        }

        /// <summary>
        /// Solicita una cadena de texto al usuario de manera asíncrona, interceptando el teclado
        /// y respondiendo inmediatamente al CancellationToken.
        /// </summary>
        public static async Task<string> ReadStringAsync(string prompt, string color, CancellationToken token = default)
        {
            var theme = Engine.Theme;

            // Imprimimos el prompt exacto sin espacios agregados artificialmente
            Console.Write($"{color}{prompt}{theme.Reset}");

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
    }
}