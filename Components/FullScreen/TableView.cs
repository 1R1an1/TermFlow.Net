using System;
using System.Collections.Generic;
using System.Text;
using TermFlow.Components.InLine;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    public static class TableView
    {
        public static void Show(string[] headers, List<string[]> rows, AnsiColor headerStyle = null)
        {


            // 1. Cálculos de geometría aislados
            int[] colWidths = CalculateColumnWidths(headers, rows);
            int totalInnerWidth = CalculateTotalWidth(colWidths);

            StringBuilder sb = new StringBuilder(1024);

            Engine.EnterFullScreen(false);
            Console.CursorVisible = false;

            // 2. Construcción modular de la estructura
            AppendBorder(sb, ConsoleGlyphs.TopLeft, ConsoleGlyphs.TopRight, totalInnerWidth);
            AppendHeaderRow(sb, headers, colWidths, headerStyle);
            AppendBorder(sb, ConsoleGlyphs.Vertical, ConsoleGlyphs.Vertical, totalInnerWidth);
            AppendDataRows(sb, rows, colWidths);
            AppendBorder(sb, ConsoleGlyphs.BottomLeft, ConsoleGlyphs.BottomRight, totalInnerWidth);

            sb.Append('\n');

            // 3. Volcado único a pantalla
            Console.Write(sb.ToString());
            TextInput.PressToContinue();

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

            sb.Append(ThemeColors.Dim)
              .Append(cornerLeft)
              .Append(new string(ConsoleGlyphs.Horizontal, width))
              .Append(cornerRight)
              .Append(ThemeColors.Reset)
              .Append('\n');
        }

        private static void AppendHeaderRow(StringBuilder sb, string[] headers, int[] colWidths, AnsiColor style = null)
        {

            sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset);

            for (int i = 0; i < headers.Length; i++)
            {
                int visualLength = GetVisualLength(headers[i]);
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

        private static void AppendDataRows(StringBuilder sb, List<string[]> rows, int[] colWidths)
        {

            foreach (var row in rows)
            {
                sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset);

                for (int i = 0; i < colWidths.Length; i++)
                {
                    string value = i < row.Length ? row[i] : "";
                    int visualLength = GetVisualLength(value);
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

        private static int GetVisualLength(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, @"\x1b\[[^m]*m", "").Length;
        }
    }
}