/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
using TermFlow.Core;

namespace TermFlow.Base.CanvasExt;

/// <summary>
/// Extensiones para <see cref="TermCanvas"/>.
/// </summary>
public static class CanvasExt
{
    /// <summary>
    /// Dibuja un recuadro (box) con bordes Unicode usando coordenadas de esquinas.
    /// </summary>
    /// <param name="canvas">Instancia del canvas.</param>
    /// <param name="x1">Columna inicial base 0.</param>
    /// <param name="y1">Fila inicial base 0.</param>
    /// <param name="x2">Columna final base 0.</param>
    /// <param name="y2">Fila final base 0.</param>
    /// <param name="color">Color del borde.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="canvas"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si las coordenadas son negativas.</exception>
    /// <exception cref="ArgumentException">Si el ancho o alto resultante es menor a 2.</exception>
    public static void DrawBorder(this ICanvas canvas, int x1, int y1, int x2, int y2, AnsiColor color = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (x1 < 0 || y1 < 0 || x2 < 0 || y2 < 0)
            throw new ArgumentOutOfRangeException("Las coordenadas no pueden ser negativas.");

        // Ordenamos las coordenadas por si estan invertidas
        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);

        int width = x2 - x1 + 1;
        int height = y2 - y1 + 1;

        if (width < 2 || height < 2)
            throw new ArgumentException("El ancho y alto mínimo para dibujar un borde es de 2.", nameof(x2));

        // Bordes horizontales
        canvas.WriteAt(x1 + 1, y1, new string(ConsoleGlyphs.Horizontal, width - 2), color);
        canvas.WriteAt(x1 + 1, y2, new string(ConsoleGlyphs.Horizontal, width - 2), color);

        // Bordes verticales
        canvas.WriteVertical(x1, y1 + 1, new string(ConsoleGlyphs.Vertical, height - 2), color);
        canvas.WriteVertical(x2, y1 + 1, new string(ConsoleGlyphs.Vertical, height - 2), color);

        // Esquinas
        canvas.WriteAt(x1, y1, ConsoleGlyphs.TopLeft.ToString(), color);
        canvas.WriteAt(x2, y1, ConsoleGlyphs.TopRight.ToString(), color);
        canvas.WriteAt(x1, y2, ConsoleGlyphs.BottomLeft.ToString(), color);
        canvas.WriteAt(x2, y2, ConsoleGlyphs.BottomRight.ToString(), color);
    }

    /// <summary>
    /// Rellena un área rectangular con un carácter y color, desde (x1, y1) hasta (x2, y2).
    /// Las coordenadas invertidas se ordenan y el área se recorta a los bordes del canvas.
    /// </summary>
    /// <param name="canvas">Instancia del canvas.</param>
    /// <param name="x1">Columna inicial base 0.</param>
    /// <param name="y1">Fila inicial base 0.</param>
    /// <param name="x2">Columna final base 0 (inclusive).</param>
    /// <param name="y2">Fila final base 0 (inclusive).</param>
    /// <param name="fillChar">Carácter con el que rellenar el área.</param>
    /// <param name="fillColor">Color del carácter.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="canvas"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna coordenada es negativa.</exception>
    public static void Fill(this ICanvas canvas, int x1, int y1, int x2, int y2, char fillChar, AnsiColor fillColor = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (x1 < 0 || y1 < 0 || x2 < 0 || y2 < 0)
            throw new ArgumentOutOfRangeException("Las coordenadas no pueden ser negativas.");

        // Ordenamos las coordenadas por si están invertidas
        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);

        if (x1 > x2 || y1 > y2) return;

        // La fila se arma una sola vez y se reutiliza para todas
        string row = new string(fillChar, x2 - x1 + 1);
        for (int y = y1; y <= y2; y++)
            canvas.WriteAt(x1, y, row, fillColor);
    }

    /// <summary>
    /// Escribe texto en una posición específica y luego limpia el resto de la línea según el largo especificado.
    /// </summary>
    /// <param name="x">Columna base 0 donde empezar a escribir.</param>
    /// <param name="y">Fila base 0 donde escribir.</param>
    /// <param name="text">Texto a escribir (puede contener ANSI).</param>
    /// <param name="color">Color inicial por defecto.</param>
    /// <param name="length">
    /// Cantidad de caracteres a limpiar desde el final del texto.
    /// Si es mayor que 0, limpia exactamente esa cantidad.
    /// Si es 0, limpia hasta el final de la línea.
    /// Si es menor que 0, limpia hasta el final dejando sin tocar los últimos <c>-length</c> caracteres.
    /// </param>
    /// <exception cref="ArgumentNullException">Si <paramref name="text"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="x"/> o <paramref name="y"/> están fuera del canvas.</exception>
    public static void WriteAtAndClear(this ICanvas canvas, int x, int y, string text, AnsiColor color = null, int length = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        // 1. Escribimos el texto
        canvas.WriteAt(x, y, text, color);
        int visualLength = text.GetVisualLength();

        // 2. Limpiamos la linea restante
        try { canvas.ClearLineFrom(x + visualLength, y, length); } catch (ArgumentOutOfRangeException) { }
    }

    /// <summary>
    /// Dibuja un encabezado escribiendo un título y un subrayado justo debajo.
    /// El subrayado se ajusta automáticamente al largo visible del título.
    /// </summary>
    /// <param name="canvas">Instancia del canvas.</param>
    /// <param name="x">Columna base 0 donde empezar.</param>
    /// <param name="y">Fila base 0 del título.</param>
    /// <param name="title">Texto del título.</param>
    /// <param name="titleColor">Color del título.</param>
    /// <param name="lineColor">Color del subrayado (si es null, usa el mismo del título).</param>
    public static void WriteHeader(this ICanvas canvas, int x, int y, string title, AnsiColor titleColor = null, AnsiColor lineColor = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        // 1. Escribimos el título
        canvas.WriteAt(x, y, title, titleColor);

        // 2. Calculamos el largo visible (ignorando ANSI) y dibujamos el subrayado
        int visualLength = title.GetVisualLength();
        if (visualLength > 0)
            canvas.WriteAt(x, y + 1, new string(ConsoleGlyphs.Horizontal, visualLength), lineColor);
    }

    /// <summary>
    /// Dibuja una lista de strings a partir de una coordenada, hacia abajo o hacia arriba.
    /// </summary>
    /// <param name="canvas">Instancia del canvas.</param>
    /// <param name="items">Lista de strings a dibujar.</param>
    /// <param name="x">Columna base 0.</param>
    /// <param name="y">Fila base 0 de inicio.</param>
    /// <param name="maxItems">Cantidad máxima de elementos a dibujar.</param>
    /// <param name="bottomToTop">Si es true, dibuja hacia arriba. Si es false, hacia abajo.</param>
    /// <param name="startIndex">Índice desde el cual empezar a dibujar.</param>
    /// <param name="formatter">Función que recibe el string y su índice, y devuelve el string formateado.</param>
    public static void DrawList<T>(this ICanvas canvas, IReadOnlyList<T> items, int x, int y, int maxItems = -1, int startIndex = 0, bool bottomToTop = false, Func<T, int, string> formatter = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0) return;
        maxItems = maxItems <= 0 ? items.Count : maxItems;

        int drawCount = Math.Min(items.Count, maxItems);

        for (int i = 0; i < drawCount; i++)
        {
            int itemIndex = startIndex + i;
            if (itemIndex >= items.Count) break;

            int currentY = bottomToTop ? (y - i) : (y + i);
            string text = formatter is null ? items[itemIndex].ToString() : formatter(items[itemIndex], itemIndex);
            canvas.WriteAtAndClear(x, currentY, text);
        }

        // Limpiamos las líneas que sobran
        for (int i = drawCount; i < maxItems; i++)
        {
            int currentY = bottomToTop ? (y - i) : (y + i);
            canvas.ClearLine(currentY);
        }
    }
}
