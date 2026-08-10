using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TermFlow.Core;

public class TermCanvas : IDisposable
{
    private struct Cell
    {
        public char Char;
        public AnsiColor Color;
    }

    private Cell[,] _buffer;
    private bool[,] _dirty;

    private int _width;
    private int _height;
    private bool _anyDirty = true;

    public int Width { get { lock (_syncLock) return _width; } }
    public int Height { get { lock (_syncLock) return _height; } }

    private int _lastWidth;
    private int _lastHeight;
    private CancellationTokenSource _resizeCts;
    private bool _automaticResize = false;

    private readonly Lock _syncLock = new();

    public TermCanvas(int width, int height)
        => Resize(width, height);

    public TermCanvas(bool automaticResize = false, Func<Task> onResize = null)
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
                        await Task.Delay(250, _resizeCts.Token);
                    }
                }
                catch (TaskCanceledException) when (_resizeCts.IsCancellationRequested) { }
            });
        }
        else
            Resize(Console.WindowWidth, Console.WindowHeight);
    }

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

    public void Clear()
    {
        lock (_syncLock)
        {
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    // Solo marcar como sucio si pasa de tener algo a estar vacío
                    if (_buffer[x, y].Char != ' ' || _buffer[x, y].Color != ThemeColors.Reset)
                    {
                        _buffer[x, y].Char = ' ';
                        _buffer[x, y].Color = ThemeColors.Reset;
                        _dirty[x, y] = true;
                    }
                }
            }
            _anyDirty = true;
        }
    }

    public void WriteAt(int x, int y, string text, AnsiColor color)
    {
        if (string.IsNullOrEmpty(text)) return;

        lock (_syncLock)
        {
            if (y < 0 || y >= _height) return;

            bool madeDirty = false;
            for (int i = 0; i < text.Length; i++)
            {
                int currentX = x + i;
                if (currentX < 0 || currentX >= _width) continue;

                ref Cell cell = ref _buffer[currentX, y];

                if (cell.Char != text[i] || cell.Color != color)
                {
                    cell.Char = text[i];
                    cell.Color = color;
                    _dirty[currentX, y] = true;
                    madeDirty = true;
                }
            }

            if (madeDirty) _anyDirty = true;
        }
    }

    public void ClearLine(int y)
    {
        lock (_syncLock)
        {
            if (y < 0 || y >= _height) return;

            bool madeDirty = false;
            for (int x = 0; x < _width; x++)
            {
                if (_buffer[x, y].Char != ' ' || _buffer[x, y].Color != ThemeColors.Reset)
                {
                    _buffer[x, y].Char = ' ';
                    _buffer[x, y].Color = ThemeColors.Reset;
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

            var sb = new StringBuilder(256);
            AnsiColor currentColor = ThemeColors.Reset;
            int? cursorX = null;
            int? cursorY = null;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if (_dirty[x, y])
                    {
                        // Si no hay cursor seteado o veníamos de un salto, posicionamos
                        if (cursorX == null || cursorY == null || cursorX != x || cursorY != y)
                        {
                            // ANSI es base 1, sumamos 1 a las coordenadas
                            sb.Append($"\x1b[{y + 1};{x + 1}H");
                            cursorX = x;
                            cursorY = y;
                        }

                        // Optimización de color
                        if (_buffer[x, y].Color != currentColor)
                        {
                            sb.Append(_buffer[x, y].Color);
                            currentColor = _buffer[x, y].Color;
                        }

                        // Escribimos el carácter
                        sb.Append(_buffer[x, y].Char);

                        // Avanzamos el cursor lógico un lugar
                        cursorX++;

                        // Limpiamos el flag dirty
                        _dirty[x, y] = false;
                    }
                    else
                    {
                        // Si la celda NO está sucia, rompemos la cadena de escritura.
                        // El próximo carácter sucio obligará a reposicionar el cursor.
                        cursorX = null;
                    }
                }
            }

            if (sb.Length > 0)
            {
                sb.Append(ThemeColors.Reset);
                output = sb.ToString();
            }
            else
                output = null;

            _anyDirty = false;
        }

        if (output != null)
            Console.Write(output);
    }

    public void Dispose()
    {
        if (_automaticResize)
        {
            _resizeCts.Cancel();
            _resizeCts.Dispose();
        }
    }
}
