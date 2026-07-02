using System;
using System.Collections.Generic;
using System.Text;

namespace TermFlow.Core
{
    internal static class AnsiStringHelper
    {
        /// <summary>
        /// Elimina todos los códigos ANSI de un string.
        /// </summary>
        public static string StripAnsi(this string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, @"\x1b\[[^m]*m", "");
        }

        /// <summary>
        /// Calcula la longitud visual de un string ignorando códigos ANSI.
        /// </summary>
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
        public static int CountPhysicalLines(this string text, int width)
             => WrapText(text, width).Count;

        /// <summary>
        /// Trunca un texto a un número máximo de caracteres visuales, conservando ANSI.
        /// </summary>
        public static string Truncate(this string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0) return "";

            var result = new StringBuilder();
            int visibleCount = 0;
            int pos = 0;

            while (pos < text.Length && visibleCount < maxLength)
            {
                // Detectar código ANSI
                if (text[pos] == '\x1b' && pos + 1 < text.Length && text[pos + 1] == '[')
                {
                    int end = text.IndexOf('m', pos);
                    if (end != -1)
                    {
                        result.Append(text.Substring(pos, end - pos + 1));
                        pos = end + 1;
                        continue;
                    }
                }

                // Carácter normal
                result.Append(text[pos]);
                visibleCount++;
                pos++;
            }

            return result.ToString();
        }
    }
}