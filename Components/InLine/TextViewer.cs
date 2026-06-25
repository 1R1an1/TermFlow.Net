using System;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    public static class TextViewer
    {
        public static void Info(string msg)
            => WriteToOutput($"{ThemeColors.Info}{ConsoleGlyphs.InfoBullet}{ThemeColors.Reset} {msg}");

        public static void Success(string msg)
            => WriteToOutput($"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} {msg}");

        public static void Warn(string msg, bool allColor = true)
            => WriteToOutput($"{ThemeColors.Warning}!" + (allColor ? $" {msg}{ThemeColors.Reset}" : $"{ThemeColors.Reset} {msg}"));

        public static void Error(string msg, bool allColor = true)
            => WriteToOutput($"{ThemeColors.Error}✘" + (allColor ? $" {msg}{ThemeColors.Reset}" : $"{ThemeColors.Reset} {msg}"));

        public static void WritePlain(string msg)
            => WriteToOutput(msg);

        /// <summary>
        /// Genera de forma independiente un encabezado estético sobre el flujo normal,
        /// sin limpiar la pantalla ni alterar el buffer de la consola.
        /// </summary>
        public static void WriteHeader(string title)
        {

            WriteToOutput(title);
            WriteToOutput($"{ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, title.GetVisualLength())}{ThemeColors.Reset}");
        }

        private static void WriteToOutput(string message)
        {
            if (LivePanel.IsActive)
                LivePanel.AddLog(message);
            else
                Console.WriteLine("\r\x1b[K" + message);
        }
    }
}