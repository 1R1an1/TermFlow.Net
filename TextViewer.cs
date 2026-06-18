using System;

namespace ConsoleUtils
{
    public static class TextViewer
    {
        public static void Info(string msg)
        {
            var theme = Engine.Theme;
            Console.WriteLine($"\r\x1b[K{theme.Cyan}{theme.InfoBullet}{theme.Reset} {msg}");
        }

        public static void Success(string msg)
        {
            var theme = Engine.Theme;
            Console.WriteLine($"\r\x1b[K{theme.Success}{theme.Checked}{theme.Reset} {msg}");
        }

        public static void Warn(string msg)
        {
            var theme = Engine.Theme;
            Console.WriteLine($"\r\x1b[K{theme.Warning}!{theme.Reset} {msg}");
        }

        public static void Error(string msg)
        {
            var theme = Engine.Theme;
            Console.WriteLine($"\r\x1b[K{theme.Error}✘{theme.Reset} {msg}");
        }

        public static void WritePlain(string msg)
        {
            Console.WriteLine("\r\x1b[K" + msg);
        }

        /// <summary>
        /// Genera de forma independiente un encabezado estético sobre el flujo normal,
        /// sin limpiar la pantalla ni alterar el buffer de la consola.
        /// </summary>
        public static void WriteHeader(string title)
        {
            var theme = Engine.Theme;
            Console.WriteLine($"{theme.Title}{theme.Bold}{title}{theme.Reset}");
            Console.WriteLine($"{theme.Dim}{new string(theme.BorderHorizontal, title.Length)}{theme.Reset}");
        }
    }
}