using System.Reflection;
using GestionDocumentos.Core.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Tests;

public sealed class EmailInfrastructureTests
{
    [Fact]
    public void ErrorEmailLoggerProvider_enqueues_only_error_or_higher()
    {
        var queue = new ErrorEmailQueue();
        var provider = new ErrorEmailLoggerProvider(
            queue,
            new TestOptionsMonitor<SmtpErrorEmailOptions>(new SmtpErrorEmailOptions { Enabled = true }));
        var logger = provider.CreateLogger("GestionDocumentos.Gre.SomeComponent");

        logger.LogWarning("warning");
        logger.LogError("error");

        Assert.True(queue.Reader.TryRead(out var item));
        Assert.NotNull(item);
        Assert.Equal(LogLevel.Error, item!.Level);
        Assert.Equal("error", item.Message);
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public void ErrorEmailLoggerProvider_does_not_enqueue_self_category_to_avoid_recursive_loop()
    {
        var queue = new ErrorEmailQueue();
        var provider = new ErrorEmailLoggerProvider(
            queue,
            new TestOptionsMonitor<SmtpErrorEmailOptions>(new SmtpErrorEmailOptions { Enabled = true }));
        var logger = provider.CreateLogger("GestionDocumentos.Core.Email.SmtpErrorEmailSender");

        logger.LogError("smtp failed");

        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public void ErrorEmailQueue_drops_oldest_when_capacity_is_exceeded()
    {
        var queue = new ErrorEmailQueue();

        for (var i = 0; i < ErrorEmailQueue.Capacity + 20; i++)
        {
            var ok = queue.Writer.TryWrite(new ErrorEmailItem(
                "cat",
                LogLevel.Error,
                $"m-{i}",
                null,
                DateTimeOffset.UtcNow));
            Assert.True(ok);
        }

        var messages = new List<string>();
        while (queue.Reader.TryRead(out var item))
        {
            messages.Add(item.Message);
        }

        Assert.Equal(ErrorEmailQueue.Capacity, messages.Count);
        Assert.DoesNotContain("m-0", messages);
        Assert.Contains($"m-{ErrorEmailQueue.Capacity + 19}", messages);
    }

    [Fact]
    public async Task SmtpErrorEmailSender_short_circuits_safely_when_disabled_or_invalid()
    {
        var senderDisabled = new SmtpErrorEmailSender(
            new TestOptionsMonitor<SmtpErrorEmailOptions>(new SmtpErrorEmailOptions { Enabled = false }));
        await senderDisabled.SendBatchAsync(
            [new ErrorEmailItem("cat", LogLevel.Error, "msg", null, DateTimeOffset.UtcNow)],
            CancellationToken.None);

        var senderInvalid = new SmtpErrorEmailSender(
            new TestOptionsMonitor<SmtpErrorEmailOptions>(new SmtpErrorEmailOptions
            {
                Enabled = true,
                Host = "",
                From = "",
                To = ""
            }));
        await senderInvalid.SendBatchAsync(
            [new ErrorEmailItem("cat", LogLevel.Error, "msg", null, DateTimeOffset.UtcNow)],
            CancellationToken.None);
    }

    [Fact]
    public void SmtpErrorEmailSender_ParseAddresses_normalizes_and_deduplicates()
    {
        var method = typeof(SmtpErrorEmailSender).GetMethod(
            "ParseAddresses",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var raw = "a@x.com; b@y.com, a@x.com \n c@z.com\r\n";
        var list = method!.Invoke(null, [raw]) as List<string>;

        Assert.NotNull(list);
        Assert.Equal(3, list!.Count);
        Assert.Contains("a@x.com", list);
        Assert.Contains("b@y.com", list);
        Assert.Contains("c@z.com", list);
    }

    private sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        public TestOptionsMonitor(TOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public TOptions CurrentValue { get; private set; }

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
