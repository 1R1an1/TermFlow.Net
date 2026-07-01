using System;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    public static class SpinnerDisplay
    {
        public static readonly string[] DefaultFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        public static async Task RunAsync(string description, Func<CancellationToken, Task> workerTask, string finalText = null, long? panelId = null, CancellationToken token = default)
        {
            using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            string[] frames = DefaultFrames;

            long _panelId = LivePanel.IsActive ? panelId ?? LivePanel.AddDynamic($"{description} spinning...") : -1;

            // Hilo de renderizado de la animación
            Task renderTask = Task.Run(async () =>
            {
                int frameIndex = 0;
                try
                {
                    string oldLine = "";
                    while (!internalCts.Token.IsCancellationRequested)
                    {
                        // \r vuelve al inicio, \x1b[K limpia hacia la derecha
                        string line = $"{ThemeColors.Warning}{frames[frameIndex]}{ThemeColors.Reset} {description}";
                        if (line != oldLine)
                        {
                            if (LivePanel.IsActive)
                                LivePanel.UpdateLine(_panelId, line);
                            else
                                Console.Write($"\r{line}\x1b[K");
                            oldLine = line;
                        }

                        frameIndex = (frameIndex + 1) % frames.Length;

                        await Task.Delay(80, internalCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Captura fina al microsegundo para salir sin explotar
                }
            }, internalCts.Token);

            try
            {
                // Ejecutamos la lógica de red o procesamiento del usuario
                await workerTask(internalCts.Token);
            }
            finally
            {
                // Frenamos el renderizado inmediatamente
                internalCts.Cancel();
                await renderTask;

                string line = $"{ThemeColors.Success}{ConsoleGlyphs.Checked}{ThemeColors.Reset} " + (finalText ?? $"{ThemeColors.Success}{description} {ThemeColors.Dim}(Completado){ThemeColors.Reset}");
                if (LivePanel.IsActive)
                    LivePanel.UpdateLine(_panelId, line);
                else
                    Console.Write($"\r{line}\x1b[K\n");
            }
        }
    }
}