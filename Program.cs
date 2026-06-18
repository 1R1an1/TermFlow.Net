using System.Threading.Tasks;

namespace ConsoleUtils;

class Program
{
    static async Task Main(string[] args)
    {
        Engine.Setup();
        try
        {
            await Menu.SelectMultiAsync("Prueba", new string[] { "hola", "pedro", "pepe", "hola", "pedro", "pepe", "hola", "pedro", "pepe", "hola", "pedro", "pepe", "hola", "pedro", "pepe", "hola", "pedro", "pepe", });
        }
        finally
        {
            Engine.Shutdown();
        }
    }
}
