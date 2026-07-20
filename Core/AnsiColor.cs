/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
namespace TermFlow.Core;

/// <summary>
/// Envoltorio tipado para secuencias ANSI de color y estilo.
/// Permite componer estilos mediante el operador <c>+</c> y convertir implícitamente a <see cref="string"/>.
/// </summary>
public sealed class AnsiColor
{
    /// <summary>Código ANSI crudo encapsulado.</summary>
    public string Code { get; }

    /// <summary>
    /// Constructor privado: las instancias se obtienen desde los campos estáticos predefinidos.
    /// </summary>
    /// <param name="code">Secuencia ANSI a encapsular.</param>
    private AnsiColor(string code) => Code = code;

    /// <summary>Devuelve el código ANSI crudo.</summary>
    public override string ToString() => Code;

    /// <summary>
    /// Conversión implícita a <see cref="string"/> para usar el color directamente en interpolaciones.
    /// </summary>
    /// <param name="color">Instancia a convertir.</param>
    public static implicit operator string(AnsiColor color) => color.Code;

    /// <summary>
    /// Combina dos estilos colocando sus códigos uno tras otro.
    /// </summary>
    /// <param name="left">Primer estilo/color.</param>
    /// <param name="right">Segundo estilo/color.</param>
    /// <returns>Nuevo <see cref="AnsiColor"/> con la concatenación de códigos.</returns>
    public static AnsiColor operator +(AnsiColor left, AnsiColor right) => new(left.Code + right.Code);

    // Reset
    /// <summary>Resetea todos los atributos a sus valores por defecto.</summary>
    public static AnsiColor Reset { get; } = new("\x1b[0m");

    // Estilos
    /// <summary>Aplica grosor extra al texto (negrita).</summary>
    public static AnsiColor Bold { get; } = new("\x1b[1m");
    /// <summary>Atenúa el texto (color tenue).</summary>
    public static AnsiColor Dim { get; } = new("\x1b[2m");
    /// <summary>Aplica estilo itálica.</summary>
    public static AnsiColor Italic { get; } = new("\x1b[3m");
    /// <summary>Aplica subrayado simple.</summary>
    public static AnsiColor Underline { get; } = new("\x1b[4m");
    /// <summary>Hace parpadear el texto (no soportado en todas las terminales).</summary>
    public static AnsiColor Blink { get; } = new("\x1b[5m");
    /// <summary>Invierte foreground y background.</summary>
    public static AnsiColor Reverse { get; } = new("\x1b[7m");
    /// <summary>Oculta el texto (útil para contraseñas).</summary>
    public static AnsiColor Hidden { get; } = new("\x1b[8m");
    /// <summary>Aplica tachado.</summary>
    public static AnsiColor Strike { get; } = new("\x1b[9m");

    // Colores normales
    /// <summary>Color de texto negro.</summary>
    public static AnsiColor Black { get; } = new("\x1b[30m");
    /// <summary>Color de texto rojo.</summary>
    public static AnsiColor Red { get; } = new("\x1b[31m");
    /// <summary>Color de texto verde.</summary>
    public static AnsiColor Green { get; } = new("\x1b[32m");
    /// <summary>Color de texto amarillo.</summary>
    public static AnsiColor Yellow { get; } = new("\x1b[33m");
    /// <summary>Color de texto azul.</summary>
    public static AnsiColor Blue { get; } = new("\x1b[34m");
    /// <summary>Color de texto magenta.</summary>
    public static AnsiColor Magenta { get; } = new("\x1b[35m");
    /// <summary>Color de texto cian.</summary>
    public static AnsiColor Cyan { get; } = new("\x1b[36m");
    /// <summary>Color de texto blanco.</summary>
    public static AnsiColor White { get; } = new("\x1b[37m");

    // Colores brillantes
    /// <summary>Color de texto negro brillante (gris claro).</summary>
    public static AnsiColor BrightBlack { get; } = new("\x1b[90m");
    /// <summary>Color de texto rojo brillante.</summary>
    public static AnsiColor BrightRed { get; } = new("\x1b[91m");
    /// <summary>Color de texto verde brillante.</summary>
    public static AnsiColor BrightGreen { get; } = new("\x1b[92m");
    /// <summary>Color de texto amarillo brillante.</summary>
    public static AnsiColor BrightYellow { get; } = new("\x1b[93m");
    /// <summary>Color de texto azul brillante.</summary>
    public static AnsiColor BrightBlue { get; } = new("\x1b[94m");
    /// <summary>Color de texto magenta brillante.</summary>
    public static AnsiColor BrightMagenta { get; } = new("\x1b[95m");
    /// <summary>Color de texto cian brillante.</summary>
    public static AnsiColor BrightCyan { get; } = new("\x1b[96m");
    /// <summary>Color de texto blanco brillante.</summary>
    public static AnsiColor BrightWhite { get; } = new("\x1b[97m");

    // Fondos
    /// <summary>Color de fondo negro.</summary>
    public static AnsiColor BgBlack { get; } = new("\x1b[40m");
    /// <summary>Color de fondo rojo.</summary>
    public static AnsiColor BgRed { get; } = new("\x1b[41m");
    /// <summary>Color de fondo verde.</summary>
    public static AnsiColor BgGreen { get; } = new("\x1b[42m");
    /// <summary>Color de fondo amarillo.</summary>
    public static AnsiColor BgYellow { get; } = new("\x1b[43m");
    /// <summary>Color de fondo azul.</summary>
    public static AnsiColor BgBlue { get; } = new("\x1b[44m");
    /// <summary>Color de fondo magenta.</summary>
    public static AnsiColor BgMagenta { get; } = new("\x1b[45m");
    /// <summary>Color de fondo cian.</summary>
    public static AnsiColor BgCyan { get; } = new("\x1b[46m");
    /// <summary>Color de fondo blanco.</summary>
    public static AnsiColor BgWhite { get; } = new("\x1b[47m");

    // Fondos brillantes
    /// <summary>Color de fondo negro brillante.</summary>
    public static AnsiColor BgBrightBlack { get; } = new("\x1b[100m");
    /// <summary>Color de fondo rojo brillante.</summary>
    public static AnsiColor BgBrightRed { get; } = new("\x1b[101m");
    /// <summary>Color de fondo verde brillante.</summary>
    public static AnsiColor BgBrightGreen { get; } = new("\x1b[102m");
    /// <summary>Color de fondo amarillo brillante.</summary>
    public static AnsiColor BgBrightYellow { get; } = new("\x1b[103m");
    /// <summary>Color de fondo azul brillante.</summary>
    public static AnsiColor BgBrightBlue { get; } = new("\x1b[104m");
    /// <summary>Color de fondo magenta brillante.</summary>
    public static AnsiColor BgBrightMagenta { get; } = new("\x1b[105m");
    /// <summary>Color de fondo cian brillante.</summary>
    public static AnsiColor BgBrightCyan { get; } = new("\x1b[106m");
    /// <summary>Color de fondo blanco brillante.</summary>
    public static AnsiColor BgBrightWhite { get; } = new("\x1b[107m");
}
