/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Text;
using TermFlow.Components.InLine;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    /// <summary>
    /// Renderizador de tablas con bordes Unicode y ancho de columna automático
    /// calculado a partir del contenido. Soporta estilo personalizado de cabecera.
    /// </summary>
    public static class TableView
    {
        /// <summary>
        /// Construye y muestra una tabla completa con bordes, cabecera estilizada y filas de datos.
        /// Si el <see cref="LivePanel"/> está activo, la tabla se vuelca como un solo log.
        /// </summary>
        /// <param name="headers">Array con los títulos de cada columna.</param>
        /// <param name="rows">Lista de filas, cada una con los valores por columna (pueden faltar columnas).</param>
        /// <param name="headerStyle">Estilo ANSI opcional para la cabecera; si es <c>null</c> usa Cyan+Bold.</param>
        public static void Show(string[] headers, List<string[]> rows, AnsiColor headerStyle = null)
        {


            // 1. Cálculos de geometría aislados
            int[] colWidths = CalculateColumnWidths(headers, rows);
            int totalInnerWidth = CalculateTotalWidth(colWidths);

            StringBuilder sb = new StringBuilder(1024);


            // 2. Construcción modular de la estructura
            AppendBorder(sb, ConsoleGlyphs.TopLeft, ConsoleGlyphs.TopRight, totalInnerWidth);
            AppendHeaderRow(sb, headers, colWidths, headerStyle);
            AppendBorder(sb, ConsoleGlyphs.Vertical, ConsoleGlyphs.Vertical, totalInnerWidth);
            AppendDataRows(sb, rows, colWidths);
            AppendBorder(sb, ConsoleGlyphs.BottomLeft, ConsoleGlyphs.BottomRight, totalInnerWidth);


            // 3. Volcado único a pantalla
            if (LivePanel.IsActive)
                LivePanel.AddLog(sb.ToString());
            else
                Console.Write(sb.Append('\n'));

        }

        /// <summary>
        /// Calcula el ancho de cada columna tomando el máximo entre la cabecera y todas las celdas de esa columna.
        /// </summary>
        /// <param name="headers">Cabeceras de las columnas.</param>
        /// <param name="rows">Filas de datos a evaluar.</param>
        /// <returns>Array de anchos visuales por columna.</returns>
        private static int[] CalculateColumnWidths(string[] headers, List<string[]> rows)
        {
            int[] widths = new int[headers.Length];
            for (int i = 0; i < headers.Length; i++)
            {
                widths[i] = headers[i].GetVisualLength();
            }

            foreach (var row in rows)
            {
                for (int i = 0; i < widths.Length; i++)
                {
                    if (i < row.Length && row[i].GetVisualLength() > widths[i])
                    {
                        widths[i] = row[i].GetVisualLength();
                    }
                }
            }
            return widths;
        }

        /// <summary>
        /// Suma el ancho total interno de la tabla (padding incluido + separadores verticales).
        /// </summary>
        /// <param name="colWidths">Anchos por columna.</param>
        /// <returns>Ancho total en caracteres visibles.</returns>
        private static int CalculateTotalWidth(int[] colWidths)
        {
            int total = 0;
            foreach (var w in colWidths)
            {
                total += w + 2; // Texto + espacios de padding (izq/der)
            }
            return total + (colWidths.Length - 1); // Suma los separadores verticales internos
        }

        /// <summary>
        /// Appendiza al buffer una línea de borde horizontal con esquinas personalizadas.
        /// </summary>
        /// <param name="sb">StringBuilder destino.</param>
        /// <param name="cornerLeft">Carácter de esquina izquierda.</param>
        /// <param name="cornerRight">Carácter de esquina derecha.</param>
        /// <param name="width">Ancho del borde (sin contar esquinas).</param>
        private static void AppendBorder(StringBuilder sb, char cornerLeft, char cornerRight, int width)
        {

            sb.Append(ThemeColors.Dim)
              .Append(cornerLeft)
              .Append(new string(ConsoleGlyphs.Horizontal, width))
              .Append(cornerRight)
              .Append(ThemeColors.Reset)
              .Append('\n');
        }

        /// <summary>
        /// Appendiza la fila de cabeceras con padding y estilo ANSI personalizado.
        /// </summary>
        /// <param name="sb">StringBuilder destino.</param>
        /// <param name="headers">Cabeceras a renderizar.</param>
        /// <param name="colWidths">Anchos calculados por columna.</param>
        /// <param name="style">Estilo ANSI a aplicar al texto; si es <c>null</c> se usa Cyan+Bold.</param>
        private static void AppendHeaderRow(StringBuilder sb, string[] headers, int[] colWidths, AnsiColor style = null)
        {

            sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset);

            for (int i = 0; i < headers.Length; i++)
            {
                int visualLength = headers[i].GetVisualLength();
                int paddingNeeded = colWidths[i] - visualLength;
                sb.Append(" ")
                  .Append(style ?? $"{AnsiColor.Cyan}{AnsiColor.Bold}")
                  .Append(headers[i])
                  .Append(new string(' ', paddingNeeded))
                  .Append(ThemeColors.Reset).Append(" ");

                if (i < headers.Length - 1)
                {
                    sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset);
                }
            }
            sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset).Append('\n');
        }

        /// <summary>
        /// Appendiza todas las filas de datos con padding para alinear columnas.
        /// </summary>
        /// <param name="sb">StringBuilder destino.</param>
        /// <param name="rows">Filas a renderizar.</param>
        /// <param name="colWidths">Anchos calculados por columna.</param>
        private static void AppendDataRows(StringBuilder sb, List<string[]> rows, int[] colWidths)
        {

            foreach (var row in rows)
            {
                sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset);

                for (int i = 0; i < colWidths.Length; i++)
                {
                    string value = i < row.Length ? row[i] : "";
                    int visualLength = value.GetVisualLength();
                    int paddingNeeded = colWidths[i] - visualLength;
                    sb.Append(" ").Append(value).Append(new string(' ', paddingNeeded)).Append(" ");

                    if (i < colWidths.Length - 1)
                    {
                        sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset);
                    }
                }
                sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset).Append('\n');
            }
        }
    }
}
