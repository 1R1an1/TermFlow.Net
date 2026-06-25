using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Components.InLine;
using TermFlow.Core;

namespace TermFlow;

class Program
{
    // Bandera atómica para garantizar que el Shutdown se ejecute EXACTAMENTE UNA VEZ
    private static int _shutdownExecuted = 0;

    static async Task Main(string[] args)
    {
        Engine.Setup();
        // Activar el panel
        LivePanel.Start();

        // Logs estáticos
        TextViewer.Info("Iniciando proceso...");
        TextViewer.Success("Conexión establecida.");

        // Barra de progreso (se muestra en el panel)
        _ = ProgressBarDisplay.RunAsync("Descargando", 100, async (p) =>
        {
            for (int i = 0; i <= 100; i++)
            {
                p.Value = i;
                await Task.Delay(200);
                TextViewer.Info("Descarga terminada: " + i);
            }
        });

        _ = SpinnerDisplay.RunAsync("Terminando2. . .", async (_) => { await Task.Delay(12345); });
        await SpinnerDisplay.RunAsync("Terminando. . .", async (_) => { await Task.Delay(123123123); });

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

    private static void ExecuteSafeShutdown()
    {
        // Interlocked.Exchange cambia el valor a 1 y devuelve el valor VIEJO.
        // Si el valor viejo era 0, significa que nadie ejecutó el Shutdown todavía.
        if (Interlocked.Exchange(ref _shutdownExecuted, 1) == 0)
        {
            Engine.ExitFullScreen();
        }
    }
}
