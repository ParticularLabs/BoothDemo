using Spectre.Console;
using Spectre.Console.Rendering;

namespace Shared;

class DialControl : IControl
{
    int value;
    readonly char upKey;
    readonly char downKey;
    readonly string name;
    readonly Func<string> getState;
    readonly Action<int> setAction;

    public DialControl(char upKey, char downKey, string name, Func<string> getState, Action<int> setAction)
    {
        this.upKey = upKey;
        this.downKey = downKey;
        this.name = name;
        this.getState = getState;
        this.setAction = setAction;
    }

    public bool Match(char input, int? param, out string? log)
    {
        if (input == upKey)
        {
            //Increase
            if (value < 9)
            {
                value++;
            }

            setAction(value);
            log = $"{name} increased to {value}";
            return true;
        }

        if (input == downKey)
        {
            //Decrease
            if (value > 0)
            {
                value--;
            }

            setAction(value);
            log = $"{name} decreased to {value}";
            return true;
        }

        if (input == char.ToUpperInvariant(upKey) && param != null)
        {
            if (param.Value > value)
            {
                log = $"{name} increased to {param.Value}";
            }
            else if (param.Value < value)
            {
                log = $"{name} decreased to {param.Value}";
            }
            else
            {
                log = null;
            }

            value = param.Value;
            setAction(param.Value);
            return true;
        }

        log = null;
        return false;
    }

    public Renderable ReportState()
    {
        return new Columns(
            new Markup($"{name} \ud83d\udd3c[bold]{upKey}[/] \ud83d\udd3d[bold]{downKey}[/] {getState()}"),
            new SettingIndicator(value))
        {
            Padding = new Padding(1),
            Expand = false
        };
    }
}