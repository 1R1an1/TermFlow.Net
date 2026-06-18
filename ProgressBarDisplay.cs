using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleUtils
{
    public interface IProgressTask
    {
        long Value { get; set; }
        double Speed { get; set; } // En bytes por segundo
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
            public double Speed { get; set; }
        }

        public static async Task RunAsync(
            string description,
            long maxValue,
            Func<IProgressTask, Task> workerTask,
            int? fixedBarWidth = null,
            CancellationToken token = default)
        {
            using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var taskState = new ProgressTaskImpl();
            var theme = Engine.Theme;

            Console.CursorVisible = false;

            Task renderTask = Task.Run(async () =>
            {
                try
                {
                    StringBuilder lineBuffer = new StringBuilder(256);

                    while (!internalCts.Token.IsCancellationRequested)
                    {
                        long currentVal = Interlocked.Read(ref taskState._value);
                        double currentSpeed = taskState.Speed;

                        double percentage = maxValue > 0 ? (double)currentVal / maxValue : 0;
                        if (percentage > 1.0) percentage = 1.0;

                        // 1. Columna Descripción
                        string colDesc = $"{theme.Primary}{description}{theme.Reset} ";

                        // 2. Columna Porcentaje
                        string colPercent = $" {(int)(percentage * 100)}% ";

                        // 3. Columna Velocidad (Decimal Base: 1 KB = 1000 Bytes)
                        string colSpeed = $" {FormatDecimalSpeed(currentSpeed)} ";

                        // 4. Columna Tiempo Restante (ETA)
                        string colEta = $" [{FormatEta(currentVal, maxValue, currentSpeed)}] ";

                        // Calcular espacio disponible para la barra de progreso
                        int metaWidth = colDesc.Length + colPercent.Length + colSpeed.Length + colEta.Length + 4; // Márgenes de escape aproximados
                        int barWidth = fixedBarWidth ?? Math.Max(10, Console.WindowWidth - metaWidth - 10);

                        int filledBlocks = (int)Math.Round(percentage * barWidth);
                        int emptyBlocks = barWidth - filledBlocks;

                        string barFilled = new string('█', Math.Max(0, filledBlocks));
                        string barEmpty = new string('░', Math.Max(0, emptyBlocks));
                        string colBar = $"{theme.Success}[{barFilled}{theme.Dim}{barEmpty}{theme.Success}]{theme.Reset}";

                        // Ensamblar buffer completo de la línea
                        lineBuffer.Clear();
                        lineBuffer.Append("\r");
                        lineBuffer.Append(colDesc);
                        lineBuffer.Append(colBar);
                        lineBuffer.Append(colPercent);
                        lineBuffer.Append(colSpeed);
                        lineBuffer.Append(colEta);
                        lineBuffer.Append("\x1b[K"); // Eliminar fantasmas a la derecha

                        Console.Write(lineBuffer.ToString());

                        await Task.Delay(50, internalCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Aborto limpio instantáneo
                }
            }, internalCts.Token);

            try
            {
                await workerTask(taskState);
            }
            finally
            {
                internalCts.Cancel();
                await renderTask;

                // Dibujar estado final al 100% clavado e inline
                int width = fixedBarWidth ?? 20;
                Console.Write($"\r{theme.Success}{theme.Checked} {description} [{new string('█', width)}] 100% {theme.Dim}(Completado){theme.Reset}\x1b[K\n");
                Console.CursorVisible = true;
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