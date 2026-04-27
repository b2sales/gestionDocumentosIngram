using GestionDocumentos.Gre;

namespace GestionDocumentos.Tests;

public sealed class GreProcessingGateTests
{
    [Fact]
    public void TryEnter_same_path_with_different_case_only_allows_first()
    {
        var gate = new GreProcessingGate();
        var path = Path.Combine(Path.GetTempPath(), "gate-test.pdf");
        var upperPath = path.ToUpperInvariant();

        Assert.True(gate.TryEnter(path));
        Assert.False(gate.TryEnter(upperPath));
    }

    [Fact]
    public void Exit_releases_path_for_future_processing()
    {
        var gate = new GreProcessingGate();
        var path = Path.Combine(Path.GetTempPath(), "gate-release-test.pdf");

        Assert.True(gate.TryEnter(path));

        gate.Exit(path);

        Assert.True(gate.TryEnter(path));
    }

    [Fact]
    public async Task TryEnter_under_concurrency_allows_single_winner()
    {
        var gate = new GreProcessingGate();
        var path = Path.Combine(Path.GetTempPath(), "gate-race-test.pdf");
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => gate.TryEnter(path)));

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(static x => x));
        Assert.Equal(31, results.Count(static x => !x));
    }
}
