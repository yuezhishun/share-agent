namespace TerminalGateway.Api.Tests;

internal static class TestPaths
{
    private static readonly string _defaultCwd = EnsureDefaultCwd();

    public static string DefaultCwd => _defaultCwd;

    private static string EnsureDefaultCwd()
    {
        var path = Path.Combine(Path.GetTempPath(), "terminal-gateway-api-tests");
        Directory.CreateDirectory(path);
        return path;
    }
}
