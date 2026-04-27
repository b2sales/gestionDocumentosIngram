using GestionDocumentos.Core.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Tests;

public sealed class ErrorEmailQueueProcessorHostedServiceTests
{
    [Fact]
    public async Task ProcessSingleBatchAsync_applies_throttle_and_drops_second_item()
    {
        var now = DateTimeOffset.UtcNow;
        var sender = new FakeSender();
        var queue = new ErrorEmailQueue();
        var options = new TestOptionsMonitor<SmtpErrorEmailOptions>(new SmtpErrorEmailOptions
        {
            Enabled = true,
            ThrottleSeconds = 120,
            AggregationWindowSeconds = 0,
            MaxBatchSize = 10
        });

        var service = new ErrorEmailQueueProcessorHostedService(
            queue,
            sender,
            options,
            NullLogger<ErrorEmailQueueProcessorHostedService>.Instance,
            utcNow: () => now);

        queue.Writer.TryWrite(NewItem("first"));
        var firstProcessed = await service.ProcessSingleBatchAsync(CancellationToken.None);

        queue.Writer.TryWrite(NewItem("second"));
        var secondProcessed = await service.ProcessSingleBatchAsync(CancellationToken.None);

        Assert.True(firstProcessed);
        Assert.True(secondProcessed);
        Assert.Single(sender.Batches);
        var batch = sender.Batches.Single();
        Assert.Single(batch);
        Assert.Equal("first", batch[0].Message);
    }

    [Fact]
    public async Task ProcessSingleBatchAsync_aggregates_items_within_window_until_max_batch_size()
    {
        var now = DateTimeOffset.UtcNow;
        var sender = new FakeSender();
        var queue = new ErrorEmailQueue();
        var options = new TestOptionsMonitor<SmtpErrorEmailOptions>(new SmtpErrorEmailOptions
        {
            Enabled = true,
            ThrottleSeconds = 0,
            AggregationWindowSeconds = 1,
            MaxBatchSize = 3
        });

        var service = new ErrorEmailQueueProcessorHostedService(
            queue,
            sender,
            options,
            NullLogger<ErrorEmailQueueProcessorHostedService>.Instance,
            utcNow: () => now);

        queue.Writer.TryWrite(NewItem("m1"));
        queue.Writer.TryWrite(NewItem("m2"));
        queue.Writer.TryWrite(NewItem("m3"));
        queue.Writer.TryWrite(NewItem("m4"));

        var processed = await service.ProcessSingleBatchAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Single(sender.Batches);
        var batch = sender.Batches.Single();
        Assert.Equal(3, batch.Count);
        Assert.Equal("m1", batch[0].Message);
        Assert.Equal("m2", batch[1].Message);
        Assert.Equal("m3", batch[2].Message);
        Assert.True(queue.Reader.TryRead(out var remaining));
        Assert.Equal("m4", remaining.Message);
    }

    private static ErrorEmailItem NewItem(string message) =>
        new("cat", Microsoft.Extensions.Logging.LogLevel.Error, message, null, DateTimeOffset.UtcNow);

    private sealed class FakeSender : IErrorEmailSender
    {
        public List<IReadOnlyList<ErrorEmailItem>> Batches { get; } = [];

        public Task SendBatchAsync(IReadOnlyList<ErrorEmailItem> items, CancellationToken cancellationToken)
        {
            Batches.Add(items.ToList());
            return Task.CompletedTask;
        }
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
