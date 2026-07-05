using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Core;

namespace TermFlow.Components.FullScreen
{
    public static class LivePanel
    {
        private class LogEntry
        {
            public long Id { get; }
            public string Content { get; set; }
            public bool IsDynamic { get; set; }
            public string Prefix { get; set; } = string.Empty;
            public string Suffix { get; set; } = string.Empty;
            public string FullText => $"{Prefix}{Content}{Suffix}";

            // --- SISTEMA DE CACHÉ PARA EVITAR RE-CÁLCULOS ---
            public List<string> CachedWrappedLines { get; private set; } = new();
            public int PhysicalLineCount => CachedWrappedLines.Count;
            private int _lastConsoleWidth = -1;

            public LogEntry(long id, string content, bool isDynamic)
            {
                Id = id;
                Content = content;
                IsDynamic = isDynamic;
            }

            public void RefreshCache(int consoleWidth, bool force = false)
            {
                if (force || _lastConsoleWidth != consoleWidth || CachedWrappedLines.Count == 0)
                {
                    CachedWrappedLines = FullText.WrapText(consoleWidth);
                    _lastConsoleWidth = consoleWidth;
                }
            }
        }

        private static List<LogEntry> _history = new();

        // Búsqueda O(1) ultra rápida
        private static Dictionary<long, LogEntry> _entryLookup = new();
        private static long _nextId = 0;
        private static int _scrollOffset = 0; // Líneas físicas scrolleadas
        private static SemaphoreSlim _renderSignal = new(0, 1);
        private static int _renderPending;
        private static readonly ConcurrentQueue<ConsoleKeyInfo> _keyQueue = new();
        private static readonly SemaphoreSlim _keySignal = new(0);
        private static CancellationTokenSource _cts;
        private static bool _isActive = false;
        private static int? _maxLogs;
        private static readonly object _lock = new();

        public static bool IsActive => _isActive;

        public static void Start(int? maxLogs = null)
        {
            if (_isActive) return;

            _maxLogs = maxLogs;
            _isActive = true;

            _cts = new CancellationTokenSource();

            while (_keyQueue.TryDequeue(out _)) { }
            while (_keySignal.Wait(0)) { }

            Engine.EnterFullScreen();

            _ = Task.Run(() => RenderLoop(_cts.Token));
            _ = Task.Run(() => InputLoop(_cts.Token));
        }

        public static void Stop()
        {
            if (!_isActive) return;
            _isActive = false;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            Engine.ExitFullScreen();

            lock (_lock)
            {
                _history.Clear();
                _entryLookup.Clear();
                _nextId = 0;
                _scrollOffset = 0;

                while (_keyQueue.TryDequeue(out _)) { }
                while (_keySignal.Wait(0)) { }
            }
        }

        internal static void AddLog(string content)
        {
            lock (_lock)
            {
                long id = Interlocked.Increment(ref _nextId);
                var entry = new LogEntry(id, content, false);

                // Pre-calcular caché al instante
                entry.RefreshCache(Console.WindowWidth);

                _history.Add(entry);
                _entryLookup[id] = entry;

                if (_maxLogs.HasValue)
                {
                    while (_history.Count > _maxLogs.Value)
                    {
                        var removed = _history[0];
                        _history.RemoveAt(0);
                        _entryLookup.Remove(removed.Id);
                    }
                }

                if (_scrollOffset > 0) _scrollOffset += entry.PhysicalLineCount;
            }
            RequestRender();
        }

        public static long AddDynamic(string initialContent)
        {
            long id;
            lock (_lock)
            {
                id = Interlocked.Increment(ref _nextId);
                var entry = new LogEntry(id, initialContent, true);

                // Pre-calcular caché
                entry.RefreshCache(Console.WindowWidth);

                _history.Add(entry);
                _entryLookup[id] = entry;

                if (_maxLogs.HasValue)
                {
                    while (_history.Count > _maxLogs.Value)
                    {
                        var removed = _history[0];
                        _history.RemoveAt(0);
                        _entryLookup.Remove(removed.Id);
                    }
                }

                if (_scrollOffset > 0) _scrollOffset += entry.PhysicalLineCount;
            }
            RequestRender();
            return id;
        }

        public static void UpdateLine(long id, string newContent) => ApplyUpdate(id, l => l.Content = newContent);

        public static void UpdateDecorations(long id, string prefix = null, string suffix = null) => ApplyUpdate(id, entry =>
        {
            if (prefix != null) entry.Prefix = prefix;
            if (suffix != null) entry.Suffix = suffix;
        });
        public static (string prefix, string suffix) GetDecorations(long id)
        {
            lock (_lock)
            {
                return _entryLookup.TryGetValue(id, out var entry)
                    ? (entry.Prefix, entry.Suffix)
                    : (string.Empty, string.Empty);
            }
        }


        private static void ApplyUpdate(long id, Action<LogEntry> updateAction)
        {
            lock (_lock)
            {
                // 1. Búsqueda O(1) instantánea (Sin recorrer listas)
                if (!_entryLookup.TryGetValue(id, out var entry)) return;

                int width = Console.WindowWidth;

                // 2. Líneas antes (leyendo de la caché, rapidísimo)
                int oldLines = entry.PhysicalLineCount;

                // 3. Ejecutar modificación
                updateAction(entry);

                // 4. Forzar re-cálculo de la caché SOLO para este elemento
                entry.RefreshCache(width, force: true);

                // 5. Líneas después
                int newLines = entry.PhysicalLineCount;

                // 6. Ajustar scroll
                int difference = newLines - oldLines;
                if (_scrollOffset > 0 && difference != 0)
                {
                    _scrollOffset += difference;
                }
            }
            RequestRender();
        }

        private static void RequestRender()
        {
            if (Interlocked.Exchange(ref _renderPending, 1) == 0)
                _renderSignal.Release();
        }

        private static async Task RenderLoop(CancellationToken token)
        {
            try
            {
                int lastWidth = Console.WindowWidth;
                int lastHeight = Console.WindowHeight;
                var sb = new StringBuilder(4096);

                while (!token.IsCancellationRequested)
                {
                    await _renderSignal.WaitAsync(token);
                    Interlocked.Exchange(ref _renderPending, 0);

                    int width = Console.WindowWidth;
                    int height = Console.WindowHeight;

                    // Si la consola cambia de tamaño, recalculamos todo el historial
                    bool widthChanged = false;
                    if (width != lastWidth || height != lastHeight)
                    {
                        lastWidth = width;
                        lastHeight = height;
                        widthChanged = true;
                        sb.Append("\x1b[2J"); // Limpiar pantalla
                    }

                    List<List<string>> wrappedLines = new List<List<string>>();
                    int totalLines = 0;
                    int currentScroll;

                    lock (_lock)
                    {
                        foreach (var entry in _history)
                        {
                            if (widthChanged) entry.RefreshCache(width);

                            wrappedLines.Add(entry.CachedWrappedLines);
                            totalLines += entry.PhysicalLineCount;
                        }

                        int maxScroll = Math.Max(0, totalLines - height);
                        if (_scrollOffset > maxScroll) _scrollOffset = maxScroll;
                        if (_scrollOffset < 0) _scrollOffset = 0;
                        currentScroll = _scrollOffset;
                    }

                    // Construir buffer visible
                    sb.Clear();
                    sb.Append("\x1b[H");

                    int lineIndex = 0;
                    int visibleStart = Math.Max(0, totalLines - height - currentScroll);
                    int visibleEnd = Math.Min(totalLines, visibleStart + height);

                    for (int i = 0; i < wrappedLines.Count && lineIndex < visibleEnd; i++)
                    {
                        var lines = wrappedLines[i];
                        for (int j = 0; j < lines.Count; j++)
                        {
                            if (lineIndex >= visibleStart && lineIndex < visibleEnd)
                            {
                                sb.Append(lines[j]).Append("\x1b[K");
                                if (lineIndex < visibleEnd - 1) sb.Append("\n");
                            }
                            lineIndex++;
                            if (lineIndex >= visibleEnd) break;
                        }
                    }

                    sb.Append("\x1b[J");
                    Console.Write(sb.ToString());
                }
            }
            catch (OperationCanceledException) { }
        }

        private static async Task InputLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var evt = InputReader.ReadInput();

                    int delta = 0;
                    if (evt.Type == InputEventType.ScrollUp || evt.KeyInfo.Key == ConsoleKey.UpArrow)
                        delta = (evt.Type == InputEventType.ScrollUp) ? 3 : 1;
                    else if (evt.Type == InputEventType.ScrollDown || evt.KeyInfo.Key == ConsoleKey.DownArrow)
                        delta = (evt.Type == InputEventType.ScrollDown) ? -3 : -1;
                    else if (evt.KeyInfo.Key == ConsoleKey.PageUp)
                        delta = int.MaxValue;  // Señal para ir al máximo scroll
                    else if (evt.KeyInfo.Key == ConsoleKey.PageDown)
                        delta = int.MinValue;  // Señal para ir al scroll 0

                    if (delta != 0)
                    {
                        lock (_lock)
                        {
                            int totalLines = _history.Sum(e => e.PhysicalLineCount);
                            int maxScroll = Math.Max(0, totalLines - Console.WindowHeight);

                            if (delta == int.MaxValue)
                                _scrollOffset = maxScroll;   // PageUp
                            else if (delta == int.MinValue)
                                _scrollOffset = 0;           // PageDown
                            else
                                _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, maxScroll);
                        }
                        RequestRender();
                    }

                    if (evt.Type == InputEventType.Key)
                    {
                        _keyQueue.Enqueue(evt.KeyInfo);
                        _keySignal.Release(); // Sube el contador del semáforo y despierta la tarea
                    }

                    await Task.Delay(7, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        internal static async Task<ConsoleKeyInfo> WaitForKeyAsync(CancellationToken token = default)
        {
            if (!_isActive) return default;

            while (_keyQueue.TryDequeue(out _)) { }
            while (_keySignal.Wait(0)) { }

            try
            {
                await _keySignal.WaitAsync(token).ConfigureAwait(false);
                _keyQueue.TryDequeue(out var key);
                return key;
            }
            catch (OperationCanceledException) { return default; }
        }

        internal static ConsoleKeyInfo WaitForKey(CancellationToken token = default)
        {
            if (!_isActive) return default;

            while (_keyQueue.TryDequeue(out _)) { }
            while (_keySignal.Wait(0)) { }

            try
            {
                _keySignal.Wait(token);
                _keyQueue.TryDequeue(out var key);
                return key;
            }
            catch (OperationCanceledException) { return default; }
        }
    }
}