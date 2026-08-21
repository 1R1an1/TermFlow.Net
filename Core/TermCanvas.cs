/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;
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

    private Cell[,] _buffer;       // Back  Buffer: lo que queremos dibujar
    private Cell[,] _frontBuffer;  // Front Buffer: lo que la terminal realmente tiene ahora
    private bool[,] _dirty;

    private int _width;
    private int _height;
    private bool _anyDirty = true;
    private bool _forceClearScreen = false;
    private (int X, int Y)? _pendingClearScreen = null; // Guarda la Y para el \x1b[J
    private readonly Dictionary<int, int> _pendingClearLineX = new(); // Y -> X para el \x1b[K

    /// <summary>
    /// Obtiene o establece donde se posicionará el cursor real (base 0) de la consola en el próximo <see cref="Flush"/>.
    /// </summary>
    public (int X, int Y)? CursorPos { get; set; } = null;

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

    private volatile int _lastWidth;
    private volatile int _lastHeight;
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
        => Init(width, height);

    /// <summary>
    /// Inicializa una nueva instancia de <see cref="TermCanvas"/> con soporte opcional para redimensionamiento automático.
    /// </summary>
    /// <param name="automaticResize">Si es <c>true</c>, el canvas detecta automáticamente cambios en el tamaño de la consola.</param>
    /// <param name="resizeIntervalms">Intervalo en milisegundos para comprobar cambios de tamaño.</param>
    /// <param name="onResize">Callback opcional que se invoca cuando la consola cambia de tamaño, recibiendo la instancia del canvas y el lock de sincronización interno.</param>
    public TermCanvas(bool automaticResize = false, int resizeIntervalms = 250, Func<TermCanvas, Lock, Task> onResize = null)
    {
        _automaticResize = automaticResize;
        if (automaticResize)
        {
            _resizeCts = new CancellationTokenSource();
            _lastHeight = Console.WindowHeight; _lastWidth = Console.WindowWidth;
            Init(_lastWidth, _lastHeight);
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                try
                {
                    while (!_resizeCts.Token.IsCancellationRequested)
                    {
                        int width = Console.WindowWidth, height = Console.WindowHeight;
                        if (width != _lastWidth || height != _lastHeight)
                        {
                            lock (_syncLock)
                            {
                                _lastWidth = width;
                                _lastHeight = height;
                                Resize(_lastWidth, _lastHeight);
                            }
                            if (onResize is not null)
                                await onResize.Invoke(this, _syncLock);
                        }
                        await Task.Delay(resizeIntervalms, _resizeCts.Token);
                    }
                }
                catch (TaskCanceledException) when (_resizeCts.IsCancellationRequested) { }
            });
        }
        else
            Init(Console.WindowWidth, Console.WindowHeight);
    }

    /// <summary>
    /// Redimensiona el canvas interno borrando todo el contenido anterior.
    /// Marca el flag <see cref="_forceClearScreen"/> para que el próximo <see cref="Flush"/> limpie la terminal.
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
            _frontBuffer = new Cell[newWidth, newHeight];
            _buffer = new Cell[newWidth, newHeight];
            _dirty = new bool[newWidth, newHeight];
            _forceClearScreen = true;
            _anyDirty = false;
            _minDirtyY = int.MaxValue;
            _maxDirtyY = int.MinValue;
        }
    }

    /// <summary>
    /// Inicializa los arrays del canvas. Aprovecha la inicialización por defecto de C# ('\0' y null) evitando bucles innecesarios.
    /// </summary>
    /// <param name="inicialWidth">Ancho inicial.</param>
    /// <param name="inicialHeight">Alto inicial.</param>
    private void Init(int inicialWidth, int inicialHeight)
    {
        lock (_syncLock)
        {
            if (_width == inicialWidth && _height == inicialHeight && _buffer != null) return;

            _width = inicialWidth;
            _height = inicialHeight;
            _frontBuffer = new Cell[inicialWidth, inicialHeight];
            _buffer = new Cell[inicialWidth, inicialHeight];
            _dirty = new bool[inicialWidth, inicialHeight];
            _forceClearScreen = true;
        }
    }

    /// <summary>
    /// Actualiza el valor de una celda individual. 
    /// Compara contra el Front Buffer para saber si cambió respecto a la consola real.
    /// Si el carácter es un espacio y la celda ya estaba vacía ('\0' o ' '), no hace nada para evitar ensuciar la celda.
    /// </summary>
    /// <param name="x">Columna base 0.</param>
    /// <param name="y">Fila base 0.</param>
    /// <param name="c">Nuevo carácter.</param>
    /// <param name="colorCode">Nuevo código de color ANSI. Se suele pasar <c>null</c> para limpiar.</param>
    /// <param name="forceDirty">Si no es <c>null</c>, fuerza el estado de suciedad de la celda al valor indicado.</param>
    private void SetCell(int x, int y, char c, string colorCode, bool? forceDirty = null)
    {
        Cell front = _frontBuffer[x, y];

        // Solo actualizamos y marcamos como sucia si ALGO cambió realmente respecto a la consola real
        if ((c == ' ' ? front.Char != ' ' && front.Char != '\0' : front.Char != c) || front.ColorCode != colorCode)
        {
            SetCell(x, y, c, colorCode, dirty: forceDirty.HasValue ? forceDirty.Value : true);
            _anyDirty = true;

            // Actualizar los límites de filas sucias
            if (y < _minDirtyY) _minDirtyY = y;
            if (y > _maxDirtyY) _maxDirtyY = y;
        }
        else if (_buffer[x, y] is var cell && (c == ' ' ? cell.Char != ' ' && cell.Char != '\0' : cell.Char != c) || cell.ColorCode != colorCode)
            SetCell(x, y, c, colorCode, dirty: false);
    }
    /// <summary>Modifica la celda posicionada en <paramref name="x"/>, <paramref name="y"/> con los parametros introducidos</summary>
    private void SetCell(int x, int y, char c, string colorCode, bool dirty)
    {
        _buffer[x, y].Char = c;
        _buffer[x, y].ColorCode = colorCode;
        _dirty[x, y] = dirty;
    }

    /// <summary>Modifica la celda fisica posicionada en <paramref name="x"/>, <paramref name="y"/> con los parametros introducidos</summary>
    private void SetFront(int x, int y, char c, string colorCode)
    {
        _frontBuffer[x, y].Char = c;
        _frontBuffer[x, y].ColorCode = colorCode;
    }

    /// <summary>Modifica la celda virtual y la celda fisica posicionada en <paramref name="x"/>, <paramref name="y"/> con los parametros introducidos</summary>
    private void SetAll(int x, int y, char c, string colorCode, bool? forceDirty = null)
    {
        SetCell(x, y, c, colorCode, forceDirty);
        SetFront(x, y, c, colorCode);
    }

    /// <summary>
    /// Limpia todo el canvas interno usando <see cref="Array.Clear"/>, reiniciando todo a '\0' y <c>null</c>.
    /// Fuerza un borrado total de la pantalla en el próximo <see cref="Flush"/>.
    /// </summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            Array.Clear(_frontBuffer);
            Array.Clear(_buffer);
            Array.Clear(_dirty);

            _anyDirty = false;
            _forceClearScreen = true;
            _minDirtyY = int.MaxValue;
            _maxDirtyY = int.MinValue;
        }
    }

    /// <summary>
    /// Escribe texto en una posición específica con un color determinado.
    /// Soporta secuencias ANSI (ej: \x1b[31m, <see cref="AnsiColor.Red"/>, <see cref="ThemeColors.Primary"/>) dentro del string, aplicando el color 
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
                            SetCell(currentX, y, c, currentColorCode);
                        currentX++; // Solo el cursor visible avanza
                    }
                }
            }
        }
    }

    /// <summary>
    /// Limpia una fila completa en el canvas poniendo espacios y color <c>null</c>.
    /// </summary>
    /// <param name="y">Fila base 0 a limpiar.</param>
    public void ClearLine(int y)
    {
        lock (_syncLock)
        {
            if (y < 0 || y >= _height) return;

            // Actualizamos el buffer interno para que se sepa que está vacío
            for (int x = 0; x < _width; x++)
            {
                SetAll(x, y, '\0', null, forceDirty: false);
                _pendingClearLineX[y] = 0;
            }
        }
    }

    /// <summary>
    /// Renderiza en la consola real. Recorre la matriz y SÓLO mueve el cursor 
    /// y escribe donde haya bloques de caracteres que cambiaron (Dirty).
    /// Si <see cref="_forceClearScreen"/> está activo, manda un ANSI Clear limpiando la consola entera antes de renderizar.
    /// </summary>
    public void Flush()
    {
        string output;

        lock (_syncLock)
        {
            if (!_anyDirty && !_forceClearScreen) return;

            var sb = new StringBuilder(4096);

            // Si se pidió un Clear o Resize, mandamos el comando ANSI de borrar todo.
            if (_forceClearScreen)
            {
                sb.Append("\x1b[2J\x1b[H"); // Borrar pantalla y mover cursor a 0,0
                _forceClearScreen = false;
            }

            if (_anyDirty)
            {
                string currentColorCode = ThemeColors.Reset;
                int? cursorX = null;
                int? cursorY = null;

                int startY = Math.Max(0, _minDirtyY);
                if (_pendingClearScreen.HasValue && _pendingClearScreen.Value.Y < startY)
                    startY = _pendingClearScreen.Value.Y;
                int endY = Math.Min(_height - 1, _maxDirtyY);

                for (int y = startY; y <= endY; y++)
                {
                    // --- INYECCIÓN DEL COMANDO \x1b[J ---
                    if (_pendingClearScreen.HasValue && _pendingClearScreen.Value.Y == y)
                    {
                        // Posicionamos el cursor en (X, Y) y mandamos el ANSI J
                        sb.Append($"\x1b[{y + 1};{_pendingClearScreen.Value.X + 1}H\x1b[J");
                        _pendingClearScreen = null;
                    }
                    // --- INYECCIÓN DEL COMANDO \x1b[K (Si lo hubiera para esta línea) ---
                    if (_pendingClearLineX.TryGetValue(y, out int clearX))
                    {
                        sb.Append($"\x1b[{y + 1};{clearX + 1}H\x1b[K");
                        _pendingClearLineX.Remove(y);
                    }

                    for (int x = 0; x < _width; x++)
                    {
                        if (_dirty[x, y])
                        {
                            // Obtenemos el color de la celda. Si está vacío (null), usamos reset.
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
                                sb.Append(ThemeColors.Reset);
                                sb.Append(cellColor);
                                currentColorCode = cellColor;
                            }

                            // Escribimos el carácter y lo guardamos en _frontBuffer
                            sb.Append(_buffer[x, y].Char);
                            SetFront(x, y, _buffer[x, y].Char, _buffer[x, y].ColorCode);

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
                // Limpiamos cualquier comando de línea que haya quedado fuera del rango startY-endY
                _pendingClearLineX.Clear();

                if (sb.Length > 0)
                    sb.Append(ThemeColors.Reset);

                _anyDirty = false;

                // Reseteamos los límites para el próximo frame
                _minDirtyY = int.MaxValue;
                _maxDirtyY = int.MinValue;
            }

            // --- Lógica de Cursor Real ---
            if (CursorVisible)
            {
                if (CursorPos.HasValue)
                    sb.Append($"\x1b[{CursorPos.Value.Y + 1};{CursorPos.Value.X + 1}H");
                sb.Append("\x1b[?25h"); // Mostrar cursor
            }
            else
                sb.Append("\x1b[?25l"); // Ocultar cursor
            output = sb.Length > 0 ? sb.ToString() : null;
        }
        if (output != null)
            Console.Write(output);
    }

    /// <summary>
    /// Limpia un área rectangular desde (x1, y1) hasta (x2, y2) poniendo espacios y color <c>null</c>.
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

            // OPTIMIZACIÓN: Si el área cubre todo el ancho de la consola, usamos \x1b[K
            if (x1 == 0 && x2 == _width - 1)
            {
                for (int y = y1; y <= y2; y++)
                {
                    for (int x = 0; x < _width; x++)
                        SetAll(x, y, '\0', null, forceDirty: false);

                    _pendingClearLineX[y] = 0;
                }
            }
            else
                for (int y = y1; y <= y2; y++)
                    for (int x = x1; x <= x2; x++)
                        SetCell(x, y, ' ', null);
        }
    }

    /// <summary>
    /// Limpia una línea desde una posición X hasta un largo determinado o el final de la consola (equivalente a \x1b[K).
    /// </summary>
    /// <param name="x">Columna base 0 desde donde empezar a limpiar.</param>
    /// <param name="y">Fila base 0 a limpiar.</param>
    /// <param name="length">
    /// Cantidad de caracteres a limpiar.
    /// Si es mayor que 0, limpia exactamente esa cantidad de caracteres desde <paramref name="x"/>.
    /// Si es 0, limpia desde <paramref name="x"/> hasta el final de la línea.
    /// Si es menor que 0, limpia desde <paramref name="x"/> hasta el final de la línea,
    /// dejando sin tocar los últimos <c>-length</c> caracteres.
    /// </param>
    public void ClearLineFrom(int x, int y, int length = 0)
    {
        lock (_syncLock)
        {
            if (y < 0 || y >= _height || x < 0) return;
            if (length == 0)
            {
                for (int i = x; i < _width; i++)
                {
                    SetAll(i, y, '\0', null, forceDirty: false);
                    _pendingClearLineX[y] = x;
                }
            }
            else
            {
                int endX = length < 0 ? _width - -length - 1 : x + length - 1;
                if (endX >= x) ClearArea(x, y, endX, y);
            }
        }
    }

    /// <summary>
    /// Limpia desde una posición (x, y) hasta el final de la consola (equivalente a \x1b[J).
    /// Programa el comando ANSI para ejecutarse en el próximo Flush.
    /// </summary>
    /// <param name="x">Columna base 0.</param>
    /// <param name="y">Fila base 0.</param>
    public void ClearFromPoint(int x, int y)
    {
        lock (_syncLock)
        {
            if (y < 0 || y >= _height || x < 0 || x >= _width) return;

            // Actualizamos el buffer interno a vacío, pero NO lo ensuciamos 
            // porque el comando ANSI físico se encargará de borrarlo en la terminal.
            for (int j = y; j < _height; j++)
                for (int i = (j == y) ? x : 0; i < _width; i++)
                    SetAll(i, j, '\0', null, forceDirty: false);

            // Guardamos la coordenada para que el Flush mande el \x1b[J
            _pendingClearScreen = (x, y);
        }
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
    public void WriteAtAndClear(int x, int y, string text, AnsiColor color = null, int length = 0)
    {
        // 1. Escribimos el texto
        WriteAt(x, y, text, color);
        int visualLength = text?.GetVisualLength() ?? 0;

        // 2. Limpiamos la linea restante
        ClearLineFrom(x + visualLength, y, length);
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
