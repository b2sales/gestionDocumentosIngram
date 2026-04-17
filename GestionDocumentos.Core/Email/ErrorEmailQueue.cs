using System.Threading.Channels;

namespace GestionDocumentos.Core.Email;

public sealed class ErrorEmailQueue
{
    private readonly Channel<ErrorEmailItem> _channel = Channel.CreateUnbounded<ErrorEmailItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ChannelWriter<ErrorEmailItem> Writer => _channel.Writer;

    public ChannelReader<ErrorEmailItem> Reader => _channel.Reader;
}
