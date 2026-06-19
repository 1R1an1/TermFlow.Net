namespace TermFlow.Core;

public static class ConsoleGlyphs
{
    // Símbolos
    public static string Indicator { get; set; } = "▶";
    public static string Checked { get; set; } = "✔";
    public static string Unchecked { get; set; } = "○";
    public static string InfoBullet { get; set; } = "●";

    // Bordes
    public static char Horizontal { get; set; } = '─';
    public static char Vertical { get; set; } = '│';

    public static char TopLeft { get; set; } = '┌';
    public static char TopRight { get; set; } = '┐';

    public static char BottomLeft { get; set; } = '└';
    public static char BottomRight { get; set; } = '┘';

    public static char DividerLeft { get; set; } = '├';
    public static char DividerRight { get; set; } = '┤';

    // public static char DividerTop { get; set; } = '┬';
    // public static char DividerBottom { get; set; } = '┴';

    // public static char Cross { get; set; } = '┼';
}