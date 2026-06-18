using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleUtils
{
    public static class SpinnerDisplay
    {
        public static readonly string[] DefaultFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        public static async Task RunAsync(string description, Func<CancellationToken, Task> workerTask, string[] customFrames = null, string completionText = "(Completado)", CancellationToken token = default)
        {
            using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            string[] frames = customFrames ?? DefaultFrames;
            var theme = Engine.Theme;

            Console.CursorVisible = false;

            // Hilo de renderizado de la animación
            Task renderTask = Task.Run(async () =>
            {
                int frameIndex = 0;
                try
                {
                    while (!internalCts.Token.IsCancellationRequested)
                    {
                        // \r vuelve al inicio, \x1b[K limpia hacia la derecha
                        Console.Write($"\r{theme.Warning}{frames[frameIndex]}{theme.Reset} {description}\x1b[K");
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

                // Render final de éxito inline
                Console.Write($"\r{theme.Success}{theme.Checked}{theme.Reset} {description} {theme.Dim}{completionText}{theme.Reset}\x1b[K\n");
                Console.CursorVisible = true;
            }
        }
    }
}