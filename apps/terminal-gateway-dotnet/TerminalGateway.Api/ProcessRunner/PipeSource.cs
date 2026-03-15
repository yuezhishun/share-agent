using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessRunner;

/// <summary>
/// 管道输入源抽象基类，用于向进程提供标准输入
/// </summary>
public abstract class PipeSource
{
    /// <summary>
    /// 将数据异步复制到目标流
    /// </summary>
    /// <param name="destination">目标流</param>
    /// <param name="cancellationToken">取消令牌</param>
    public abstract Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从字符串创建输入源
    /// </summary>
    /// <param name="text">输入文本</param>
    /// <param name="encoding">编码（默认为UTF-8）</param>
    public static PipeSource FromString(string text, Encoding? encoding = null)
        => new StringPipeSource(text, encoding);

    /// <summary>
    /// 从流创建输入源
    /// </summary>
    /// <param name="stream">输入流</param>
    /// <param name="leaveOpen">完成后是否保持流打开</param>
    public static PipeSource FromStream(Stream stream, bool leaveOpen = false)
        => new StreamPipeSource(stream, leaveOpen);

    /// <summary>
    /// 从命令输出创建输入源（管道到stdin）
    /// </summary>
    /// <param name="command">源命令</param>
    public static PipeSource FromCommand(ProcessCommand command)
        => new CommandPipeSource(command);

    /// <summary>
    /// 从文件创建输入源
    /// </summary>
    /// <param name="filePath">文件路径</param>
    public static PipeSource FromFile(string filePath)
        => new FilePipeSource(filePath);

    /// <summary>
    /// 从字节数组创建输入源
    /// </summary>
    /// <param name="data">字节数据</param>
    public static PipeSource FromBytes(byte[] data)
        => new BytesPipeSource(data);

    /// <summary>
    /// 空输入源（不写入任何数据）
    /// </summary>
    public static PipeSource Null { get; } = new NullPipeSource();
}

/// <summary>
/// 字符串输入源
/// </summary>
internal class StringPipeSource : PipeSource
{
    private readonly string _text;
    private readonly Encoding _encoding;

    public StringPipeSource(string text, Encoding? encoding = null)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _encoding = encoding ?? Encoding.UTF8;
    }

    public override async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        var bytes = _encoding.GetBytes(_text);
        await destination.WriteAsync(bytes, cancellationToken);
    }
}

/// <summary>
/// 流输入源
/// </summary>
internal class StreamPipeSource : PipeSource
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public StreamPipeSource(Stream stream, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!_stream.CanRead)
            throw new ArgumentException("Stream must be readable", nameof(stream));
        _leaveOpen = leaveOpen;
    }

    public override async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        await _stream.CopyToAsync(destination, cancellationToken);
        if (!_leaveOpen)
            await _stream.DisposeAsync();
    }
}

/// <summary>
/// 命令输出作为输入源
/// </summary>
internal class CommandPipeSource : PipeSource
{
    private readonly ProcessCommand _command;

    public CommandPipeSource(ProcessCommand command)
    {
        _command = command ?? throw new ArgumentNullException(nameof(command));
    }

    public override async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        // 执行命令并将 stdout 写入 destination
        // 使用 ExecuteCoreAsync 避免递归问题
        var result = await _command.ExecuteAsync(cancellationToken);
        var bytes = Encoding.UTF8.GetBytes(result.StandardOutput);
        await destination.WriteAsync(bytes, cancellationToken);
    }
}

/// <summary>
/// 文件输入源
/// </summary>
internal class FilePipeSource : PipeSource
{
    private readonly string _filePath;

    public FilePipeSource(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
    }

    public override async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        await using var fileStream = File.OpenRead(_filePath);
        await fileStream.CopyToAsync(destination, cancellationToken);
    }
}

/// <summary>
/// 字节数组输入源
/// </summary>
internal class BytesPipeSource : PipeSource
{
    private readonly byte[] _data;

    public BytesPipeSource(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public override async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        await destination.WriteAsync(_data, cancellationToken);
    }
}

/// <summary>
/// 空输入源
/// </summary>
internal class NullPipeSource : PipeSource
{
    public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
