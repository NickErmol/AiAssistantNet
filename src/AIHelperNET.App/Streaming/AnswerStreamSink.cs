using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.App.Streaming;

public sealed class AnswerStreamSink : IAnswerStreamSink
{
    private Action<AnswerId, string>? _handler;

    public void SetHandler(Action<AnswerId, string> handler)
        => _handler = handler;

    public ValueTask PushAsync(AnswerId answerId, string chunk, CancellationToken ct)
    {
        if (_handler is not null)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _handler(answerId, chunk));
        }
        return ValueTask.CompletedTask;
    }
}
