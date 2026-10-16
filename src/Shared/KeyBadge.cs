using Spectre.Console;

namespace Shared;

static class KeyBadge
{
    public static string Render(char key) => $"[bold black on yellow] {Markup.Escape(key.ToString())} [/]";
}
