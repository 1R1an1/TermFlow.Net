using System.Collections.Generic;
using System.Threading.Tasks;
using Figgle.Fonts;
using TermFlow.Components.FullScreen;
using TermFlow.Components.InLine;
using TermFlow.Core;

namespace TermFlow;

internal class Program
{
    static async Task Main(string[] args)
    {
        Engine.Setup();
        while (true)
        {

            // Activar el panel
            LivePanel.Start();
            var appLifetimeToken = new TaskCompletionSource();

            // Logs estáticos
            TextViewer.Info("Iniciando proceso...");
            TextViewer.Success("Conexión establecida.");


            // Barra de progreso (se muestra en el panel)
            _ = ProgressBarDisplay.RunAsync("Descargando", 100, async (p) =>
            {
                for (int i = 0; i <= 100; i += 5)
                {
                    p.Value = i;
                    await Task.Delay(2000);
                    TextViewer.Info("Descarga terminada: " + i);
                }

                appLifetimeToken.SetResult();
            });

            _ = SpinnerDisplay.RunAsync("Terminando2. . .", async (_) => { await Task.Delay(12345); });
            //await SpinnerDisplay.RunAsync("Terminando. . .", async (_) => { await Task.Delay(123123123); });

            string[] headers = { $"{ThemeColors.Reset + AnsiColor.Cyan + AnsiColor.Bold}ID", "Nombre Servidor", "IP Puerto", "Estado" };
            var rows = new List<string[]>
            {
                new[] { $"{AnsiColor.Cyan}001{ThemeColors.Reset}", "ControlHub.PCServer", "127.0.0.1:8080", "ONLINE" },
                new[] { $"{AnsiColor.Cyan}002{ThemeColors.Reset}", "ControlHub.PCCommon", "127.0.0.1:8081", "STANDBY" },
                new[] { $"{AnsiColor.Cyan}003{ThemeColors.Reset}", "Backup_Node", "192.168.1.50:9000", "OFFLINE" }
            };

            TableView.Show(headers, rows, ThemeColors.Primary + AnsiColor.Bold);
            await appLifetimeToken.Task;
            TextInput.PressToContinue();
            TextViewer.Info("Descarga terminada");
            // LivePanel.Stop();
        }
        //Console.ReadKey(true);
        // await ProgressBarDisplay.RunAsync("Descargando", 2000, async (p) =>
        //         {
        //             for (int i = 0; i <= 2000; i++)
        //             {
        //                 p.Value = i;
        //                 await Task.Delay(50);
        //             }
        //         });

        // Detener el panel (volver a la consola normal)
        //LivePanel.Stop();
        // var panel = new HistoryPanel("Mi Panel de Descargas");

        // var miBarra1 = new ProgressBarDisplay("Archivo ISO", 100);
        // var miBarra2 = new ProgressBarDisplay("Archivo ZIP", 100);

        // panel.AddElement(miBarra1);
        // panel.AddLog("Iniciando paralelo...");
        // panel.AddElement(miBarra2);

        // // Lanzar tareas en background
        // _ = Task.Run(async () => { /* actualizar miBarra1.Value */ });
        // _ = Task.Run(async () => { /* actualizar miBarra2.Value */ });

        // // Arrancar el motor del panel en el hilo principal
        // await PanelHost.RunAsync(panel);

        // await new ProgressBarDisplay("adawda", 100).RunInlineAsync(async (_) => { await Task.Delay(10000); });
    }
}
