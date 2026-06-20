namespace TermFlow.Core;

public static class ThemeColors
{
    public static AnsiColor Selector { get; set; } = AnsiColor.BrightMagenta;

    public static AnsiColor Primary { get; set; } = AnsiColor.Magenta + AnsiColor.Bold;
    public static AnsiColor Bright { get; set; } = AnsiColor.BrightWhite + AnsiColor.Bold;

    public static AnsiColor Success { get; set; } = AnsiColor.Green;

    public static AnsiColor Warning { get; set; } = AnsiColor.BrightYellow;

    public static AnsiColor Error { get; set; } = AnsiColor.Red;

    public static AnsiColor Info { get; set; } = AnsiColor.Cyan;

    public static AnsiColor Dim { get; set; } = AnsiColor.Dim;

    public static AnsiColor Reset { get; set; } = AnsiColor.Reset;
}