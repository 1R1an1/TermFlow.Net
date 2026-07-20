/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
namespace TermFlow.Core;

/// <summary>
/// Catálogo central de glifos Unicode usados para símbolos y bordes de caja.
/// Todos los valores son reemplazables en runtime para customizar la estética.
/// </summary>
public static class ConsoleGlyphs
{
    // Símbolos
    /// <summary>Símbolo indicador de selección (cursor activo).</summary>
    public static string Indicator { get; set; } = "▶";
    /// <summary>Símbolo de check (item marcado / operación exitosa).</summary>
    public static string Checked { get; set; } = "✔";
    /// <summary>Símbolo de error.</summary>
    public static string Error { get; set; } = "✖";
    /// <summary>Símbolo de advertencia.</summary>
    public static string Warning { get; set; } = "⚠";
    /// <summary>Símbolo de checkbox desmarcado.</summary>
    public static string Unchecked { get; set; } = "○";
    /// <summary>Viñeta circular para mensajes informativos.</summary>
    public static string InfoBullet { get; set; } = "●";

    // Bordes
    /// <summary>Carácter horizontal de borde de caja.</summary>
    public static char Horizontal { get; set; } = '─';
    /// <summary>Carácter vertical de borde de caja.</summary>
    public static char Vertical { get; set; } = '│';

    /// <summary>Esquina superior izquierda.</summary>
    public static char TopLeft { get; set; } = '┌';
    /// <summary>Esquina superior derecha.</summary>
    public static char TopRight { get; set; } = '┐';

    /// <summary>Esquina inferior izquierda.</summary>
    public static char BottomLeft { get; set; } = '└';
    /// <summary>Esquina inferior derecha.</summary>
    public static char BottomRight { get; set; } = '┘';

    /// <summary>Conector izquierdo de divisor horizontal (T invertida).</summary>
    public static char DividerLeft { get; set; } = '├';
    /// <summary>Conector derecho de divisor horizontal.</summary>
    public static char DividerRight { get; set; } = '┤';

    // public static char DividerTop { get; set; } = '┬';
    // public static char DividerBottom { get; set; } = '┴';

    // public static char Cross { get; set; } = '┼';
}
