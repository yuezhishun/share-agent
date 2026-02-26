using Xunit.Sdk;

namespace TerminalGateway.Api.Tests.Oracle;

public static class TerminalOracleAssert
{
    public static void EqualLoose(NormalizedFrame expected, NormalizedFrame actual, string because = "")
    {
        Assert.Equal(expected.Cols, actual.Cols);
        Assert.Equal(expected.Rows, actual.Rows);

        var expectedLines = expected.VisibleLines;
        var actualLines = actual.VisibleLines;
        Assert.True(actualLines.Count >= expectedLines.Count, $"actual lines({actualLines.Count}) < expected lines({expectedLines.Count})");
        var offset = actualLines.Count - expectedLines.Count;
        for (var i = 0; i < expectedLines.Count; i++)
        {
            Assert.Equal(expectedLines[i], actualLines[offset + i]);
        }

        var cursorTolerance = 1;
        if (Math.Abs(expected.CursorY - actual.CursorY) > cursorTolerance)
        {
            throw new XunitException($"cursor row mismatch. expectedY={expected.CursorY} actualY={actual.CursorY} {because}".Trim());
        }
    }
}
