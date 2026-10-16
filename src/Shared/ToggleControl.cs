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
        if (input == char.ToLower(toggleKey))
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
        else if (input == char.ToUpper(toggleKey) && param != null)
        {
            if (param.Value == 1)
            {
                enabled = true;
                log = $"{name} enabled.";
                enableAction();
                return true;
            }
            else if (param.Value == 0)
            {
                enabled = false;
                log = $"{name} disabled.";
                disableAction();
                return true;
            }
        }

        log = null;
        return false;
    }

    public Renderable ReportState()
    {
        var stateDesc = enabled ? "[bold green][[enabled]][/]" : "[grey][[disabled]][/]";
        return new Markup($"{KeyBadge.Render(toggleKey)} {Markup.Escape(name)} {stateDesc}");
    }
}