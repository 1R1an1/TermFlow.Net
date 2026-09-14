using System;
using TermFlow.Core;

namespace TermFlow.Base;

public delegate (int Left, int Top, int Right, int Bottom) MarginCalc(int Width, int Height);

public readonly record struct Styles
{
    public readonly bool DrawBorder { get; init; } = false;
    public readonly AnsiColor BorderColor { get; init; } = null;
    public readonly char BackgroundChar { get; init; } = ' ';
    public readonly AnsiColor BackgroundColor { get; init; } = null;

    public readonly MarginCalc Margin { get; init; } = null;

    private int _marginRows => Margin?.Invoke(Console.WindowWidth, Console.WindowHeight) is var m && m.HasValue ? m.Value.Top + m.Value.Bottom : 0;

    internal readonly int AdditionalRows => (DrawBorder ? 2 : 0) + _marginRows;

    public Styles() { }
}
