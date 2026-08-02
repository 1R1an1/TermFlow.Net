/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    /// <summary>
    /// Renderizador de tablas con bordes Unicode y ancho de columna automático
    /// calculado a partir del contenido. Soporta estilo personalizado de cabecera.
    /// </summary>
    /// <remarks>
    /// Se puede usar de dos formas:
    /// <list type="bullet">
    ///   <item><b>Estática</b>: <c>TableView.Show(headers, rows)</c> — rápida, una sola impresión.</item>
    ///   <item><b>Instancia</b>: construir un <see cref="TableView"/>, agregar headers/filas con
    ///   <see cref="AddRow"/>, <see cref="AddRows"/>, etc., y llamar a <see cref="Show(long?)"/>
    ///   cuando se quiera renderizar. Útil para construir tablas incrementalmente.</item>
    /// </list>
    /// </remarks>
    public class TableView
    {
        // ───────────────────────── Estado de instancia ─────────────────────────
        private readonly List<string[]> _rows = new List<string[]>();
        private string[] _headers = Array.Empty<string>();

        /// <summary>
        /// Obtiene o establece el estilo ANSI aplicado al texto de las cabeceras.
        /// </summary>
        /// <value>Si es <c>null</c>, se utilizará el estilo por defecto (Cyan + Bold).</value>
        public AnsiColor HeaderStyle { get; set; }

        /// <summary>
        /// Obtiene o establece las cabeceras actuales de la tabla.
        /// </summary>
        /// <value>Un array de strings representando los títulos de las columnas.</value>
        public string[] Headers
        {
            get => _headers;
            set => _headers = value.ToArray() ?? Array.Empty<string>();
        }

        /// <summary>
        /// Obtiene la cantidad de filas de datos cargadas actualmente en la tabla.
        /// </summary>
        public int RowCount => _rows.Count;

        /// <summary>
        /// Obtiene una lista de solo lectura con las filas actuales de la tabla.
        /// </summary>
        public IReadOnlyList<string[]> Rows => _rows;

        // ───────────────────────── Constructor ─────────────────────────

        /// <summary>
        /// Inicializa una nueva instancia de la clase <see cref="TableView"/> con cabeceras definidas.
        /// </summary>
        /// <param name="headers">Array con los títulos iniciales de cada columna.</param>
        /// <param name="headerStyle">Estilo ANSI opcional para la cabecera. Si es <c>null</c>, usa Cyan+Bold.</param>
        public TableView(string[] headers, List<string[]> rows = null, AnsiColor headerStyle = null)
        {
            _headers = headers.ToArray() ?? Array.Empty<string>();
            _rows = rows is null ? new() : rows.ConvertAll(r => r.ToArray());
            HeaderStyle = headerStyle;
        }

        // ───────────────────────── API fluida de modificación ─────────────────────────

        /// <summary>
        /// Reemplaza las cabeceras actuales de la tabla.
        /// </summary>
        /// <param name="headers">Array con los títulos de cada columna.</param>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        public TableView SetHeaders(string[] headers)
        {
            _headers = headers.ToArray() ?? Array.Empty<string>();
            return this;
        }

        /// <summary>
        /// Asigna un estilo ANSI personalizado a las cabeceras.
        /// </summary>
        /// <param name="style">El estilo ANSI a aplicar al texto de las cabeceras.</param>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        public TableView WithHeaderStyle(AnsiColor style)
        {
            HeaderStyle = style;
            return this;
        }

        /// <summary>
        /// Agrega una nueva fila a la tabla construida a partir de valores sueltos.
        /// </summary>
        /// <param name="values">Los valores de cada celda para esta fila.</param>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        /// <example>
        /// <code>table.AddRow("001", "Hub", "127.0.0.1");</code>
        /// </example>
        public TableView AddRow(params string[] values)
        {
            _rows.Add(values ?? Array.Empty<string>());
            return this;
        }

        /// <summary>
        /// Agrega múltiples filas a la tabla desde una colección.
        /// </summary>
        /// <param name="rows">Colección de arrays, cada uno representando una fila.</param>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        public TableView AddRows(IEnumerable<string[]> rows)
        {
            if (rows is null) return this;
            foreach (var r in rows)
                _rows.Add(r.ToArray() ?? Array.Empty<string>());
            return this;
        }

        /// <summary>
        /// Inserta una fila en el índice especificado.
        /// </summary>
        /// <param name="index">Índice basado en cero donde se debe insertar la fila.</param>
        /// <param name="values">Valores de cada celda para la nueva fila.</param>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Se lanza si <paramref name="index"/> es menor que cero o mayor que <see cref="RowCount"/>.</exception>
        public TableView InsertRow(int index, params string[] values)
        {
            if (index < 0 || index > _rows.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "El índice de inserción está fuera de rango.");
            _rows.Insert(index, values ?? Array.Empty<string>());
            return this;
        }

        /// <summary>
        /// Reemplaza los datos de una fila existente.
        /// </summary>
        /// <param name="index">Índice basado en cero de la fila a reemplazar.</param>
        /// <param name="values">Nuevos valores para las celdas de la fila.</param>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        /// <exception cref="IndexOutOfRangeException">Se lanza si <paramref name="index"/> es inválido.</exception>
        public TableView SetRow(int index, params string[] values)
        {
            if (index < 0 || index >= _rows.Count)
                throw new IndexOutOfRangeException($"Índice de fila inválido: {index}");
            _rows[index] = values ?? Array.Empty<string>();
            return this;
        }

        /// <summary>
        /// Elimina la fila ubicada en el índice especificado.
        /// </summary>
        /// <param name="index">Índice basado en cero de la fila a eliminar.</param>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        /// <exception cref="IndexOutOfRangeException">Se lanza si <paramref name="index"/> es inválido.</exception>
        public TableView RemoveRowAt(int index)
        {
            if (index < 0 || index >= _rows.Count)
                throw new IndexOutOfRangeException($"Índice de fila inválido: {index}");
            _rows.RemoveAt(index);
            return this;
        }

        /// <summary>
        /// Elimina la primera fila que coincida con el predicado proporcionado.
        /// </summary>
        /// <param name="match">Delegado <see cref="Predicate{T}"/> que define las condiciones para eliminar la fila.</param>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        public TableView RemoveRow(Predicate<string[]> match)
        {
            int idx = _rows.FindIndex(match);
            if (idx >= 0) _rows.RemoveAt(idx);
            return this;
        }

        /// <summary>
        /// Elimina todas las filas de datos de la tabla, manteniendo intactas las cabeceras.
        /// </summary>
        /// <returns>La misma instancia de <see cref="TableView"/> para permitir encadenamiento fluido.</returns>
        public TableView ClearRows()
        {
            _rows.Clear();
            return this;
        }

        // ───────────────────────── Render ─────────────────────────
        /// <summary>
        /// Renderiza la tabla en la consola o en el <see cref="LivePanel"/> activo.
        /// </summary>
        /// <param name="panelId">ID opcional de una línea dinámica del <see cref="LivePanel"/> si se desea reutilizar en lugar de crear un nuevo log.</param>
        /// <exception cref="InvalidOperationException">Se lanza si no se han definido cabeceras (<see cref="Headers"/>).</exception>
        public void Show(long? panelId = null)
        {
            if (_headers.Length == 0)
                throw new InvalidOperationException("La tabla no tiene cabeceras definidas.");

            int[] colWidths = CalculateColumnWidths(_headers, _rows);
            int totalInnerWidth = CalculateTotalWidth(colWidths);

            StringBuilder sb = new StringBuilder(1024);

            AppendBorder(sb, ConsoleGlyphs.TopLeft, ConsoleGlyphs.TopRight, totalInnerWidth);
            AppendHeaderRow(sb, _headers, colWidths, HeaderStyle);
            AppendBorder(sb, ConsoleGlyphs.Vertical, ConsoleGlyphs.Vertical, totalInnerWidth);
            AppendDataRows(sb, _rows, colWidths);
            AppendBorder(sb, ConsoleGlyphs.BottomLeft, ConsoleGlyphs.BottomRight, totalInnerWidth);
            sb.Remove(sb.Length - 1, 1); // Quita el último '\n' sobrante

            string content = sb.ToString();
            if (LivePanel.IsActive)
            {
                if (panelId is null)
                    LivePanel.AddLog(content);
                else
                    LivePanel.UpdateLine(panelId.Value, content);
            }
            else
                Console.WriteLine(content);
        }

        // ───────────────────────── Atajo estático ─────────────────────────

        /// <summary>
        /// Construye y muestra una tabla completa con bordes, cabecera estilizada y filas de datos.
        /// </summary>
        /// <param name="headers">Array con los títulos de cada columna.</param>
        /// <param name="rows">Lista de filas, cada una con los valores por columna (pueden faltar columnas).</param>
        /// <param name="headerStyle">Estilo ANSI opcional para la cabecera; si es <c>null</c> usa Cyan+Bold.</param>
        /// <param name="panelId">ID opcional de línea dinámica del <see cref="LivePanel"/> a reutilizar.</param>
        /// <remarks>Equivalente interno a <c>new TableView(headers, rows, headerStyle).Show(panelId)</c>.</remarks>
        public static void Show(string[] headers, List<string[]> rows, AnsiColor headerStyle = null, long? panelId = null)
            => new TableView(headers, rows, headerStyle).Show(panelId);

        // ───────────────────────── Helpers internos (puros, sin estado) ─────────────────────────

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
                widths[i] = headers[i].GetVisualLength();

            foreach (var row in rows)
            {
                for (int i = 0; i < widths.Length; i++)
                {
                    if (i < row.Length && row[i].GetVisualLength() > widths[i])
                        widths[i] = row[i].GetVisualLength();
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
                total += w + 2; // Texto + espacios de padding (izq/der)
            return total + (colWidths.Length - 1); // separadores verticales internos
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
                    sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset);
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
                        sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset);
                }
                sb.Append(ThemeColors.Dim).Append(ConsoleGlyphs.Vertical).Append(ThemeColors.Reset).Append('\n');
            }
        }
    }
}
