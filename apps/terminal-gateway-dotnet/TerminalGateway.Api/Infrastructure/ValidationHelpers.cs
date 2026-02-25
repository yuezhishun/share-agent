namespace TerminalGateway.Api.Infrastructure;

public static class ValidationHelpers
{
    public static int ClampInt(int? value, int min, int max, int fallback)
    {
        if (!value.HasValue)
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, value.Value));
    }

    public static int? ParseNullableInt(string? value)
    {
        if (int.TryParse(value, out var x))
        {
            return x;
        }

        return null;
    }
}
