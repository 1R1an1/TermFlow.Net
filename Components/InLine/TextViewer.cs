/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    /// <summary>
    /// Componente de salida de texto en línea (no full-screen).
    /// Imprime mensajes con estilo semántico (info/success/warn/error), encabezados simples.
    /// Si el <see cref="LivePanel"/> está activo, redirige la salida al panel.
    /// </summary>
    public static class TextViewer
    {
        /// <summary>
        /// Imprime un mensaje informativo con la viñeta de info.
        /// </summary>
        /// <param name="msg">Texto a mostrar.</param>
        public static void Info(string msg)
            => WriteToOutput(InfoF(msg));

        /// <summary>
        /// Imprime un mensaje de éxito con el check verde.
        /// </summary>
        /// <param name="msg">Texto a mostrar.</param>
        public static void Success(string msg)
            => WriteToOutput(SuccessF(msg));

        /// <summary>
        /// Imprime un mensaje de advertencia con el símbolo de warning.
        /// </summary>
        /// <param name="msg">Texto a mostrar.</param>
        /// <param name="allColor">Si <c>true</c> colorea todo el mensaje; si <c>false</c> solo el ícono.</param>
        public static void Warn(string msg, bool allColor = true)
            => WriteToOutput(WarnF(msg, allColor));

        /// <summary>
        /// Imprime un mensaje de error con el símbolo de error.
        /// </summary>
        /// <param name="msg">Texto a mostrar.</param>
        /// <param name="allColor">Si <c>true</c> colorea todo el mensaje; si <c>false</c> solo el ícono.</param>
        public static void Error(string msg, bool allColor = true)
            => WriteToOutput(ErrorF(msg, allColor));

        /// <summary>
        /// Genera la cadena ANSI para un mensaje informativo sin imprimirlo.
        /// </summary>
        /// <param name="msg">Texto a formatear.</param>
        /// <returns>Cadena con el estilo aplicado.</returns>
        public static string InfoF(string msg)
            => $"{ThemeColors.Info}{ConsoleGlyphs.InfoBullet}{ThemeColors.Reset} {msg}";

        /// <summary>
        /// Genera la cadena ANSI para un mensaje de éxito sin imprimirlo.
        /// </summary>
        /// <param name="msg">Texto a formatear.</param>
        /// <returns>Cadena con el estilo aplicado.</returns>
        public static string SuccessF(string msg)
            => $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} {msg}";

        /// <summary>
        /// Genera la cadena ANSI para un mensaje de advertencia sin imprimirlo.
        /// </summary>
        /// <param name="msg">Texto a formatear.</param>
        /// <param name="allColor">Si <c>true</c> colorea todo el mensaje; si <c>false</c> solo el ícono.</param>
        /// <returns>Cadena con el estilo aplicado.</returns>
        public static string WarnF(string msg, bool allColor = true)
            => $"{ThemeColors.Warning}{ConsoleGlyphs.Warning}" + (allColor ? $" {msg}{ThemeColors.Reset}" : $"{ThemeColors.Reset} {msg}");

        /// <summary>
        /// Genera la cadena ANSI para un mensaje de error sin imprimirlo.
        /// </summary>
        /// <param name="msg">Texto a formatear.</param>
        /// <param name="allColor">Si <c>true</c> colorea todo el mensaje; si <c>false</c> solo el ícono.</param>
        /// <returns>Cadena con el estilo aplicado.</returns>
        public static string ErrorF(string msg, bool allColor = true)
            => $"{ThemeColors.Error}{ConsoleGlyphs.Error}" + (allColor ? $" {msg}{ThemeColors.Reset}" : $"{ThemeColors.Reset} {msg}");

        /// <summary>
        /// Imprime un mensaje plano sin formato ni íconos.
        /// </summary>
        /// <param name="msg">Texto a mostrar.</param>
        public static void WritePlain(string msg)
            => WriteToOutput(msg);

        /// <summary>
        /// Genera de forma independiente un encabezado estético sobre el flujo normal,
        /// sin limpiar la pantalla ni alterar el buffer de la consola.
        /// </summary>
        /// <param name="title">Título del encabezado.</param>
        public static void WriteHeader(string title)
        {

            WriteToOutput(title);
            WriteToOutput($"{ThemeColors.Dim}{new string(ConsoleGlyphs.Horizontal, title.GetVisualLength())}{ThemeColors.Reset}");
        }

        /// <summary>
        /// Redirige el mensaje al <see cref="LivePanel"/> si está activo, o lo imprime
        /// directamente con limpieza de línea (\r + \x1b[K) si la consola está en modo inline.
        /// </summary>
        /// <param name="message">Mensaje ya formateado a imprimir.</param>
        private static void WriteToOutput(string message)
        {
            if (LivePanel.IsActive)
                LivePanel.AddLog(message);
            else
                Console.WriteLine("\r\x1b[K" + message);
        }
    }
}
