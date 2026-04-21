using System.Threading.Channels;

namespace GestionDocumentos.Core.Email;

public sealed class ErrorEmailQueue
{
    /// <summary>
    /// Capacidad máxima de la cola. Cuando se llena se descartan los items más viejos
    /// (<see cref="BoundedChannelFullMode.DropOldest"/>) para evitar memory leaks si el SMTP
    /// está caído y se generan correos en rafaga.
    /// </summary>
    public const int Capacity = 500;

    private readonly Channel<ErrorEmailItem> _channel = Channel.CreateBounded<ErrorEmailItem>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public ChannelWriter<ErrorEmailItem> Writer => _channel.Writer;

    public ChannelReader<ErrorEmailItem> Reader => _channel.Reader;
}
