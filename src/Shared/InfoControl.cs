using Spectre.Console;
using Spectre.Console.Rendering;

namespace Shared;

/// <summary>
/// A read-only line of status information next to the other controls. It has no key.
/// </summary>
class InfoControl(string name, Func<string?> getState) : IControl
{
    public bool Match(char input, int? param, out string? log)
    {
        log = null;
        return false;
    }

    public Renderable ReportState()
    {
        var state = getState();
        return state is null
            ? Text.Empty
            : new Markup($"{Markup.Escape(name)}: [bold yellow]{Markup.Escape(state)}[/]");
    }
}
