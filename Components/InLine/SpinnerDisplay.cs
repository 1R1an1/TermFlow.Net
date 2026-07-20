/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Components.InLine
{
    /// <summary>
    /// Componente de spinner animado en línea. Mientras se ejecuta una tarea de fondo,
    /// dibuja una secuencia de cuadros Braille hasta que la tarea termina.
    /// </summary>
    public static class SpinnerDisplay
    {
        /// <summary>Frames Braille por defecto del spinner.</summary>
        public static readonly string[] DefaultFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        /// <summary>
        /// Ejecuta un spinner animado mientras corre la tarea indicada. Al terminar,
        /// imprime un mensaje de éxito. Compatible con modo inline y <see cref="LivePanel"/>.
        /// </summary>
        /// <param name="description">Texto a mostrar al lado del spinner.</param>
        /// <param name="workerTask">Tarea asíncrona de fondo a ejecutar.</param>
        /// <param name="finalText">Texto final opcional; si es <c>null</c> se usa la descripción + "(Completado)".</param>
        /// <param name="panelId">ID opcional de línea dinámica del <see cref="LivePanel"/> a reutilizar.</param>
        /// <param name="token">Token de cancelación externa.</param>
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
