using Spectre.Console;
using Spectre.Console.Rendering;

namespace Shared;

class ToggleControl : IControl
{
    private readonly char toggleKey;
    private readonly string name;
    private readonly Action enableAction;
    private readonly Action disableAction;
    private bool enabled;

    public ToggleControl(char toggleKey, string name, Action enableAction, Action disableAction)
    {
        this.toggleKey = toggleKey;
        this.name = name;
        this.enableAction = enableAction;
        this.disableAction = disableAction;
    }

    public bool Match(char input, int? param, out string? log)
    {
        if (input == toggleKey)
        {
            enabled = !enabled;
            if (enabled)
            {
                log = $"{name} enabled.";
                enableAction();
            }
            else
            {
                log = $"{name} disabled.";
                disableAction();
            }
            return true;
        }

        log = null;
        return false;
    }

    public Renderable ReportState()
    {
        var stateDesc = enabled ? "enabled" : "disabled";
        return new Columns(
            new Markup($"{name} {Emoji.Known.UpDownArrow}[bold]{toggleKey}[/] [[[bold]{stateDesc}[/]]]"),
            new Markup(""))
        {
            Padding = new Padding(1),
            Expand = false
        };
    }
}