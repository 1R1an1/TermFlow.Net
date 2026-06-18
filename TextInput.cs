using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TermFlow
{
    public static class TextInput
    {
        /// <summary>
        /// Solicita una cadena de texto al usuario de manera asíncrona, interceptando el teclado
        /// y respondiendo inmediatamente al CancellationToken.
        /// </summary>
        public static async Task<string> ReadStringAsync(string prompt, CancellationToken token = default)
        {
            var theme = Engine.Theme;

            // Imprimimos el prompt exacto sin espacios agregados artificialmente
            Console.Write($"{theme.Primary}{prompt}{theme.Reset}");

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