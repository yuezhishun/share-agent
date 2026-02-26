using TerminalGateway.Api.Services;
using TerminalGateway.Api.Tests.Oracle;

namespace TerminalGateway.Api.Tests;

public class TerminalStateBufferOracleTests
{
    [Fact]
    [Trait("Category", "oracle")]
    public void CarriageReturn_ShouldMatchOracleState()
    {
        var buffer = new TerminalStateBuffer();
        using var oracle = new XTermOracleAdapter(80, 25);

        var chunk = "hello\rY\n";
        buffer.ApplyChunk(chunk);
        oracle.Feed(chunk);

        var expected = TerminalFrameNormalizer.FromOracle(oracle.Export());
        var actual = TerminalFrameNormalizer.FromBuffer(buffer, 80, 25);
        TerminalOracleAssert.EqualLoose(expected, actual);
    }

    [Fact]
    [Trait("Category", "oracle")]
    public void BackspaceSequence_ShouldMatchOracleState()
    {
        var buffer = new TerminalStateBuffer();
        using var oracle = new XTermOracleAdapter(80, 25);

        var chunk = "abcd\b\bXY\n";
        buffer.ApplyChunk(chunk);
        oracle.Feed(chunk);

        var expected = TerminalFrameNormalizer.FromOracle(oracle.Export());
        var actual = TerminalFrameNormalizer.FromBuffer(buffer, 80, 25);
        TerminalOracleAssert.EqualLoose(expected, actual);
    }

    [Fact]
    [Trait("Category", "oracle")]
    public void AnsiControlSequence_ShouldMatchOracleState()
    {
        var buffer = new TerminalStateBuffer();
        using var oracle = new XTermOracleAdapter(80, 25);

        var chunk = "before\u001b[31m-red-\u001b[0mafter\n";
        buffer.ApplyChunk(chunk);
        oracle.Feed(chunk);

        var expected = TerminalFrameNormalizer.FromOracle(oracle.Export());
        var actual = TerminalFrameNormalizer.FromBuffer(buffer, 80, 25);
        TerminalOracleAssert.EqualLoose(expected, actual);
    }

    [Fact]
    [Trait("Category", "oracle")]
    public void ChunkBoundarySplit_ShouldRemainConsistent()
    {
        var buffer = new TerminalStateBuffer();
        using var oracle = new XTermOracleAdapter(80, 25);

        var chunks = new[]
        {
            "A\u001b[3",
            "1mB\u001b[",
            "0m",
            "C\rZ",
            "\n"
        };

        foreach (var chunk in chunks)
        {
            buffer.ApplyChunk(chunk);
            oracle.Feed(chunk);
        }

        var expected = TerminalFrameNormalizer.FromOracle(oracle.Export());
        var actual = TerminalFrameNormalizer.FromBuffer(buffer, 80, 25);
        TerminalOracleAssert.EqualLoose(expected, actual);
    }

    [Fact]
    [Trait("Category", "oracle")]
    public void ResizeThenWrite_ShouldKeepStableFrame()
    {
        var buffer = new TerminalStateBuffer();
        using var oracle = new XTermOracleAdapter(80, 25);

        oracle.Resize(100, 30);
        var chunk = "resize-ok\n";
        buffer.ApplyChunk(chunk);
        oracle.Feed(chunk);

        var expected = TerminalFrameNormalizer.FromOracle(oracle.Export()) with { Cols = 100, Rows = 30 };
        var actual = TerminalFrameNormalizer.FromBuffer(buffer, 100, 30);
        TerminalOracleAssert.EqualLoose(expected, actual);
    }
}
