/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Wcwidth;

namespace TermFlow.Dev
{
    /// <summary>
    /// Utilidades de extensión para manipular strings que contienen secuencias ANSI,
    /// calculando longitud visual, truncando y envolviendo texto sin romper los códigos de color.
    /// </summary>
    public static class AnsiStringHelper
    {
        private static readonly Regex AnsiRegex = new(@"\x1b\[[^m]*m", RegexOptions.Compiled);
        /// <summary>
        /// Elimina todos los códigos ANSI de un string.
        /// </summary>
        /// <param name="text">Texto con posibles secuencias ANSI.</param>
        /// <returns>Texto limpio sin ningún código de escape.</returns>
        public static string StripAnsi(this string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return AnsiRegex.Replace(text, "");
        }

        /// <summary>
        /// Calcula la longitud visual de un string ignorando códigos ANSI.
        /// </summary>
        /// <param name="text">Texto con posibles secuencias ANSI.</param>
        /// <returns>Cantidad de caracteres visibles reales.</returns>
        public static int GetVisualLength(this string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return StripAnsi(text).Length;
        }

        /// <summary>
        /// Envuelve un texto respetando códigos ANSI y saltos de línea.
        /// </summary>
        /// <param name="text">Texto con posibles códigos ANSI</param>
        /// <param name="width">Ancho máximo en caracteres visuales</param>
        /// <returns>Lista de líneas envueltas con sus códigos ANSI conservados</returns>
        public static List<string> WrapText(this string text, int width)
        {
            var result = new List<string>();
            if (width <= 0) { result.Add(text ?? ""); return result; }
            if (string.IsNullOrEmpty(text)) { result.Add(""); return result; }

            // 1. Normalizamos saltos de línea de Windows (\r\n -> \n) y separamos por líneas lógicas originales
            string[] logicalLines = text.Replace("\r\n", "\n").Split('\n');

            foreach (var logicalLine in logicalLines)
            {
                // Si es una línea completamente vacía (ej. dos saltos seguidos "\n\n"), la añadimos directo
                if (logicalLine.Length == 0)
                {
                    result.Add("");
                    continue;
                }

                var currentLine = new StringBuilder();
                int visibleCount = 0;
                int pos = 0;

                while (pos < logicalLine.Length)
                {
                    // 2. Detectar código ANSI
                    if (logicalLine[pos] == '\x1b' && pos + 1 < logicalLine.Length && logicalLine[pos + 1] == '[')
                    {
                        int end = logicalLine.IndexOf('m', pos);
                        if (end != -1)
                        {
                            // Añadir el ANSI a la línea en la posición donde aparece
                            currentLine.Append(logicalLine.Substring(pos, end - pos + 1));
                            pos = end + 1;
                            continue;
                        }
                    }

                    // 3. Evaluar el ancho ANTES de agregar el carácter.
                    // Si ya alcanzamos el límite exacto, cerramos esta línea física e iniciamos la siguiente.
                    if (visibleCount == width)
                    {
                        result.Add(currentLine.ToString());
                        currentLine.Clear();
                        visibleCount = 0;
                    }

                    // 4. Agregar carácter normal
                    currentLine.Append(logicalLine[pos]);
                    visibleCount++;
                    pos++;
                }

                // 5. Guardar el residuo final que haya quedado de esta línea lógica
                if (currentLine.Length > 0 || visibleCount == 0)
                {
                    result.Add(currentLine.ToString());
                }
            }

            return result;
        }

        /// <summary>
        /// Cuenta las líneas físicas que ocupa un texto al ser envuelto.
        /// </summary>
        /// <param name="text">Texto a evaluar.</param>
        /// <param name="width">Ancho máximo en caracteres visuales.</param>
        /// <returns>Cantidad de líneas físicas resultantes.</returns>
        public static int CountPhysicalLines(this string text, int width)
             => WrapText(text, width).Count;

        /// <summary>
        /// Trunca un texto a un ancho físico máximo en columnas, conservando códigos ANSI.
        /// </summary>
        /// <param name="text">Texto original con posibles ANSI.</param>
        /// <param name="maxWidth">Ancho máximo en columnas físicas a conservar.</param>
        /// <returns>Texto truncado manteniendo los códigos ANSI intactos.</returns>
        public static string Truncate(this string text, int maxWidth)
        {
            if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0) return "";
            if (text.GetRealWidth() <= maxWidth) return text;

            var result = new StringBuilder();
            int width = 0;
            int pos = 0;
            int len = text.Length;

            while (pos < len)
            {
                char c = text[pos];

                if (c == '\x1b' && pos + 1 < len && text[pos + 1] == '[')
                {
                    int end = text.IndexOf('m', pos + 2);
                    if (end != -1)
                    {
                        result.Append(text, pos, end - pos + 1);
                        pos = end + 1;
                        continue;
                    }
                }

                int codePoint;
                int charLength = 1;

                if (char.IsHighSurrogate(c) && pos + 1 < len && char.IsLowSurrogate(text[pos + 1]))
                {
                    codePoint = char.ConvertToUtf32(c, text[pos + 1]);
                    charLength = 2;
                }
                else
                    codePoint = c;

                int w = UnicodeCalculator.GetWidth(codePoint);
                if (w > 0 && width + w > maxWidth) break;

                if (w > 0) width += w;

                result.Append(text, pos, charLength);
                pos += charLength;
            }

            return result.ToString();
        }

        /// <summary>
        /// Parsea un string separando el texto visible de las secuencias ANSI.
        /// </summary>
        /// <returns>Una colección de tuplas (Texto, EsAnsi).</returns>
        public static IEnumerable<(string Segment, bool IsAnsi)> ParseAnsi(this string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            int i = 0;
            while (i < text.Length)
            {
                // Si detectamos el inicio de una secuencia ANSI CSI (\x1b[ ... )
                if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '[')
                {
                    int end = i + 2;
                    while (end < text.Length && !char.IsLetter(text[end]))
                        end++;

                    if (end < text.Length)
                    {
                        yield return (text.Substring(i, end - i + 1), true);
                        i = end + 1;
                        continue;
                    }
                }

                // Si no es ANSI, extraer el bloque de texto puro hasta el próximo ANSI
                int start = i;
                while (i < text.Length && !(text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '['))
                    i++;
                yield return (text.Substring(start, i - start), false);
            }
        }

        /// <summary>
        /// Calcula el ancho físico real que ocupará <paramref name="text"/> en una terminal,
        /// sumando el ancho de cada codepoint (wide=2, narrow=1, zero-width=0) y salteando secuencias ANSI.
        /// </summary>
        /// <param name="text">Texto con posibles secuencias ANSI.</param>
        /// <returns>Ancho en columnas físicas que el texto ocupará en la terminal.</returns>
        public static int GetRealWidth(this string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int total = 0;
            int i = 0;
            var txt = text.StripAnsi();
            while (i < txt.Length)
            {
                char c = txt[i];
                int codePoint;
                int charCount;
                if (char.IsHighSurrogate(c) && i + 1 < txt.Length && char.IsLowSurrogate(txt[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(c, txt[i + 1]);
                    charCount = 2;
                }
                else { codePoint = c; charCount = 1; }

                int w = UnicodeCalculator.GetWidth(codePoint);
                if (w > 0) total += w;
                i += charCount;
            }
            return total;
        }
    }
}
