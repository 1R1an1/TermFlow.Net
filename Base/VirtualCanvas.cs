/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using TermFlow.Core;

namespace TermFlow.Base;

/// <summary>
/// Canvas virtual dentro de un <see cref="TermCanvas"/>: un rectángulo con coordenadas propias
/// base 0 que traduce todas sus operaciones al canvas padre. No tiene buffers ni Flush propio,
/// y ninguna operación modifica celdas fuera de su rectángulo.
/// </summary>
public sealed class VirtualCanvas : ICanvas
{
    /// <summary>Canvas padre sobre el que se dibuja todo el contenido.</summary>
    private readonly TermCanvas _parent;

    /// <summary>Columna absoluta (base 0) del canvas padre donde empieza.</summary>
    public int X { get; private set; }

    /// <summary>Fila absoluta (base 0) del canvas padre donde empieza.</summary>
    public int Y { get; private set; }

    /// <summary>Ancho en celdas. No crece si el padre se agranda.</summary>
    public int Width { get; private set; }

    /// <summary>Alto en celdas. No crece si el padre se agranda.</summary>
    public int Height { get; private set; }

    /// <summary>
    /// Inicializa el canvas virtual sobre el rectángulo indicado, en coordenadas absolutas del
    /// padre (ambas esquinas inclusivas).
    /// </summary>
    /// <param name="parent">Canvas padre sobre el que se dibuja.</param>
    /// <param name="x1">Columna absoluta base 0 donde empieza.</param>
    /// <param name="y1">Fila absoluta base 0 donde empieza.</param>
    /// <param name="x2">Columna absoluta base 0 donde termina (inclusive).</param>
    /// <param name="y2">Fila absoluta base 0 donde termina (inclusive).</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna coordenada es negativa o el
    /// rectángulo se sale del canvas padre.</exception>
    internal VirtualCanvas(TermCanvas parent, int x1, int y1, int x2, int y2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x1);
        ArgumentOutOfRangeException.ThrowIfNegative(y1);
        ArgumentOutOfRangeException.ThrowIfNegative(x2);
        ArgumentOutOfRangeException.ThrowIfNegative(y2);

        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);

        // El rectángulo tiene que entrar en el padre
        if (x2 >= parent.Width || y2 >= parent.Height)
            throw new ArgumentOutOfRangeException("El rectángulo se sale del canvas padre.");


        _parent = parent;
        X = x1; Y = y1;
        Width = x2 - x1 + 1; Height = y2 - y1 + 1;
    }

    /// <summary>
    /// Crea un canvas virtual dentro de este, en coordenadas locales. Se ancla al
    /// <see cref="TermCanvas"/> raíz y su rectángulo se intersecta con el de este.
    /// </summary>
    /// <param name="x1">Columna local base 0 donde empieza.</param>
    /// <param name="y1">Fila local base 0 donde empieza.</param>
    /// <param name="x2">Columna local base 0 donde termina (inclusive).</param>
    /// <param name="y2">Fila local base 0 donde termina (inclusive).</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna coordenada es negativa o el
    /// rectángulo se sale de este canvas.</exception>
    /// <returns>El canvas virtual creado, anclado al canvas raíz.</returns>
    public VirtualCanvas CreateSubCanvas(int x1, int y1, int x2, int y2)
    {
        if (x1 < 0 || y1 < 0 || x2 < 0 || y2 < 0)
            throw new ArgumentOutOfRangeException("Las coordenadas no pueden ser negativas.");

        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);

        // El rectángulo tiene que entrar en este canvas
        if (x2 >= Width || y2 >= Height)
            throw new ArgumentOutOfRangeException("El rectángulo se sale de este canvas.");


        // Intersectamos el borde lejano con este sub-canvas (el cercano ya es >= 0 validado)
        int nx2 = Math.Min(X + x2, X + Width - 1);
        int ny2 = Math.Min(Y + y2, Y + Height - 1);

        if (nx2 < X + x1 || ny2 < Y + y1)
            throw new ArgumentOutOfRangeException("El rectángulo queda fuera de este sub-canvas.");

        return new VirtualCanvas(_parent, X + x1, Y + y1, nx2, ny2);
    }

    /// <summary>
    /// Escribe texto de forma horizontal en una posición del canvas virtual.
    /// Soporta secuencias ANSI dentro del string, aplicando el color a los caracteres subsiguientes.
    /// Los caracteres que caen fuera de los bordes se descartan.
    /// </summary>
    /// <param name="x">Columna base 0 donde empezar a escribir.</param>
    /// <param name="y">Fila base 0 donde escribir.</param>
    /// <param name="text">Texto a escribir (puede contener ANSI).</param>
    /// <param name="color">Color inicial por defecto.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="text"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="y"/> está fuera del rango del canvas virtual.</exception>
    public void WriteAt(int x, int y, string text, AnsiColor color = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y), "La fila Y está fuera del sub-canvas.");

        _parent.WriteAt(X + x, Y + y, text, color);
    }

    /// <summary>
    /// Escribe texto en vertical comenzando desde una posición del canvas virtual.
    /// Soporta secuencias ANSI dentro del string, aplicando el color a los caracteres.
    /// Cada carácter se escribe en una fila consecutiva, manteniendo la misma columna.
    /// Los caracteres que caen fuera de los bordes se descartan.
    /// </summary>
    /// <param name="x">Columna base 0 donde escribir.</param>
    /// <param name="y">Fila base 0 donde comenzar a escribir verticalmente.</param>
    /// <param name="text">Texto a escribir verticalmente (cada carácter en una línea).</param>
    /// <param name="color">Color inicial por defecto.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="text"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="x"/> está fuera del rango del canvas virtual.</exception>
    public void WriteVertical(int x, int y, string text, AnsiColor color = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (x < 0 || x >= Width)
            throw new ArgumentOutOfRangeException(nameof(x), "La columna X está fuera del sub-canvas.");

        _parent.WriteVertical(X + x, Y + y, text, color);
    }

    /// <summary>Limpia todo el sub-canvas.</summary>
    public void Clear()
        => ClearArea(0, 0, Width - 1, Height - 1);

    /// <summary>
    /// Limpia una fila del canvas virtual, únicamente sus columnas (no toda la fila del padre).
    /// </summary>
    /// <param name="y">Fila base 0 a limpiar.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="y"/> está fuera del rango del canvas virtual.</exception>
    public void ClearLine(int y)
    {
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y), "La fila está fuera del sub-canvas.");
        ClearArea(0, y, Width - 1, y);
    }

    /// <summary>
    /// Limpia un área rectangular del canvas virtual poniendo espacios y color <c>null</c>.
    /// Las coordenadas invertidas se ordenan y el área se recorta a los bordes del canvas virtual.
    /// </summary>
    /// <param name="x1">Columna inicial base 0.</param>
    /// <param name="y1">Fila inicial base 0.</param>
    /// <param name="x2">Columna final base 0.</param>
    /// <param name="y2">Fila final base 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna coordenada es negativa.</exception>
    public void ClearArea(int x1, int y1, int x2, int y2)
    {
        if (x1 < 0 || y1 < 0 || x2 < 0 || y2 < 0)
            throw new ArgumentOutOfRangeException("Las coordenadas no pueden ser negativas.");

        // Mismo criterio que el padre: coordenadas invertidas se ordenan
        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);

        // Limitamos al sub-canvas para no borrar fuera de él
        x2 = Math.Min(x2, Width - 1);
        y2 = Math.Min(y2, Height - 1);
        if (x1 > x2 || y1 > y2) return; // El área quedó afuera

        _parent.ClearArea(X + x1, Y + y1, X + x2, Y + y2);
    }

    /// <summary>
    /// Limpia una línea del canvas virtual desde una posición X hasta el final de su línea o un
    /// largo determinado.
    /// </summary>
    /// <param name="x">Columna base 0 desde donde empezar a limpiar.</param>
    /// <param name="y">Fila base 0 a limpiar.</param>
    /// <param name="length">
    /// Cantidad de caracteres a limpiar.
    /// Si es mayor que 0, limpia exactamente esa cantidad de caracteres desde <paramref name="x"/>.
    /// Si es 0, limpia hasta el final de la línea del canvas virtual.
    /// Si es menor que 0, limpia hasta el final de la línea, dejando sin tocar los últimos <c>-length</c> caracteres.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="x"/> o <paramref name="y"/> están fuera del canvas virtual.</exception>
    public void ClearLineFrom(int x, int y, int length = 0)
    {
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y), "La fila está fuera del sub-canvas.");
        if (x < 0 || x >= Width)
            throw new ArgumentOutOfRangeException(nameof(x), "La columna está fuera del sub-canvas.");

        if (length == 0)
            ClearArea(x, y, Width - 1, y);
        else
        {
            int endX = length < 0 ? Width - (-length) - 1 : x + length - 1;
            if (endX >= x) ClearArea(x, y, endX, y);
        }
    }

    /// <summary>
    /// Limpia desde una posición hasta el final del canvas virtual (equivalente por superficie a \x1b[J).
    /// </summary>
    /// <param name="x">Columna base 0.</param>
    /// <param name="y">Fila base 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si las coordenadas (x, y) están fuera del canvas virtual.</exception>
    public void ClearFromPoint(int x, int y)
    {
        if (y < 0 || y >= Height || x < 0 || x >= Width)
            throw new ArgumentOutOfRangeException("Las coordenadas (x, y) están fuera del sub-canvas.");
        ClearArea(x, y, Width - 1, Height - 1);
    }

    /// <summary>
    /// Redimensiona el canvas virtual manteniendo su posición (X, Y), limpiando antes el
    /// rectángulo viejo para que no quede contenido dibujado fuera del nuevo borde.
    /// El nuevo tamaño debe seguir entrando dentro del canvas padre.
    /// </summary>
    /// <param name="newWidth">Nuevo ancho, en celdas.</param>
    /// <param name="newHeight">Nuevo alto, en celdas.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si el ancho o el alto son menores o iguales
    /// a 0, o si el nuevo tamaño se sale del canvas padre.</exception>
    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "El nuevo ancho debe ser mayor a 0.");
        if (newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newHeight), "El nuevo alto debe ser mayor a 0.");
        if (newWidth == Width && newHeight == Height) return;

        // El nuevo tamaño tiene que seguir entrando en el padre
        if (X + newWidth > _parent.Width || Y + newHeight > _parent.Height)
            throw new ArgumentOutOfRangeException("El nuevo tamaño se sale del canvas padre.");

        // Limpiamos el rectángulo viejo antes de cambiar el tamaño
        ClearArea(0, 0, Width - 1, Height - 1);

        Width = newWidth;
        Height = newHeight;
    }

    /// <summary>
    /// Redimensiona y/o mueve el canvas virtual al rectángulo indicado, en coordenadas absolutas
    /// del padre (ambas esquinas inclusivas, igual que <see cref="TermCanvas.CreateSubCanvas(int,int,int,int)"/>).
    /// Limpia el rectángulo viejo para que no quede contenido en la posición anterior.
    /// </summary>
    /// <param name="x1">Columna absoluta base 0 donde empieza.</param>
    /// <param name="y1">Fila absoluta base 0 donde empieza.</param>
    /// <param name="x2">Columna absoluta base 0 donde termina (inclusive).</param>
    /// <param name="y2">Fila absoluta base 0 donde termina (inclusive).</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna coordenada es negativa o el
    /// rectángulo se sale del canvas padre.</exception>
    public void Resize(int x1, int y1, int x2, int y2)
    {
        if (x1 < 0 || y1 < 0 || x2 < 0 || y2 < 0)
            throw new ArgumentOutOfRangeException("Las coordenadas no pueden ser negativas.");

        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);

        if (x2 >= _parent.Width || y2 >= _parent.Height)
            throw new ArgumentOutOfRangeException("El rectángulo se sale del canvas padre.");

        int newWidth = x2 - x1 + 1;
        int newHeight = y2 - y1 + 1;
        if (x1 == X && y1 == Y && newWidth == Width && newHeight == Height) return; // mismo rectángulo

        // Limpiamos el rectángulo VIEJO (con posición/tamaño actuales)
        // para que no quede contenido dibujado en el lugar anterior
        ClearArea(0, 0, Width - 1, Height - 1);

        X = x1;
        Y = y1;
        Width = newWidth;
        Height = newHeight;
    }
}
