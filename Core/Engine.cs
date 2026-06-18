using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TermFlow.Core;

public static class Engine
{
    // Propiedad global para acceder al tema activo desde cualquier componente
    public static ConsoleTheme Theme { get; set; } = new ConsoleTheme();

    // Imports nativos de Windows para forzar el soporte ANSI
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private static bool isFullScreen = false;

    public static void Setup()
    {
        // 1. Forzar encoding UTF-8 en ambos flujos para soporte total de caracteres especiales
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        // 2. Si estamos en Windows, activamos el procesamiento VT nativo
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (handle != IntPtr.Zero)
            {
                if (GetConsoleMode(handle, out uint mode))
                {
                    mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                    SetConsoleMode(handle, mode);
                }
            }
        }
    }

    public static void EnterFullScreen()
    {
        // \x1b[?1049h -> Activar alternate screen buffer (Lienzo limpio sin alterar la consola previa)
        // \x1b[2J     -> Limpiar pantalla completa
        // \x1b[?25l    -> Ocultar el cursor físico de la terminal
        // \x1b[?1002h -> Activar reporte de clicks y rueda de mouse
        // \x1b[?1006h -> Activar formato extendido SGR para tracking de mouse preciso
        if (!isFullScreen)
        {
            Console.Write("\x1b[?1049h\x1b[2J\x1b[?25l\x1b[?1002h\x1b[?1006h");
            isFullScreen = true;
        }
    }

    public static void ExitFullScreen()
    {
        // Secuencia de restauración total:
        // \x1b[?1002l\x1b[?1006l -> Apagar tracking de mouse
        // \x1b[?25h             -> Mostrar de nuevo el cursor nativo
        // \x1b[?1049l            -> Volver al main screen buffer restaurando todo como estaba antes
        if (isFullScreen)
        {
            Console.Write("\x1b[?1002l\x1b[?1006l\x1b[?25h\x1b[?1049l");
            isFullScreen = false;
        }
    }
}