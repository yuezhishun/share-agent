namespace TerminalGateway.Api.Infrastructure;

public static class CommandParser
{
    public static (string File, IReadOnlyList<string> Args) Parse(string command)
    {
        var trimmed = (command ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return DefaultCommand();
        }

        var tokens = Tokenize(trimmed);
        if (tokens.Count == 0)
        {
            return DefaultCommand();
        }

        return (tokens[0], tokens.Skip(1).ToList());
    }

    private static (string File, IReadOnlyList<string> Args) DefaultCommand()
    {
        return (OperatingSystem.IsWindows() ? (Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe") : (Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash"), []);
    }

    private static List<string> Tokenize(string value)
    {
        var output = new List<string>();
        var current = new List<char>();
        var quote = '\0';

        foreach (var c in value)
        {
            if (quote == '\0')
            {
                if (c is '"' or '\'')
                {
                    quote = c;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (current.Count > 0)
                    {
                        output.Add(new string([.. current]));
                        current.Clear();
                    }
                    continue;
                }

                current.Add(c);
                continue;
            }

            if (c == quote)
            {
                quote = '\0';
                continue;
            }

            current.Add(c);
        }

        if (current.Count > 0)
        {
            output.Add(new string([.. current]));
        }

        return output;
    }
}
