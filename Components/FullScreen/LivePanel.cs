using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

            public LogEntry(long id, string content, bool isDynamic)
            {
                Id = id;
                Content = content;
                IsDynamic = isDynamic;
            }
        }

        private static List<LogEntry> _history = new();
        private static long _nextId = 0;
        private static int _scrollOffset = 0; // Líneas físicas scrolleadas
        private static SemaphoreSlim _renderSignal = new(0, 1);
        private static readonly ConcurrentQueue<ConsoleKeyInfo> _keyQueue = new();
        private static readonly SemaphoreSlim _keySignal = new(0);
        private static CancellationTokenSource _cts;
        private static bool _isActive = false;
        private static readonly object _lock = new();

        public static bool IsActive => _isActive;

        public static void Start()
        {
            if (_isActive) return;
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
                _history.Add(entry);

                int physicalLines = content.CountPhysicalLines(Console.WindowWidth);
                if (_scrollOffset > 0) _scrollOffset += physicalLines;
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
                _history.Add(entry);

                // Siempre 1 línea física (asumimos que los dinámicos son de una línea)
                if (_scrollOffset > 0) _scrollOffset += 1;
            }
            RequestRender();
            return id;
        }

        internal static void UpdateLine(long id, string newContent)
        {
            lock (_lock)
            {
                for (int i = 0; i < _history.Count; i++)
                {
                    if (_history[i].Id == id)
                    {
                        _history[i].Content = newContent;
                        break;
                    }
                }
            }
            RequestRender();
        }

        private static void RequestRender()
        {
            if (_renderSignal.CurrentCount == 0)
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

                    // Detectar redimensionamiento
                    if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
                    {
                        lastWidth = Console.WindowWidth;
                        lastHeight = Console.WindowHeight;
                        // No es necesario limpiar con \x1b[2J porque sobrescribimos todo
                    }

                    int width = lastWidth;
                    int height = lastHeight;

                    // Calcular total de líneas físicas y ajustar scroll
                    List<List<string>> wrappedLines = new List<List<string>>();
                    int totalLines = 0;
                    int currentScroll;
                    lock (_lock)
                    {
                        foreach (var entry in _history)
                        {
                            var lines = entry.Content.WrapText(width);
                            wrappedLines.Add(lines);
                            totalLines += lines.Count;
                        }

                        int maxScroll = Math.Max(0, totalLines - height);

                        // Si _scrollOffset es mayor que maxScroll, ajustar
                        if (_scrollOffset > maxScroll)
                            _scrollOffset = maxScroll;

                        // Si _scrollOffset es negativo, poner en 0
                        if (_scrollOffset < 0)
                            _scrollOffset = 0;

                        currentScroll = _scrollOffset;
                    }


                    // Construir buffer visible
                    sb.Clear();
                    sb.Append("\x1b[H");

                    int lineIndex = 0;
                    int visibleStart = Math.Max(0, totalLines - height - _scrollOffset);
                    int visibleEnd = Math.Min(totalLines, visibleStart + height);
                    int remaining = height - (visibleEnd - visibleStart);

                    for (int i = 0; i < wrappedLines.Count && lineIndex < visibleEnd; i++)
                    {
                        var lines = wrappedLines[i];
                        for (int j = 0; j < lines.Count; j++)
                        {
                            if (lineIndex >= visibleStart && lineIndex < visibleEnd)
                            {
                                sb.Append(lines[j]).Append("\x1b[K");
                                if (remaining > 0 || lineIndex < visibleEnd - 1) sb.Append("\n");

                            }
                            lineIndex++;
                            if (lineIndex >= visibleEnd) break;
                        }
                    }

                    // Limpiar líneas sobrantes
                    for (int i = 0; i < remaining; i++)
                    {
                        sb.Append("\x1b[K");
                        if (i < remaining - 1) sb.Append("\n");
                    }

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

                    if (evt.Type == InputEventType.ScrollUp ||
                        (evt.Type == InputEventType.Key && evt.KeyInfo.Key == ConsoleKey.UpArrow))
                    {
                        lock (_lock)
                        {
                            // Calcular total de líneas físicas
                            int totalLines = 0;
                            foreach (var entry in _history)
                            {
                                totalLines += entry.Content.CountPhysicalLines(Console.WindowWidth);
                            }
                            int maxScroll = Math.Max(0, totalLines - Console.WindowHeight);
                            if (_scrollOffset < maxScroll)
                                _scrollOffset++;
                        }
                        RequestRender();
                    }
                    else if (evt.Type == InputEventType.ScrollDown ||
                             (evt.Type == InputEventType.Key && evt.KeyInfo.Key == ConsoleKey.DownArrow))
                    {
                        lock (_lock)
                        {
                            if (_scrollOffset > 0)
                                _scrollOffset--;
                        }
                        RequestRender();
                    }

                    if (evt.Type == InputEventType.Key)
                    {
                        _keyQueue.Enqueue(evt.KeyInfo);
                        _keySignal.Release(); // Sube el contador del semáforo y despierta la tarea
                    }

                    await Task.Delay(15, token);
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