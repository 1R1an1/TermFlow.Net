using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleUtils;

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
            foreach (var item in e)
            {
                System.Console.WriteLine(item);
            }
            Console.ReadKey();
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
