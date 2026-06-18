namespace ConsoleUtils;

public class ConsoleTheme
{
    // Colores ANSI Estándar (Modificables)
    public string Primary { get; set; } = "\x1b[1;35m";    // Morado/Magenta Brillante
    public string Success { get; set; } = "\x1b[0;32m";    // Verde
    public string Warning { get; set; } = "\x1b[1;33m";    // Amarillo
    public string Error { get; set; } = "\x1b[0;31m";      // Rojo
    public string Cyan { get; set; } = "\x1b[0;36m";       // Cian
    public string Dim { get; set; } = "\x1b[2m";           // Opaco / Gris oscuro
    public string Bold { get; set; } = "\x1b[1m";          // Negrita
    public string Reset { get; set; } = "\x1b[0m";         // Resetear formato

    // Símbolos de Interfaz (Unicode)
    public string Indicator { get; set; } = "▶";
    public string Checked { get; set; } = "✔";
    public string Unchecked { get; set; } = "○";
    public string InfoBullet { get; set; } = "●";

    // Caracteres para bordes de cuadros o resúmenes
    public char BorderHorizontal { get; set; } = '─';
    public char BorderVertical { get; set; } = '│';
    public char CornerTopLeft { get; set; } = '┌';
    public char CornerTopRight { get; set; } = '┐';
    public char CornerBottomLeft { get; set; } = '└';
    public char CornerBottomRight { get; set; } = '┘';
    public char DividerLeft { get; set; } = '├';
    public char DividerRight { get; set; } = '┤';
}
