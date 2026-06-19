using System;
using System.Collections.Generic;
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
        // 1. Captura SIGTERM (Cierre del sistema, kill ordinario) y salidas normales
        AppDomain.CurrentDomain.ProcessExit += (s, e) => ExecuteSafeShutdown();

        // 2. Captura SIGINT (Ctrl + C en la terminal)
        Console.CancelKeyPress += (s, e) =>
        {
            // false significa: "Dejá que el proceso muera normalmente después de que termine este evento"
            e.Cancel = false;
            ExecuteSafeShutdown();
        };

        var lista = new string[] { "hola", "pedro", "pepe", "hola1", "pedro1", "pepe1", "hola2", "pedro2", "pepe2", "hola3", "pedro3", "pepe3", "hola4", "pedro4", "pepe4", "hola5", "pedro5", "pepe5", };
        var e = await TreeExplorer.ExploreMultiAsync("Explorar", "/home/xz1r1an1", ExplorerFilter.OnlyFiles);
        // foreach (var item in e)
        // {
        //     System.Console.WriteLine(item);
        // }
        // Console.ReadKey();
        // 1. Probamos el Input Box para pedir un dato común
        // Texto plano normal impreso a la vieja usanza
        var cts = new CancellationTokenSource();
        var a = await SearchList.FilterOneAsync("ASDASDASD", lista);
        await Menu.SelectOneAsync("dfgdfggdfghjdfg", lista);

        // 1. Probar el confirm inline
        if (await TextInput.AskAsync("¿Desea desplegar la lista de servidores activos?"))
        {
            // 2. Probar la Tabla Fullscreen
            string[] headers = { "ID", "Nombre Servidor", "IP Puerto", "Estado" };
            var rows = new List<string[]>
    {
        new[] { "001", "ControlHub.PCServer", "127.0.0.1:8080", "ONLINE" },
        new[] { "002", "ControlHub.PCCommon", "127.0.0.1:8081", "STANDBY" },
        new[] { "003", "Backup_Node", "192.168.1.50:9000", "OFFLINE" }
    };

            TableView.Show(headers, rows, ThemeColors.Primary + AnsiColor.Bold);
        }

        TextViewer.Info(await SpinnerDisplay.RunAsync(
                    "Verificando credenciales SSH en el servidor remoto",
                    async (a) => await Task.Delay(2000)
                ));
        LiveConsole liveChat = new();
        // Podés inyectar logs desde tareas en segundo plano libremente
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(3000); // Simulamos mensajes que caen del servidor
                liveChat.WriteLog($"{AnsiColor.Cyan}[SERVER]{ThemeColors.Reset} Ping recibido a las {DateTime.Now:HH:mm:ss}");
            }
        });

        // Iniciás la interfaz bloqueando el hilo principal
        await liveChat.RunAsync(
            prompt: $"{ThemeColors.Dim}>>>{ThemeColors.Reset} ",
            onInputSubmitted: async (mensaje) =>
            {
                // 1. Lo agregas a tu propia pantalla
                liveChat.WriteLog($"{ThemeColors.Primary}Tú{ThemeColors.Reset}: {mensaje}");

                // 2. Ejecutás tu sobrecarga de envío (ControlHub.SendBinary / SendCommand)
                // await ControlHubNetwork.SendAsync(mensaje);
            }
        );

        Console.WriteLine("Saliste del chat de manera segura.");
        // _ = Task.Run(async () =>
        // {
        //     await Task.Delay(5000);
        //     cts.Cancel();
        //     // TextViewer.Success("Tareas canceladas");
        // });
        // await Menu.SelectOneAsync("awdaad", lista, cts.Token);

        // TextViewer.WriteHeader("Iniciando herramientas de automatización de dotfiles... - ");

        // // 1. El input ahora es sutil, de una sola línea y sin títulos raros
        // string user = await TextInput.ReadStringAsync(">>> ", cts.Token);
        // if (user == null) { TextViewer.Error("Operación cancelada."); return; }

        // // 2. El spinner corre discreto en su propia línea
        // await SpinnerDisplay.RunAsync(
        //     "Verificando credenciales SSH en el servidor remoto",
        //     async (a) => await Task.Delay(2000)
        // );

        // // Imprimimos algo en el medio sin que nada se rompa
        // TextViewer.Info($"[INFO] Conexión aprobada para el usuario: {user}");

        // // 3. La barra de progreso avanza en su lugar y al terminar se queda fija al 100%
        // await ProgressBarDisplay.RunAsync(
        //     $"Sincronizando archivos con tu repositorio local", 223575856,
        //     async (task) =>
        //     {
        //         for (int i = 1; i <= 223575856; i += new Random().Next((1024 * 1024 * 10) / 10, (20 * 1024 * 1024) / 10))
        //         {
        //             await Task.Delay(100);
        //             task.Value = i;
        //             // progress.Report(i / 100.0);
        //         }
        //     }, showSpeed: true
        // );

        // // El flujo de texto plano sigue perfectamente limpio hacia abajo
        // TextViewer.Success("¡Proceso finalizado! Todo el sistema quedó al día.");
        // var e = await Menu.SelectMultiAsync("Prueba", lista);
        // foreach (var item in e)
        //     System.Console.WriteLine(lista[item]);

        // Console.ReadKey();
        // var a = await SearchList.FilterOneAsync("ASDASDASD", lista);
        // System.Console.WriteLine(lista[a]);
        // Console.ReadKey();
        // e = await SearchList.FilterMultiAsync("ASDASDASD", lista);
        // foreach (var item in e)
        //     System.Console.WriteLine(lista[item]);
        // Console.ReadKey();
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
