using Spectre.Console.Rendering;

namespace Shared;

public interface IControl
{
    bool Match(char input, int? param, out string? log);

    Renderable ReportState();
}