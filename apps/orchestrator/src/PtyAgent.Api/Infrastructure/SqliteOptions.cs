namespace PtyAgent.Api.Infrastructure;

public sealed class SqliteOptions
{
    public string DbPath { get; set; } = "data/pty-agent.db";
    public string LogsPath { get; set; } = "data/logs";
    public string WorkdirsPath { get; set; } = "data/workdirs";
}
