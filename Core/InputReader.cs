/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Text;
using System.Threading;

namespace TermFlow.Core;

/// <summary>
/// Clasifica el tipo de evento de entrada capturado por <see cref="InputReader"/>.
/// </summary>
internal enum InputEventType
{
    /// <summary>No hay evento disponible.</summary>
    None,
    /// <summary>Tecla común del teclado (letras, flechas, etc.).</summary>
    Key,
    /// <summary>Rueda del mouse hacia arriba.</summary>
    ScrollUp,
    /// <summary>Rueda del mouse hacia abajo.</summary>
    ScrollDown
}

/// <summary>
/// Estructura inmutable que representa un evento de entrada individual de la consola.
/// </summary>
internal struct ConsoleInputEvent
{
    /// <summary>Tipo de evento detectado.</summary>
    public InputEventType Type { get; set; }
    /// <summary>Información de la tecla cuando <see cref="Type"/> es <see cref="InputEventType.Key"/>.</summary>
    public ConsoleKeyInfo KeyInfo { get; set; }
}

/// <summary>
/// Lector de bajo nivel para la consola. Detecta teclas comunes y decodifica
/// las secuencias ANSI SGR del mouse (scroll up/down) descartando clicks.
/// </summary>
internal static class InputReader
{
    /// <summary>
    /// Lee un evento de entrada sin bloquear. Si no hay teclas disponibles devuelve un evento
    /// de tipo <see cref="InputEventType.None"/>. Decodifica secuencias SGR del mouse y filtra
    /// clicks fantasma para no propagar un Escape residual.
    /// </summary>
    /// <returns>Evento de consola decodificado.</returns>
    public static ConsoleInputEvent ReadInput()
    {
        if (!Console.KeyAvailable)
        {
            return new ConsoleInputEvent { Type = InputEventType.None };
        }

        // Capturamos el caracter inicial sin imprimirlo
        ConsoleKeyInfo firstKey = Console.ReadKey(intercept: true);

        // Si el caracter es ESC (27 / '\x1b'), verificamos si es una ráfaga ANSI del mouse
        if (firstKey.KeyChar == '\x1b')
        {
            // Damos un margen mínimo de 2ms para que terminen de entrar los bytes de la secuencia
            Thread.Sleep(2);
            if (Console.KeyAvailable)
            {
                StringBuilder sb = new StringBuilder();
                while (Console.KeyAvailable)
                {
                    sb.Append(Console.ReadKey(intercept: true).KeyChar);
                }

                string sequence = sb.ToString();

                // Formato SGR para la rueda del mouse:
                // "[<64;..." -> Scroll Arriba
                // "[<65;..." -> Scroll Abajo
                if (sequence.StartsWith("[<64;"))
                {
                    return new ConsoleInputEvent { Type = InputEventType.ScrollUp };
                }
                if (sequence.StartsWith("[<65;"))
                {
                    return new ConsoleInputEvent { Type = InputEventType.ScrollDown };
                }

                // FIX: Si es cualquier otra interacción de mouse (click, soltar click, arrastrar), 
                // la descartamos explícitamente para que no filtre un "Escape" fantasma.
                if (sequence.StartsWith("[<"))
                {
                    return new ConsoleInputEvent { Type = InputEventType.None };
                }
            }
        }

        // Si no fue una secuencia compleja, devolvemos la tecla común (flechas, letras, etc.)
        return new ConsoleInputEvent
        {
            Type = InputEventType.Key,
            KeyInfo = firstKey
        };
    }
}
