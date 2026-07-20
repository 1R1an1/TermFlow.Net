/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
namespace TermFlow.Core;

/// <summary>
/// Paleta central de colores y estilos semánticos del tema TermFlow.
/// Modificable en runtime para customizar la apariencia de todos los componentes.
/// </summary>
public static class ThemeColors
{
    /// <summary>Color del selector (indicador de cursor y item activo).</summary>
    public static AnsiColor Selector { get; set; } = AnsiColor.BrightMagenta;

    /// <summary>Color primario del tema (encabezados, figlet, acentos).</summary>
    public static AnsiColor Primary { get; set; } = AnsiColor.Magenta + AnsiColor.Bold;

    /// <summary>Texto brillante para énfasis máximo.</summary>
    public static AnsiColor Bright { get; set; } = AnsiColor.BrightWhite + AnsiColor.Bold;

    /// <summary>Color para mensajes de éxito.</summary>
    public static AnsiColor Success { get; set; } = AnsiColor.Green;

    /// <summary>Color para advertencias.</summary>
    public static AnsiColor Warning { get; set; } = AnsiColor.BrightYellow;

    /// <summary>Color para errores.</summary>
    public static AnsiColor Error { get; set; } = AnsiColor.Red;

    /// <summary>Color para mensajes informativos.</summary>
    public static AnsiColor Info { get; set; } = AnsiColor.Cyan;

    /// <summary>Texto atenuado (detalles secundarios).</summary>
    public static AnsiColor Dim { get; set; } = AnsiColor.Dim;

    /// <summary>Reset universal para limpiar cualquier estilo aplicado.</summary>
    public static AnsiColor Reset { get; set; } = AnsiColor.Reset;
}
