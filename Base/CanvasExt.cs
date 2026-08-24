/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
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
    /// <param name="color">Color del borde.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="canvas"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si las coordenadas son negativas.</exception>
    /// <exception cref="ArgumentException">Si el ancho o alto resultante es menor a 2.</exception>
    public static void DrawBorder(this TermCanvas canvas, int x1, int y1, int x2, int y2, AnsiColor color = null)
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
    public static void WriteAtAndClear(this TermCanvas canvas, int x, int y, string text, AnsiColor color = null, int length = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        // 1. Escribimos el texto
        canvas.WriteAt(x, y, text, color);
        int visualLength = text.GetVisualLength();

        // 2. Limpiamos la linea restante
        try { canvas.ClearLineFrom(x + visualLength, y, length); } catch (ArgumentOutOfRangeException) { }
    }
}
