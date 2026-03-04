using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Tests;

public class TerminalStateBufferTests
{
    [Fact]
    public void CarriageReturn_ShouldOverwriteFromLineStart()
    {
        var buffer = new TerminalStateBuffer();
        buffer.ApplyChunk("hello\rY\n");

        Assert.Equal("Yello", buffer.VisibleLines.Single());
    }

    [Fact]
    public void Backspace_ShouldMoveCursorBackAndAllowOverwrite()
    {
        var buffer = new TerminalStateBuffer();
        buffer.ApplyChunk("abcd\b\bXY\n");

        Assert.Equal("abXY", buffer.VisibleLines.Single());
    }

    [Fact]
    public void CsiAndOsc_ShouldBeIgnoredFromVisibleText()
    {
        var buffer = new TerminalStateBuffer();
        buffer.ApplyChunk("aa\u001b[31m-red-\u001b[0m\u001b]0;title\u0007bb\n");

        Assert.Equal("aa-red-bb", buffer.VisibleLines.Single());
    }

    [Fact]
    public void Tab_ShouldExpandToNextTabStop()
    {
        var buffer = new TerminalStateBuffer();
        buffer.ApplyChunk("a\tb\n");

        Assert.Equal("a       b", buffer.VisibleLines.Single());
    }

    [Fact]
    public void EraseLineCsi_ShouldRemoveStaleTailAfterCarriageReturn()
    {
        var buffer = new TerminalStateBuffer();
        buffer.ApplyChunk("welcome to openai codex\r\u001b[2Kok\n");

        Assert.Equal("ok", buffer.VisibleLines.Single());
    }

    [Fact]
    public void EraseDisplayCsi_ShouldClearPreviousVisibleLines()
    {
        var buffer = new TerminalStateBuffer();
        buffer.ApplyChunk("line-1\nline-2\n\u001b[2Jfresh\n");

        Assert.Single(buffer.VisibleLines);
        Assert.Equal("fresh", buffer.VisibleLines.Single());
    }

    [Fact]
    public void EraseCharsCsi_ShouldBlankCellsWithoutShiftingTail()
    {
        var buffer = new TerminalStateBuffer();
        buffer.ApplyChunk("abcdef\u001b[3G\u001b[2X");

        Assert.Single(buffer.VisibleLines);
        Assert.Equal("ab  ef", buffer.VisibleLines.Single());
        Assert.Equal(2, buffer.CursorColumn);
    }
}
