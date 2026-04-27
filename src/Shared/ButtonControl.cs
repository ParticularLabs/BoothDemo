using Spectre.Console;
using Spectre.Console.Rendering;

namespace Shared;

class ButtonControl : IControl
{
    private readonly char buttonKey;
    private readonly string name;
    private readonly Action pressedAction;

    public ButtonControl(char buttonKey, string name, Action pressedAction)
    {
        this.buttonKey = buttonKey;
        this.name = name;
        this.pressedAction = pressedAction;
    }

    public bool Match(char input, int? param, out string? log)
    {
        if (input == buttonKey)
        {
            pressedAction();
            log = $"{name} triggered.";
            return true;
        }

        log = null;
        return false;
    }

    public Renderable ReportState()
    {
        return new Columns(
            new Markup($"{name} {Emoji.Known.RightArrow} [bold]{buttonKey}[/]"),
            new Markup(""))
        {
            Padding = new Padding(1),
            Expand = false
        };
    }
}