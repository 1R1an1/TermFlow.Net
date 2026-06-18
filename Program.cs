using System;
using System.Threading.Tasks;

namespace ConsoleUtils;

class Program
{
    static async Task Main(string[] args)
    {
        Engine.Setup();
        try
        {
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
        finally
        {
            Engine.Shutdown();
        }
    }
}
