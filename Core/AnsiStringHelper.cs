namespace TermFlow.Core
{
    internal static class AnsiStringHelper
    {
        public static int GetVisualLength(this string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, @"\x1b\[[^m]*m", "").Length;
        }
    }
}
