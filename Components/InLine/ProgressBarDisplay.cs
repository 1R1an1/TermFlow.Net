using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    public interface IProgressTask
    {
        long Value { get; set; }
    }

    public static class ProgressBarDisplay
    {
        private class ProgressTaskImpl : IProgressTask
        {
            public long _value; // El campo real en memoria

            public long Value
            {
                get => Interlocked.Read(ref _value);
                set => Interlocked.Exchange(ref _value, value);
            }
        }

        public static async Task RunAsync(string description, long maxValue, Func<IProgressTask, Task> workerTask, string finalText = null, long? panelId = null, bool showSpeed = true, CancellationToken token = default)
        {
            using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var taskState = new ProgressTaskImpl();
            long _panelId = LivePanel.IsActive ? panelId ?? LivePanel.AddDynamic($"{description} 0%") : -1;

            var stopwatch = Stopwatch.StartNew();

            double lastMetricsUpdate = 0;
            double metricsInterval = 0.25; // 300ms

            double lastValue = 0;
            double lastTime = 0;
            double lastSpeed = 0;

            Task renderTask = Task.Run(async () =>
            {
                try
                {
                    StringBuilder lineBuffer = new StringBuilder(256);
                    string oldLine = "";

                    while (!internalCts.Token.IsCancellationRequested)
                    {
                        long currentVal = Interlocked.Read(ref taskState._value);
                        double currentTime = stopwatch.Elapsed.TotalSeconds;

                        double currentSpeed = 0;

                        if ((currentTime - lastMetricsUpdate) >= metricsInterval)
                        {
                            if (currentVal != lastValue && currentTime > lastTime)
                            {
                                double instSpeed =
                                    (currentVal - lastValue) / Math.Max(0.001, (currentTime - lastTime));

                                lastSpeed = instSpeed;
                                lastValue = currentVal;
                                lastTime = currentTime;
                            }
                            lastMetricsUpdate = currentTime;
                        }

                        var displaySpeed = lastSpeed;

                        double percentage = maxValue > 0 ? (double)currentVal / maxValue : 0;
                        if (percentage > 1.0) percentage = 1.0;

                        // 1. Columna Descripción
                        string colDesc = description + " ";

                        // 2. Columna Porcentaje
                        string colPercent = $" {(int)(percentage * 100)}% ";

                        // 3. Columna Velocidad (Condicional)
                        string colSpeed = showSpeed ? $" {FormatDecimalSpeed(displaySpeed)} " : string.Empty;

                        // 4. Columna Tiempo Restante (ETA)
                        string colEta = $" [{FormatEta(currentVal, maxValue, displaySpeed)}] ";

                        // Calcular espacio disponible para la barra de progreso de forma dinámica
                        int metaWidth = colDesc.GetVisualLength() + colPercent.GetVisualLength() + colSpeed.GetVisualLength() + colEta.GetVisualLength() + 2;
                        int barWidth = Math.Max(10, Console.WindowWidth - metaWidth);

                        int filledBlocks = (int)Math.Round(percentage * barWidth);
                        int emptyBlocks = barWidth - filledBlocks;

                        string barFilled = new string('█', Math.Max(0, filledBlocks));
                        string barEmpty = new string('░', Math.Max(0, emptyBlocks));
                        string colBar = $"{ThemeColors.Success}[{barFilled}{ThemeColors.Dim}{barEmpty}{ThemeColors.Success}]{ThemeColors.Reset}";

                        string line = $"{colDesc}{colBar}{colPercent}{colSpeed}{colEta}";

                        if (line != oldLine)
                        {
                            if (LivePanel.IsActive)
                                LivePanel.UpdateLine(_panelId, line);
                            else
                            {
                                // Ensamblar buffer completo de la línea
                                lineBuffer.Clear();
                                lineBuffer.Append("\r");
                                lineBuffer.Append(line);
                                lineBuffer.Append("\x1b[K"); // Eliminar fantasmas a la derecha

                                Console.Write(lineBuffer.ToString());
                            }
                            oldLine = line;
                        }

                        await Task.Delay(50, internalCts.Token);

                        if (currentSpeed > 0)  // ← Solo actualiza si hubo movimiento
                        {
                            lastValue = currentVal;
                            lastTime = currentTime;
                            lastSpeed = currentSpeed;
                        }
                    }
                }
                catch (OperationCanceledException) { }
            });

            try
            {
                await workerTask(taskState);
            }
            finally
            {
                internalCts.Cancel();
                await renderTask;

                string finalLine = $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} " + (finalText ?? $"{description} {ThemeColors.Success}[{new string('█', 20)}] 100% {ThemeColors.Dim}(Completado){ThemeColors.Reset}");

                if (LivePanel.IsActive)
                    LivePanel.UpdateLine(_panelId, finalLine);
                else
                    Console.Write($"\r{finalLine}\x1b[K\n");
            }
        }

        private static string FormatDecimalSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "0 B/s";
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
            int digitGroup = (int)(Math.Log10(bytesPerSecond) / 3);
            if (digitGroup >= units.Length) digitGroup = units.Length - 1;
            return $"{bytesPerSecond / Math.Pow(1000, digitGroup):F1} {units[digitGroup]}";
        }

        private static string FormatEta(long current, long max, double bytesPerSecond)
        {
            if (current >= max) return "00:00:00";
            if (bytesPerSecond <= 0) return "--:--:--";

            long remainingBytes = max - current;
            double secondsLeft = remainingBytes / bytesPerSecond;

            if (secondsLeft > 86400 * 99) return "99d+"; // Límite de seguridad

            TimeSpan time = TimeSpan.FromSeconds(secondsLeft);
            return $"{((int)time.TotalHours):D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }
    }
}