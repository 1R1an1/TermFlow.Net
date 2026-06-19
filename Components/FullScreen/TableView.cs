using System;
using System.Collections.Generic;
using System.Text;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    public static class TableView
    {
        public static void Show(string[] headers, List<string[]> rows)
        {
            var theme = Engine.Theme;

            // 1. Cálculos de geometría aislados
            int[] colWidths = CalculateColumnWidths(headers, rows);
            int totalInnerWidth = CalculateTotalWidth(colWidths);

            StringBuilder sb = new StringBuilder(1024);

            Engine.EnterFullScreen(false);
            Console.CursorVisible = false;

            // 2. Construcción modular de la estructura
            AppendBorder(sb, theme.CornerTopLeft, theme.CornerTopRight, totalInnerWidth);
            AppendHeaderRow(sb, headers, colWidths);
            AppendBorder(sb, theme.DividerLeft, theme.DividerRight, totalInnerWidth);
            AppendDataRows(sb, rows, colWidths);
            AppendBorder(sb, theme.CornerBottomLeft, theme.CornerBottomRight, totalInnerWidth);

            sb.Append('\n').Append(theme.Dim).Append(" Presione cualquier tecla para regresar...").Append(theme.Reset);

            // 3. Volcado único a pantalla
            Console.Write(sb.ToString());
            Console.ReadKey(intercept: true);

            Engine.ExitFullScreen();
            Console.CursorVisible = true;
        }

        private static int[] CalculateColumnWidths(string[] headers, List<string[]> rows)
        {
            int[] widths = new int[headers.Length];
            for (int i = 0; i < headers.Length; i++)
            {
                widths[i] = headers[i].Length;
            }

            foreach (var row in rows)
            {
                for (int i = 0; i < widths.Length; i++)
                {
                    if (i < row.Length && GetVisualLength(row[i]) > widths[i])
                    {
                        widths[i] = GetVisualLength(row[i]);
                    }
                }
            }
            return widths;
        }

        private static int CalculateTotalWidth(int[] colWidths)
        {
            int total = 0;
            foreach (var w in colWidths)
            {
                total += w + 2; // Texto + espacios de padding (izq/der)
            }
            return total + (colWidths.Length - 1); // Suma los separadores verticales internos
        }

        private static void AppendBorder(StringBuilder sb, char cornerLeft, char cornerRight, int width)
        {
            var theme = Engine.Theme;
            sb.Append(theme.Dim)
              .Append(cornerLeft)
              .Append(new string(theme.BorderHorizontal, width))
              .Append(cornerRight)
              .Append(theme.Reset)
              .Append('\n');
        }

        private static void AppendHeaderRow(StringBuilder sb, string[] headers, int[] colWidths)
        {
            var theme = Engine.Theme;
            sb.Append(theme.Dim).Append(theme.BorderVertical).Append(theme.Reset);

            for (int i = 0; i < headers.Length; i++)
            {
                int visualLength = GetVisualLength(headers[i]);
                int paddingNeeded = colWidths[i] - visualLength;
                sb.Append(" ")
                  .Append(theme.Cyan).Append(theme.Bold)
                  .Append(headers[i])
                  .Append(new string(' ', paddingNeeded))
                  .Append(theme.Reset).Append(" ");

                if (i < headers.Length - 1)
                {
                    sb.Append(theme.Dim).Append(theme.BorderVertical).Append(theme.Reset);
                }
            }
            sb.Append(theme.Dim).Append(theme.BorderVertical).Append(theme.Reset).Append('\n');
        }

        private static void AppendDataRows(StringBuilder sb, List<string[]> rows, int[] colWidths)
        {
            var theme = Engine.Theme;
            foreach (var row in rows)
            {
                sb.Append(theme.Dim).Append(theme.BorderVertical).Append(theme.Reset);

                for (int i = 0; i < colWidths.Length; i++)
                {
                    string value = i < row.Length ? row[i] : "";
                    int visualLength = GetVisualLength(value);
                    int paddingNeeded = colWidths[i] - visualLength;
                    sb.Append(" ").Append(value).Append(new string(' ', paddingNeeded)).Append(" ");

                    if (i < colWidths.Length - 1)
                    {
                        sb.Append(theme.Dim).Append(theme.BorderVertical).Append(theme.Reset);
                    }
                }
                sb.Append(theme.Dim).Append(theme.BorderVertical).Append(theme.Reset).Append('\n');
            }
        }

        private static int GetVisualLength(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, @"\x1b\[[^m]*m", "").Length;
        }
    }
}