using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessRunner;

/// <summary>
/// 管道输出目标抽象基类，用于接收进程的标准输出和错误
/// </summary>
public abstract class PipeTarget
{
    /// <summary>
    /// 从流异步复制数据到目标
    /// </summary>
    /// <param name="source">源流（stdout 或 stderr）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public abstract Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建将输出写入流的管道目标
    /// </summary>
    /// <param name="stream">目标流</param>
    /// <param name="leaveOpen">完成后是否保持流打开</param>
    public static PipeTarget ToStream(Stream stream, bool leaveOpen = false)
        => new StreamPipeTarget(stream, leaveOpen);

    /// <summary>
    /// 创建将输出写入文件的管道目标
    /// </summary>
    /// <param name="filePath">文件路径</param>
    public static PipeTarget ToFile(string filePath)
        => new FilePipeTarget(filePath);

    /// <summary>
    /// 创建将输出写入 StringBuilder 的管道目标
    /// </summary>
    /// <param name="stringBuilder">StringBuilder 实例</param>
    public static PipeTarget ToStringBuilder(StringBuilder stringBuilder)
        => new StringBuilderPipeTarget(stringBuilder);

    /// <summary>
    /// 创建将输出发送到委托的管道目标
    /// </summary>
    /// <param name="handler">行处理委托</param>
    public static PipeTarget ToDelegate(Action<string> handler)
        => new DelegatePipeTarget(handler);

    /// <summary>
    /// 创建将输出发送到异步委托的管道目标
    /// </summary>
    /// <param name="handler">异步行处理委托</param>
    public static PipeTarget ToDelegate(Func<string, Task> handler)
        => new AsyncDelegatePipeTarget(handler);

    /// <summary>
    /// 创建合并多个管道目标的复合目标
    /// </summary>
    /// <param name="targets">目标数组</param>
    public static PipeTarget Merge(params PipeTarget[] targets)
        => new MergedPipeTarget(targets);

    /// <summary>
    /// 空管道目标（丢弃所有输出）
    /// </summary>
    public static PipeTarget Null { get; } = new NullPipeTarget();
}

/// <summary>
/// 流管道目标
/// </summary>
internal class StreamPipeTarget : PipeTarget
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public StreamPipeTarget(Stream stream, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!_stream.CanWrite)
            throw new ArgumentException("Stream must be writable", nameof(stream));
        _leaveOpen = leaveOpen;
    }

    public override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        try
        {
            await source.CopyToAsync(_stream, cancellationToken);
        }
        finally
        {
            if (!_leaveOpen)
                await _stream.DisposeAsync();
        }
    }
}

/// <summary>
/// 文件管道目标
/// </summary>
internal class FilePipeTarget : PipeTarget
{
    private readonly string _filePath;

    public FilePipeTarget(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        await using var fileStream = File.Create(_filePath);
        await source.CopyToAsync(fileStream, cancellationToken);
    }
}

/// <summary>
/// StringBuilder 管道目标
/// </summary>
internal class StringBuilderPipeTarget : PipeTarget
{
    private readonly StringBuilder _stringBuilder;

    public StringBuilderPipeTarget(StringBuilder stringBuilder)
    {
        _stringBuilder = stringBuilder ?? throw new ArgumentNullException(nameof(stringBuilder));
    }

    public override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
        var buffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (charsRead == 0) break;

            lock (_stringBuilder)
            {
                _stringBuilder.Append(buffer, 0, charsRead);
            }
        }
    }
}

/// <summary>
/// 委托管道目标（同步）
/// </summary>
internal class DelegatePipeTarget : PipeTarget
{
    private readonly Action<string> _handler;

    public DelegatePipeTarget(Action<string> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            _handler(line);
        }
    }
}

/// <summary>
/// 委托管道目标（异步）
/// </summary>
internal class AsyncDelegatePipeTarget : PipeTarget
{
    private readonly Func<string, Task> _handler;

    public AsyncDelegatePipeTarget(Func<string, Task> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            await _handler(line);
        }
    }
}

/// <summary>
/// 合并多个管道目标的复合目标
/// </summary>
internal class MergedPipeTarget : PipeTarget
{
    private readonly PipeTarget[] _targets;

    public MergedPipeTarget(PipeTarget[] targets)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        if (_targets.Length == 0)
            throw new ArgumentException("At least one target is required", nameof(targets));
    }

    public override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        // 为每个目标创建独立的内存流副本
        var tasks = new List<Task>();

        foreach (var target in _targets)
        {
            // 重置流位置（如果支持）
            if (source.CanSeek)
            {
                source.Position = 0;
            }

            // 创建内存流副本
            var memoryStream = new MemoryStream();
            await source.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            tasks.Add(target.CopyFromAsync(memoryStream, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }
}

/// <summary>
/// 空管道目标（丢弃所有输出）
/// </summary>
internal class NullPipeTarget : PipeTarget
{
    public override Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        // 读取并丢弃所有数据
        return source.CopyToAsync(Stream.Null, cancellationToken);
    }
}
