namespace TermFlow.Core;

public sealed class AnsiColor
{
    public string Code { get; }

    private AnsiColor(string code) => Code = code;

    public override string ToString() => Code;

    // Conversión implícita a string
    public static implicit operator string(AnsiColor color) => color.Code;

    // Combinar estilos y colores
    public static AnsiColor operator +(AnsiColor left, AnsiColor right) => new(left.Code + right.Code);

    // Reset
    public static AnsiColor Reset { get; } = new("\x1b[0m");

    // Estilos
    public static AnsiColor Bold { get; } = new("\x1b[1m");
    public static AnsiColor Dim { get; } = new("\x1b[2m");
    public static AnsiColor Italic { get; } = new("\x1b[3m");
    public static AnsiColor Underline { get; } = new("\x1b[4m");
    public static AnsiColor Blink { get; } = new("\x1b[5m");
    public static AnsiColor Reverse { get; } = new("\x1b[7m");
    public static AnsiColor Hidden { get; } = new("\x1b[8m");
    public static AnsiColor Strike { get; } = new("\x1b[9m");

    // Colores normales
    public static AnsiColor Black { get; } = new("\x1b[30m");
    public static AnsiColor Red { get; } = new("\x1b[31m");
    public static AnsiColor Green { get; } = new("\x1b[32m");
    public static AnsiColor Yellow { get; } = new("\x1b[33m");
    public static AnsiColor Blue { get; } = new("\x1b[34m");
    public static AnsiColor Magenta { get; } = new("\x1b[35m");
    public static AnsiColor Cyan { get; } = new("\x1b[36m");
    public static AnsiColor White { get; } = new("\x1b[37m");

    // Colores brillantes
    public static AnsiColor BrightBlack { get; } = new("\x1b[90m");
    public static AnsiColor BrightRed { get; } = new("\x1b[91m");
    public static AnsiColor BrightGreen { get; } = new("\x1b[92m");
    public static AnsiColor BrightYellow { get; } = new("\x1b[93m");
    public static AnsiColor BrightBlue { get; } = new("\x1b[94m");
    public static AnsiColor BrightMagenta { get; } = new("\x1b[95m");
    public static AnsiColor BrightCyan { get; } = new("\x1b[96m");
    public static AnsiColor BrightWhite { get; } = new("\x1b[97m");

    // Fondos
    public static AnsiColor BgBlack { get; } = new("\x1b[40m");
    public static AnsiColor BgRed { get; } = new("\x1b[41m");
    public static AnsiColor BgGreen { get; } = new("\x1b[42m");
    public static AnsiColor BgYellow { get; } = new("\x1b[43m");
    public static AnsiColor BgBlue { get; } = new("\x1b[44m");
    public static AnsiColor BgMagenta { get; } = new("\x1b[45m");
    public static AnsiColor BgCyan { get; } = new("\x1b[46m");
    public static AnsiColor BgWhite { get; } = new("\x1b[47m");

    // Fondos brillantes
    public static AnsiColor BgBrightBlack { get; } = new("\x1b[100m");
    public static AnsiColor BgBrightRed { get; } = new("\x1b[101m");
    public static AnsiColor BgBrightGreen { get; } = new("\x1b[102m");
    public static AnsiColor BgBrightYellow { get; } = new("\x1b[103m");
    public static AnsiColor BgBrightBlue { get; } = new("\x1b[104m");
    public static AnsiColor BgBrightMagenta { get; } = new("\x1b[105m");
    public static AnsiColor BgBrightCyan { get; } = new("\x1b[106m");
    public static AnsiColor BgBrightWhite { get; } = new("\x1b[107m");
}