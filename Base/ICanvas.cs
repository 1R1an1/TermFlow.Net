using TermFlow.Core;

namespace TermFlow.Base;

/// <summary>
/// Contrato común para superficies de dibujo de caracteres en 2D con coordenadas propias base 0.
/// Toda operación queda limitada al rectángulo de <see cref="Width"/> × <see cref="Height"/>:
/// lo que excede los bordes se recorta, sin tocar celdas de afuera.
/// La implementan <see cref="TermCanvas"/> y <see cref="VirtualCanvas"/>.
/// </summary>
public interface ICanvas
{
    /// <summary>Obtiene el ancho de la superficie, en celdas.</summary>
    public int Width { get; }

    /// <summary>Obtiene el alto de la superficie, en celdas.</summary>
    public int Height { get; }

    /// <summary>
    /// Escribe texto de forma horizontal a partir de una posición.
    /// Soporta secuencias ANSI (ej: \x1b[31m, <see cref="AnsiColor.Red"/>, <see cref="ThemeColors.Primary"/>)
    /// dentro del string, aplicando el color a los caracteres subsiguientes sin romper el
    /// posicionamiento del cursor lógico. Los caracteres que exceden el ancho se descartan.
    /// </summary>
    /// <param name="x">Columna base 0 donde empezar a escribir.</param>
    /// <param name="y">Fila base 0 donde escribir.</param>
    /// <param name="text">Texto a escribir (puede contener ANSI).</param>
    /// <param name="color">Color inicial por defecto.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="text"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="y"/> está fuera del rango de la superficie.</exception>
    public void WriteAt(int x, int y, string text, AnsiColor color = null);

    /// <summary>
    /// Escribe texto en vertical comenzando desde una posición.
    /// Soporta secuencias ANSI dentro del string, aplicando el color a los caracteres.
    /// Cada carácter se escribe en una fila consecutiva, manteniendo la misma columna.
    /// Los caracteres que exceden el alto se descartan.
    /// </summary>
    /// <param name="x">Columna base 0 donde escribir.</param>
    /// <param name="y">Fila base 0 donde comenzar a escribir verticalmente.</param>
    /// <param name="text">Texto a escribir verticalmente (cada carácter en una línea).</param>
    /// <param name="color">Color inicial por defecto.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="text"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="x"/> está fuera del rango de la superficie.</exception>
    public void WriteVertical(int x, int y, string text, AnsiColor color = null);

    /// <summary>
    /// Limpia un área rectangular desde (x1, y1) hasta (x2, y2) poniendo espacios y color <c>null</c>.
    /// Las coordenadas invertidas se ordenan y el área se recorta a los bordes de la superficie.
    /// </summary>
    /// <param name="x1">Columna inicial base 0.</param>
    /// <param name="y1">Fila inicial base 0.</param>
    /// <param name="x2">Columna final base 0.</param>
    /// <param name="y2">Fila final base 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna coordenada es negativa.</exception>
    public void ClearArea(int x1, int y1, int x2, int y2);

    /// <summary>
    /// Limpia una línea desde una posición X hasta un largo determinado o el final de la línea.
    /// </summary>
    /// <param name="x">Columna base 0 desde donde empezar a limpiar.</param>
    /// <param name="y">Fila base 0 a limpiar.</param>
    /// <param name="length">
    /// Cantidad de caracteres a limpiar.
    /// Si es mayor que 0, limpia exactamente esa cantidad de caracteres desde <paramref name="x"/>.
    /// Si es 0, limpia hasta el final de la línea.
    /// Si es menor que 0, limpia hasta el final de la línea dejando sin tocar los últimos <c>-length</c> caracteres.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="x"/> o <paramref name="y"/> están fuera de la superficie.</exception>
    public void ClearLineFrom(int x, int y, int length = 0);

    /// <summary>
    /// Limpia desde una posición (x, y) hasta el final de la superficie (equivalente por superficie a \x1b[J).
    /// </summary>
    /// <param name="x">Columna base 0.</param>
    /// <param name="y">Fila base 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si las coordenadas (x, y) están fuera de la superficie.</exception>
    public void ClearFromPoint(int x, int y);

    /// <summary>
    /// Limpia una fila completa de la superficie poniendo espacios y color <c>null</c>.
    /// </summary>
    /// <param name="y">Fila base 0 a limpiar.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="y"/> está fuera del rango de la superficie.</exception>
    public void ClearLine(int y);

    /// <summary>
    /// Redimensiona la superficie, borrando el contenido anterior.
    /// </summary>
    /// <param name="newWidth">Nuevo ancho, en celdas.</param>
    /// <param name="newHeight">Nuevo alto, en celdas.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si el ancho o el alto son menores o iguales a 0.</exception>
    public void Resize(int newWidth, int newHeight);

    /// <summary>Limpia toda la superficie.</summary>
    public void Clear();

    /// <summary>
    /// Crea un canvas virtual dentro de esta superficie, desde (x1, y1) hasta (x2, y2).
    /// </summary>
    /// <param name="x1">Columna inicial base 0.</param>
    /// <param name="y1">Fila inicial base 0.</param>
    /// <param name="x2">Columna final base 0 (inclusive).</param>
    /// <param name="y2">Fila final base 0 (inclusive).</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna coordenada es negativa.</exception>
    /// <returns>El canvas virtual creado.</returns>
    //public VirtualCanvas CreateSubCanvas(int x1, int y1, int x2, int y2);
}
