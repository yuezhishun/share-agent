using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Tests;

public class GatewayBackpressureAndOrderTests
{
    [Fact]
    public void HighThroughputChunks_ShouldKeepOrdering()
    {
        var buffer = new TerminalStateBuffer(maxVisibleLines: 5000);
        var chunks = Enumerable.Range(0, 1000).Select(i => $"line-{i}\n").ToList();

        foreach (var chunk in chunks)
        {
            buffer.ApplyChunk(chunk);
        }

        var lines = buffer.VisibleLines;
        Assert.Equal(1000, lines.Count);
        Assert.Equal("line-0", lines[0]);
        Assert.Equal("line-999", lines[^1]);
    }

    [Fact]
    public void MaxVisibleLines_ShouldTrimOldestWithoutReordering()
    {
        var buffer = new TerminalStateBuffer(maxVisibleLines: 50);

        for (var i = 0; i < 120; i++)
        {
            buffer.ApplyChunk($"row-{i}\n");
        }

        var lines = buffer.VisibleLines;
        Assert.Equal(50, lines.Count);
        Assert.Equal("row-70", lines[0]);
        Assert.Equal("row-119", lines[^1]);
    }
}
