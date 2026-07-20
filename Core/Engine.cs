/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TermFlow.Core;

/// <summary>
/// Motor de inicialización de la consola: configura encoding, ANSI nativo en Windows
/// y gestiona la entrada/salida del modo pantalla completa (alternate buffer + mouse).
/// </summary>
public static class Engine
{
    // Imports nativos de Windows para forzar el soporte ANSI

    /// <summary>
    /// Obtiene el handle de un flujo estándar de la consola (entrada/salida/error).
    /// </summary>
    /// <param name="nStdHandle">Identificador del flujo (-11 = stdout, -12 = stdin, -13 = stderr).</param>
    /// <returns>Puntero al handle solicitado.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    /// <summary>
    /// Consulta el modo actual de procesamiento de la consola.
    /// </summary>
    /// <param name="hConsoleHandle">Handle de la consola devuelto por <see cref="GetStdHandle"/>.</param>
    /// <param name="lpMode">Recibe las flags de modo activas.</param>
    /// <returns><c>true</c> si la operación tuvo éxito.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    /// <summary>
    /// Establece el modo de procesamiento de la consola (habilita VT/ANSI).
    /// </summary>
    /// <param name="hConsoleHandle">Handle de la consola.</param>
    /// <param name="dwMode">Nuevas flags de modo a aplicar.</param>
    /// <returns><c>true</c> si la operación tuvo éxito.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private static bool isFullScreen = false;

    /// <summary>
    /// Inicializa encoding UTF-8, activa secuencias ANSI en Windows y registra hooks
    /// de limpieza (ProcessExit y CancelKeyPress) para restaurar la consola al salir.
    /// Debe llamarse una sola vez al inicio del programa.
    /// </summary>
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


        // 1. Captura SIGTERM (Cierre del sistema, kill ordinario) y salidas normales
        AppDomain.CurrentDomain.ProcessExit += (s, e) => ExitFullScreen();

        // 2. Captura SIGINT (Ctrl + C en la terminal)
        Console.CancelKeyPress += (s, e) =>
        {
            // false significa: "Dejá que el proceso muera normalmente después de que termine este evento"
            e.Cancel = false;
            ExitFullScreen();
        };
    }

    /// <summary>
    /// Entra al modo pantalla completa activando el alternate buffer, ocultando el cursor
    /// y (opcionalmente) habilitando el tracking de mouse SGR.
    /// </summary>
    /// <param name="captureMouse">Si <c>true</c>, activa el reporte de clicks y rueda del mouse.</param>
    public static void EnterFullScreen(bool captureMouse = true)
    {
        // \x1b[?1049h -> Activar alternate screen buffer (Lienzo limpio sin alterar la consola previa)
        // \x1b[2J     -> Limpiar pantalla completa
        // \x1b[?25l    -> Ocultar el cursor físico de la terminal
        // \x1b[?1002h -> Activar reporte de clicks y rueda de mouse
        // \x1b[?1006h -> Activar formato extendido SGR para tracking de mouse preciso
        if (!isFullScreen)
        {
            Console.Write("\x1b[?1049h\x1b[2J\x1b[?25l" + (captureMouse ? "\x1b[?1000h\x1b[?1006h" : ""));
            isFullScreen = true;
        }
    }

    /// <summary>
    /// Activa o desactiva manualmente el alternate buffer sin tocar mouse ni cursor.
    /// Útil para limpiar la pantalla sin perder el estado de pantalla completa.
    /// </summary>
    /// <param name="active"><c>true</c> para activar, <c>false</c> para restaurar el buffer principal.</param>
    public static void AlternateBuffer(bool active) => Console.Write(active ? "\x1b[?1049h\x1b[2J" : "\x1b[?1049l");

    /// <summary>
    /// Sale del modo pantalla completa restaurando mouse, cursor y buffer principal.
    /// Es idempotente: si no estaba activo, no hace nada.
    /// </summary>
    public static void ExitFullScreen()
    {
        // Secuencia de restauración total:
        // \x1b[?1002l\x1b[?1006l -> Apagar tracking de mouse
        // \x1b[?25h             -> Mostrar de nuevo el cursor nativo
        // \x1b[?1049l            -> Volver al main screen buffer restaurando todo como estaba antes
        if (isFullScreen)
        {
            Console.Write("\x1b[?1000l\x1b[?1006l\x1b[?25h\x1b[?1049l");
            isFullScreen = false;
        }
    }
}
