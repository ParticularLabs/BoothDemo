using Spectre.Console;
using Spectre.Console.Rendering;

namespace Shared;

/// <summary>
/// Renders a setting as a vertical bar, like a mixer fader. Each text row shows two levels using half-block characters.
/// </summary>
public sealed class VerticalSettingIndicator : Renderable
{
    public int Value { get; }
    public int Max { get; }
    public int BarWidth { get; }

    public VerticalSettingIndicator(int value, int max = 9, int barWidth = 10)
    {
        Max = Math.Max(1, max);
        Value = Math.Clamp(value, 0, Max);
        BarWidth = Math.Max(1, barWidth);
    }

    protected override Measurement Measure(RenderOptions options, int maxWidth) => new(BarWidth, BarWidth);

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        // Lower half block: the foreground paints the lower level of the row, the background the upper one
        var halfBlocks = new string('▄', BarWidth);
        var rows = (Max + 1) / 2;

        for (var row = rows - 1; row >= 0; row--)
        {
            var lowerLevel = row * 2 + 1;
            var upperLevel = lowerLevel + 1;

            var style = new Style(
                foreground: LevelColor(lowerLevel),
                background: upperLevel <= Max ? LevelColor(upperLevel) : Color.Default);
            yield return new Segment(halfBlocks, style);

            if (row > 0)
            {
                yield return Segment.LineBreak;
            }
        }
    }

    Color LevelColor(int level)
    {
        if (level > Value)
        {
            return Color.Grey23;
        }

        var fraction = (double)level / Max;
        return fraction switch
        {
            <= 0.34 => Color.Green,
            <= 0.67 => Color.Yellow,
            _ => Color.Red
        };
    }
}
