using Spectre.Console;
using Spectre.Console.Rendering;

namespace Shared;

public sealed class SettingIndicator : Renderable
{
    public int Value { get; }
    public int Max { get; }
    public Color FilledColor { get; }
    public Color EmptyColor { get; }

    public SettingIndicator(
        int value,
        int max = 9,
        Color? filledColor = null,
        Color? emptyColor = null)
    {
        Max = Math.Max(1, max);
        Value = Math.Clamp(value, 0, Max);
        FilledColor = filledColor ?? Color.Green;
        EmptyColor = emptyColor ?? Color.Grey;
    }

    protected override Measurement Measure(RenderOptions options, int maxWidth)
    {
        var text = $"[{new string('#', Max)}]";
        return new Measurement(text.Length, text.Length);
    }

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        yield return new Segment($"[");

        for (int i = 0; i < Max; i++)
        {
            var isFilled = i < Value;
            yield return new Segment(
                isFilled ? "■" : "·",
                new Style(foreground: isFilled ? FilledColor : EmptyColor));
        }

        yield return new Segment($"]");
    }
}