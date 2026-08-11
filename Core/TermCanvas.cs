/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TermFlow.Core;

/// <summary>
/// Motor de renderizado intermedio (Canvas Virtual) para consola.
/// Mantiene un mapa de la pantalla en memoria y genera un único string optimizado 
/// con secuencias ANSI para un solo <see cref="Console.Write(string)"/>, evitando el parpadeo.
/// Implementa renderizado diferencial (Dirty Tracking), actualizando únicamente 
/// las celdas que cambiaron desde el último frame para maximizar el rendimiento.
/// </summary>
public class TermCanvas : IDisposable
{
    /// <summary>
    /// Representa una celda individual del canvas virtual en memoria.
    /// Almacena el carácter visible y su código de color ANSI asociado.
    /// </summary>
    private struct Cell
    {
        /// <summary>
        /// El carácter visible a dibujar en la celda.
        /// </summary>
        public char Char;

        /// <summary>
        /// El código ANSI (color/estilo) aplicado al carácter.
        /// Se almacena como <see cref="string"/> para facilitar la acumulación de múltiples secuencias ANSI 
        /// (ej: color de frente + negrita) sin instanciar objetos complejos.
        /// </summary>
        public string ColorCode;
    }

    private Cell[,] _buffer;
    private bool[,] _dirty;

    private int _width;
    private int _height;
    private bool _anyDirty = true;

    /// <summary>
    /// Obtiene o establece la columna X (base 0) donde se posicionará el cursor real de la consola en el próximo <see cref="Flush"/>.
    /// </summary>
    public int? CursorX { get; set; } = null;

    /// <summary>
    /// Obtiene o establece la fila Y (base 0) donde se posicionará el cursor real de la consola en el próximo <see cref="Flush"/>.
    /// </summary>
    public int? CursorY { get; set; } = null;

    /// <summary>
    /// Obtiene o establece un valor que indica si el cursor real de la consola debe ser visible.
    /// </summary>
    public bool CursorVisible { get; set; } = false;

    /// <summary>
    /// Obtiene el ancho actual del canvas.
    /// </summary>
    public int Width { get { lock (_syncLock) return _width; } }

    /// <summary>
    /// Obtiene el alto actual del canvas.
    /// </summary>
    public int Height { get { lock (_syncLock) return _height; } }

    private int _lastWidth;
    private int _lastHeight;
    private CancellationTokenSource _resizeCts;
    private bool _automaticResize = false;
    private int _minDirtyY = int.MaxValue;
    private int _maxDirtyY = int.MinValue;

    private readonly Lock _syncLock = new();

    /// <summary>
    /// Inicializa una nueva instancia de <see cref="TermCanvas"/> con un tamaño fijo.
    /// </summary>
    /// <param name="width">Ancho inicial del canvas.</param>
    /// <param name="height">Alto inicial del canvas.</param>
    public TermCanvas(int width, int height)
        => Resize(width, height);

    /// <summary>
    /// Inicializa una nueva instancia de <see cref="TermCanvas"/> con soporte opcional para redimensionamiento automático.
    /// </summary>
    /// <param name="automaticResize">Si es <c>true</c>, el canvas detecta automáticamente cambios en el tamaño de la consola.</param>
    /// <param name="resizeIntervalms">Intervalo en milisegundos para comprobar cambios de tamaño.</param>
    /// <param name="onResize">Callback opcional que se invoca cuando la consola cambia de tamaño.</param>
    public TermCanvas(bool automaticResize = false, int resizeIntervalms = 250, Func<Task> onResize = null)
    {
        _automaticResize = automaticResize;
        if (automaticResize)
        {
            _resizeCts = new CancellationTokenSource();
            _lastHeight = Console.WindowHeight; _lastWidth = Console.WindowWidth;
            Resize(_lastWidth, _lastHeight);
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                try
                {
                    while (!_resizeCts.Token.IsCancellationRequested)
                    {
                        int width = Console.WindowWidth, height = Console.WindowHeight;
                        if (width != _lastWidth || height != _lastHeight)
                        {
                            _lastWidth = width;
                            _lastHeight = height;
                            Resize(_lastWidth, _lastHeight);

                            if (onResize is not null)
                                await onResize.Invoke();
                        }
                        await Task.Delay(resizeIntervalms, _resizeCts.Token);
                    }
                }
                catch (TaskCanceledException) when (_resizeCts.IsCancellationRequested) { }
            });
        }
        else
            Resize(Console.WindowWidth, Console.WindowHeight);
    }

    /// <summary>
    /// Redimensiona el canvas interno. Fuerza un redibujado completo en el próximo render.
    /// </summary>
    /// <param name="newWidth">Nuevo ancho.</param>
    /// <param name="newHeight">Nuevo alto.</param>
    public void Resize(int newWidth, int newHeight)
    {
        lock (_syncLock)
        {
            if (_width == newWidth && _height == newHeight && _buffer != null) return;

            _width = newWidth;
            _height = newHeight;
            _buffer = new Cell[newWidth, newHeight];
            _dirty = new bool[newWidth, newHeight];
            Clear(); // Marca todo como sucio para forzar el primer render
        }
    }

    /// <summary>
    /// Limpia todo el canvas interno, marcando todas las celdas como sucias.
    /// </summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if (_buffer[x, y].Char != ' ' || _buffer[x, y].ColorCode != ThemeColors.Reset)
                    {
                        _buffer[x, y].Char = ' ';
                        _buffer[x, y].ColorCode = ThemeColors.Reset;
                        _dirty[x, y] = true;
                    }
                }
            }
            _anyDirty = true;

            // Forzamos a que escanee toda la pantalla en el próximo Flush
            _minDirtyY = 0;
            _maxDirtyY = _height - 1;
        }
    }

    /// <summary>
    /// Escribe texto en una posición específica con un color determinado.
    /// Soporta secuencias ANSI (ej: \x1b[31m) dentro del string, aplicando el color 
    /// a los caracteres subsiguientes sin romper el posicionamiento del cursor lógico.
    /// </summary>
    /// <param name="x">Columna base 0 donde empezar a escribir.</param>
    /// <param name="y">Fila base 0 donde escribir.</param>
    /// <param name="text">Texto a escribir (puede contener ANSI).</param>
    /// <param name="color">Color inicial por defecto.</param>
    public void WriteAt(int x, int y, string text, AnsiColor color = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        lock (_syncLock)
        {
            if (y < 0 || y >= _height) return;

            bool madeDirty = false;
            string currentColorCode = color ?? ThemeColors.Reset;
            int currentX = x;

            // Usamos la extensión para iterar el string de forma limpia
            foreach (var (segment, isAnsi) in text.ParseAnsi())
            {
                if (isAnsi)
                {
                    // Lógica de acumulación de colores
                    if (segment == "\x1b[0m" || segment == "\x1b[m")
                        currentColorCode = ThemeColors.Reset;
                    else
                        currentColorCode = currentColorCode == ThemeColors.Reset ? segment : currentColorCode + segment;
                }
                else
                {
                    // Lógica de escritura de caracteres visibles
                    foreach (char c in segment)
                    {
                        if (currentX >= 0 && currentX < _width)
                        {
                            ref Cell cell = ref _buffer[currentX, y];

                            if (cell.Char != c || cell.ColorCode != currentColorCode)
                            {
                                cell.Char = c;
                                cell.ColorCode = currentColorCode;
                                _dirty[currentX, y] = true;
                                madeDirty = true;

                                // Actualizar los límites de filas sucias
                                if (y < _minDirtyY) _minDirtyY = y;
                                if (y > _maxDirtyY) _maxDirtyY = y;
                            }
                        }
                        currentX++; // Solo el cursor visible avanza
                    }
                }
            }

            if (madeDirty) _anyDirty = true;
        }
    }

    /// <summary>
    /// Limpia una fila completa en el canvas.
    /// </summary>
    /// <param name="y">Fila base 0 a limpiar.</param>
    public void ClearLine(int y)
    {
        lock (_syncLock)
        {
            if (y < 0 || y >= _height) return;

            bool madeDirty = false;
            for (int x = 0; x < _width; x++)
            {
                if (_buffer[x, y].Char != ' ' || _buffer[x, y].ColorCode != ThemeColors.Reset)
                {
                    _buffer[x, y].Char = ' ';
                    _buffer[x, y].ColorCode = ThemeColors.Reset;
                    _dirty[x, y] = true;
                    madeDirty = true;
                }
            }
            if (madeDirty) _anyDirty = true;
        }
    }

    /// <summary>
    /// Renderiza en la consola real. Recorre la matriz y SÓLO mueve el cursor 
    /// y escribe donde haya bloques de caracteres que cambiaron (Dirty).
    /// </summary>
    public void Flush()
    {
        string output;

        lock (_syncLock)
        {
            if (!_anyDirty) return;

            var sb = new StringBuilder(4096);
            string currentColorCode = ThemeColors.Reset;
            int? cursorX = null;
            int? cursorY = null;

            // OPTIMIZACIÓN: Solo recorrer desde la primer fila sucia hasta la última
            int startY = Math.Max(0, _minDirtyY);
            int endY = Math.Min(_height - 1, _maxDirtyY);

            for (int y = startY; y <= endY; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if (_dirty[x, y])
                    {
                        // Obtenemos el color de la celda. Si está vacío, usamos reset.
                        string cellColor = _buffer[x, y].ColorCode;
                        if (string.IsNullOrEmpty(cellColor)) cellColor = ThemeColors.Reset;

                        // Si no hay cursor seteado o veníamos de un salto, posicionamos
                        if (cursorX == null || cursorY == null || cursorX != x || cursorY != y)
                        {
                            // ANSI es base 1, sumamos 1 a las coordenadas
                            sb.Append($"\x1b[{y + 1};{x + 1}H");
                            cursorX = x;
                            cursorY = y;

                            sb.Append(ThemeColors.Reset);
                            sb.Append(cellColor);
                            currentColorCode = cellColor;
                        }
                        else if (cellColor != currentColorCode)
                        {
                            sb.Append(cellColor);
                            currentColorCode = cellColor;
                        }

                        // Escribimos el carácter
                        sb.Append(_buffer[x, y].Char);

                        // Avanzamos el cursor lógico un lugar
                        cursorX++;

                        // Limpiamos el flag dirty
                        _dirty[x, y] = false;
                    }
                    else
                        // Si la celda NO está sucia, rompemos la cadena de escritura.
                        cursorX = null;
                }
            }

            if (sb.Length > 0)
                sb.Append(ThemeColors.Reset);

            // --- Lógica de Cursor Real ---
            if (CursorVisible && CursorX.HasValue && CursorY.HasValue)
            {
                sb.Append($"\x1b[{CursorY.Value + 1};{CursorX.Value + 1}H");
                sb.Append("\x1b[?25h"); // Mostrar cursor
            }
            else
                sb.Append("\x1b[?25l"); // Ocultar cursor

            output = sb.Length > 0 ? sb.ToString() : null;
            _anyDirty = false;

            // Reseteamos los límites para el próximo frame
            _minDirtyY = int.MaxValue;
            _maxDirtyY = int.MinValue;
        }

        if (output != null)
            Console.Write(output);
    }

    /// <summary>
    /// Limpia un área rectangular desde (x1, y1) hasta (x2, y2).
    /// Solo marca como sucias las celdas que pasan de tener texto a estar vacías.
    /// </summary>
    /// <param name="x1">Columna inicial base 0.</param>
    /// <param name="y1">Fila inicial base 0.</param>
    /// <param name="x2">Columna final base 0.</param>
    /// <param name="y2">Fila final base 0.</param>
    public void ClearArea(int x1, int y1, int x2, int y2)
    {
        lock (_syncLock)
        {
            // Ordenamos las coordenadas por si vienen invertidas
            if (x1 > x2) (x1, x2) = (x2, x1);
            if (y1 > y2) (y1, y2) = (y2, y1);

            // Limitamos al tamaño del canvas (clamping)
            x1 = Math.Max(0, x1);
            y1 = Math.Max(0, y1);
            x2 = Math.Min(_width - 1, x2);
            y2 = Math.Min(_height - 1, y2);

            // Si las coordenadas quedaron fuera de rango, no hay nada que limpiar
            if (x1 > x2 || y1 > y2) return;

            bool madeDirty = false;

            for (int y = y1; y <= y2; y++)
            {
                for (int x = x1; x <= x2; x++)
                {
                    ref Cell cell = ref _buffer[x, y];
                    if (cell.Char != ' ' || cell.ColorCode != ThemeColors.Reset)
                    {
                        cell.Char = ' ';
                        cell.ColorCode = ThemeColors.Reset;
                        _dirty[x, y] = true;
                        madeDirty = true;

                        if (y < _minDirtyY) _minDirtyY = y;
                        if (y < _maxDirtyY) _maxDirtyY = y;
                    }
                }
            }
            if (madeDirty) _anyDirty = true;
        }
    }

    /// <summary>
    /// Limpia una línea desde una posición X hasta un largo determinado o el final de la consola (equivalente a \x1b[K).
    /// </summary>
    /// <param name="x">Columna base 0 desde donde empezar a limpiar.</param>
    /// <param name="y">Fila base 0 a limpiar.</param>
    /// <param name="length">Cantidad de caracteres a limpiar. Si es -1, limpia hasta el final de la fila.</param>
    public void ClearLineFrom(int x, int y, int length = -1)
    {
        // Si length es -1, vamos hasta el final de la pantalla. Si no, calculamos el final.
        int endX = length < 0 ? _width - 1 : x + length - 1;
        ClearArea(x, y, endX, y);
    }

    /// <summary>
    /// Escribe texto en una posición específica y luego limpia el resto de la línea 
    /// desde el final del texto hasta el ancho especificado o el final de la consola.
    /// </summary>
    /// <param name="x">Columna base 0 donde empezar a escribir.</param>
    /// <param name="y">Fila base 0 donde escribir.</param>
    /// <param name="text">Texto a escribir (puede contener ANSI).</param>
    /// <param name="color">Color inicial por defecto.</param>
    /// <param name="length">Largo de la zona a limpiar desde el final del texto. Si es -1, limpia hasta el final de la consola.</param>
    public void WriteAtAndClear(int x, int y, string text, AnsiColor color = null, int length = -1)
    {
        // 1. Escribimos el texto
        WriteAt(x, y, text, color);

        int visualLength = text?.GetVisualLength() ?? 0;
        int clearLength = length < 0 ? -1 : length;

        // 2. Limpiamos la linea restante
        ClearLineFrom(x + visualLength, y, clearLength);
    }

    /// <summary>
    /// Libera los recursos usados por el canvas, deteniendo el monitor de resize si está activo.
    /// </summary>
    public void Dispose()
    {
        if (_automaticResize)
        {
            _resizeCts.Cancel();
            _resizeCts.Dispose();
        }
    }
}
