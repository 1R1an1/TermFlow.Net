using System;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    public static class TextViewer
    {
        public static void Info(string msg) => Console.WriteLine($"\r\x1b[K{ThemeColors.Info}{ConsoleGlyphs.InfoBullet}{ThemeColors.Reset} {msg}");

        public static void Success(string msg) => Console.WriteLine($"\r\x1b[K{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} {msg}");

        public static void Warn(string msg, bool allColor = true) => Console.WriteLine($"\r\x1b[K{ThemeColors.Warning}!" + (allColor ? $" {msg}{ThemeColors.Reset}" : $"{ThemeColors.Reset} {msg}"));

        public static void Error(string msg, bool allColor = true) => Console.WriteLine($"\r\x1b[K{ThemeColors.Error}✘" + (allColor ? $" {msg}{ThemeColors.Reset}" : $"{ThemeColors.Reset} {msg}"));

        public static void WritePlain(string msg) => Console.WriteLine("\r\x1b[K" + msg);

        /// <summary>
        /// Genera de forma independiente un encabezado estético sobre el flujo normal,
        /// sin limpiar la pantalla ni alterar el buffer de la consola.
        /// </summary>
        public static void WriteHeader(string title)
        {

            Console.WriteLine(title);
            Console.WriteLine($"{ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, title.Length)}{ThemeColors.Reset}");
        }
    }
}