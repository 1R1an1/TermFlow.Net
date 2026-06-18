using System;
using System.Text;
using System.Threading;

namespace TermFlow.Core;

public enum InputEventType
{
    None,
    Key,
    ScrollUp,
    ScrollDown
}

public struct ConsoleInputEvent
{
    public InputEventType Type { get; set; }
    public ConsoleKeyInfo KeyInfo { get; set; }
}

public static class InputReader
{
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
