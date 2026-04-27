using System.Collections.Concurrent;
using System.Text;

namespace Shared;

public sealed class QueueTextWriter : TextWriter
{
    private readonly ConcurrentQueue<string> _queue;
    private readonly StringBuilder _buffer = new();

    public QueueTextWriter(ConcurrentQueue<string> queue)
    {
        _queue = queue;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\n')
        {
            var line = _buffer.ToString().TrimEnd('\r');
            _buffer.Clear();
            if (line.Length > 0)
                _queue.Enqueue(line);
        }
        else
        {
            _buffer.Append(value);
        }
    }

    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _queue.Enqueue(value);
    }
}