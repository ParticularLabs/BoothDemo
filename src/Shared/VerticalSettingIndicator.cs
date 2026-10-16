using Spectre.Console;
using Spectre.Console.Rendering;

namespace Shared;

/// <summary>
/// Renders a setting as a tall vertical bar (one row per level), like a mixer fader.
/// </summary>
public sealed class VerticalSettingIndicator : Renderable
{
    const int labelWidth = 3;

    public int Value { get; }
    public int Max { get; }
    public int BarWidth { get; }

    public VerticalSettingIndicator(int value, int max = 9, int barWidth = 10)
    {
        Max = Math.Max(1, max);
        Value = Math.Clamp(value, 0, Max);
        BarWidth = Math.Max(1, barWidth);
    }

    protected override Measurement Measure(RenderOptions options, int maxWidth)
    {
        var width = labelWidth + BarWidth;
        return new Measurement(width, width);
    }

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var filledBlock = new string('█', BarWidth);
        var emptyBlock = new string('░', BarWidth);

        for (var level = Max; level >= 1; level--)
        {
            var isFilled = level <= Value;

            yield return new Segment(level.ToString().PadLeft(labelWidth - 1) + " ", new Style(foreground: isFilled ? Color.White : Color.Grey35));
            yield return new Segment(
                isFilled ? filledBlock : emptyBlock,
                new Style(foreground: isFilled ? LevelColor(level) : Color.Grey23));

            if (level > 1)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    Color LevelColor(int level)
    {
        var fraction = (double)level / Max;
        return fraction switch
        {
            <= 0.34 => Color.Green,
            <= 0.67 => Color.Yellow,
            _ => Color.Red
        };
    }
}
