/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    /// <summary>
    /// Interfaz que expone el valor mutable del progreso de una tarea para que el worker
    /// lo actualice desde cualquier hilo de forma segura.
    /// </summary>
    public interface IProgressTask
    {
        /// <summary>Valor actual del progreso (de 0 a maxValue indicado en <see cref="ProgressBarDisplay.RunAsync"/>).</summary>
        long Value { get; set; }
    }

    /// <summary>
    /// Componente de barra de progreso en línea. Dibuja barra, porcentaje, velocidad y ETA
    /// mientras se ejecuta la tarea de fondo. Compatible con modo inline y <see cref="LivePanel"/>.
    /// </summary>
    public static class ProgressBarDisplay
    {
        /// <summary>
        /// Implementación interna de <see cref="IProgressTask"/> con acceso atómico al valor
        /// mediante <see cref="Interlocked"/>.
        /// </summary>
        private class ProgressTaskImpl : IProgressTask
        {
            /// <summary>El campo real en memoria que guarda el valor del progreso.</summary>
            public long _value;

            /// <summary>Lee o escribe el valor de progreso de forma atómica y thread-safe.</summary>
            public long Value
            {
                get => Interlocked.Read(ref _value);
                set => Interlocked.Exchange(ref _value, value);
            }
        }

        /// <summary>
        /// Ejecuta una barra de progreso animada mientras corre la tarea indicada.
        /// Calcula dinámicamente porcentaje, velocidad y ETA en intervalos de 250ms.
        /// </summary>
        /// <param name="description">Etiqueta descriptiva de la operación (ej. "Descargando").</param>
        /// <param name="maxValue">Valor máximo que representa el 100%.</param>
        /// <param name="workerTask">Tarea asíncrona que recibe un <see cref="IProgressTask"/> para reportar avance.</param>
        /// <param name="finalText">Texto final opcional al terminar; si es <c>null</c> se arma uno por defecto.</param>
        /// <param name="panelId">ID opcional de línea dinámica del <see cref="LivePanel"/> a reutilizar.</param>
        /// <param name="showSpeed">Si <c>true</c> muestra la columna de velocidad (B/s, KB/s, etc.).</param>
        /// <param name="token">Token de cancelación externa.</param>
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

        /// <summary>
        /// Formatea una velocidad en bytes/segundo a la unidad decimal más legible (B/s, KB/s, MB/s, ...).
        /// </summary>
        /// <param name="bytesPerSecond">Velocidad cruda en bytes por segundo.</param>
        /// <returns>Cadena formateada con la unidad apropiada.</returns>
        private static string FormatDecimalSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "0 B/s";
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
            int digitGroup = (int)(Math.Log10(bytesPerSecond) / 3);
            if (digitGroup >= units.Length) digitGroup = units.Length - 1;
            return $"{bytesPerSecond / Math.Pow(1000, digitGroup):F1} {units[digitGroup]}";
        }

        /// <summary>
        /// Calcula el tiempo restante estimado (ETA) en formato HH:MM:SS.
        /// </summary>
        /// <param name="current">Valor actual del progreso.</param>
        /// <param name="max">Valor máximo (100%).</param>
        /// <param name="bytesPerSecond">Velocidad actual estimada.</param>
        /// <returns>Cadena HH:MM:SS, "--:--:--" si no se puede calcular, o "99d+" si excede el límite.</returns>
        private static string FormatEta(long current, long max, double bytesPerSecond)
        {
            if (current >= max) return "00:00:00";
            if (bytesPerSecond <= 0) return "--:--:--";

            long remainingBytes = max - current;
            double secondsLeft = remainingBytes / bytesPerSecond;

            if (secondsLeft > 86400 * 99) return "99d+"; // Límite de seguridad

            TimeSpan time = TimeSpan.FromSeconds(secondsLeft);
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }
    }
}
