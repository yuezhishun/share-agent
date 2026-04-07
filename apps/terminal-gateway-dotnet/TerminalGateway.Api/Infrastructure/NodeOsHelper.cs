namespace TerminalGateway.Api.Infrastructure;

public static class NodeOsHelper
{
    public static string Current =>
        OperatingSystem.IsWindows() ? "windows" : "linux";

    public static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "windows" ? "windows" : "linux";
    }

    public static string PathSeparatorFor(string? nodeOs)
    {
        return string.Equals(Normalize(nodeOs), "windows", StringComparison.Ordinal) ? ";" : ":";
    }
}
