using System.Text;

namespace TerminalGateway.Api.Services;

public sealed class FileApiService
{
    private const int FilePreviewMaxBytes = 1024 * 1024;
    private const int TextProbeBytes = 8192;
    public const int UploadMaxBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> UploadAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    };

    public object List(string basePath, string? inputPath, bool showHidden)
    {
        var root = Path.GetFullPath(basePath);
        var targetPath = ResolveWithinBase(root, inputPath);
        if (targetPath is null)
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (!Directory.Exists(targetPath))
        {
            throw new DirectoryNotFoundException("Path not found");
        }

        var items = Directory.GetFileSystemEntries(targetPath)
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var attr = File.GetAttributes(path);
                var isDir = attr.HasFlag(FileAttributes.Directory);
                var isSym = attr.HasFlag(FileAttributes.ReparsePoint);
                var info = isDir ? new DirectoryInfo(path) as FileSystemInfo : new FileInfo(path);
                return new
                {
                    name,
                    path,
                    kind = isDir ? "dir" : (isSym ? "symlink" : "file"),
                    size = isDir ? (long?)null : ((FileInfo)info).Length,
                    mtime = info.LastWriteTimeUtc.ToString("O")
                };
            })
            .Where(x => showHidden || !x.name.StartsWith(".", StringComparison.Ordinal))
            .OrderBy(x => x.kind == "dir" ? 0 : 1)
            .ThenBy(x => x.name, StringComparer.Ordinal)
            .ToList();

        return new
        {
            @base = root,
            path = targetPath,
            parent = ResolveParent(targetPath, root),
            items
        };
    }

    public async Task<object> ReadAsync(string basePath, string? inputPath, int maxLines, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(basePath);
        var targetPath = ResolveWithinBase(root, inputPath);
        if (targetPath is null)
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("File not found", targetPath);
        }

        var info = new FileInfo(targetPath);
        if (!await IsLikelyTextFileAsync(targetPath, cancellationToken))
        {
            throw new InvalidDataException("File is not a supported text file");
        }

        var preview = await ReadTextPreviewAsync(targetPath, maxLines, cancellationToken);
        return new
        {
            path = targetPath,
            encoding = "utf-8",
            size = info.Length,
            content = preview.Content,
            lines_shown = preview.LinesShown,
            max_lines = maxLines,
            truncated = preview.Truncated,
            truncate_reason = preview.TruncateReason,
            byte_limit = FilePreviewMaxBytes
        };
    }

    public async Task<object> SaveUploadAsync(
        string basePath,
        string? targetCwd,
        string fileName,
        Stream content,
        long? expectedLength,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(basePath);
        var cwd = ResolveWithinBase(root, targetCwd);
        if (cwd is null)
        {
            throw new UnauthorizedAccessException("cwd is outside allowed base");
        }

        var rawName = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (rawName.Length == 0)
        {
            throw new InvalidDataException("file name is required");
        }

        var ext = Path.GetExtension(rawName);
        if (!UploadAllowedExtensions.Contains(ext))
        {
            throw new InvalidDataException("unsupported file type");
        }

        if (expectedLength is > UploadMaxBytes)
        {
            throw new InvalidDataException("file too large");
        }

        var uploadDir = Path.Combine(cwd, ".webcli-uploads");
        Directory.CreateDirectory(uploadDir);

        var finalName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var targetPath = Path.Combine(uploadDir, finalName);
        await using var output = File.Create(targetPath);
        var copied = await CopyWithLimitAsync(content, output, UploadMaxBytes, cancellationToken);

        return new
        {
            path = targetPath,
            size = copied,
            name = finalName
        };
    }

    public Task<object> SaveUploadBytesAsync(string basePath, string? targetCwd, string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream(bytes, writable: false);
        return SaveUploadAsync(basePath, targetCwd, fileName, stream, bytes.Length, cancellationToken);
    }

    private static string? ResolveWithinBase(string basePath, string? inputPath)
    {
        var candidate = string.IsNullOrWhiteSpace(inputPath) ? basePath : Path.GetFullPath(inputPath.Trim());
        if (candidate == basePath || candidate.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return candidate;
        }

        return null;
    }

    private static string? ResolveParent(string path, string basePath)
    {
        var parent = Directory.GetParent(path)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
        {
            return null;
        }

        return parent == basePath || parent.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? parent
            : null;
    }

    private static async Task<bool> IsLikelyTextFileAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        var buffer = new byte[Math.Min((int)stream.Length, TextProbeBytes)];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        if (read <= 0)
        {
            return true;
        }

        for (var i = 0; i < read; i++)
        {
            var b = buffer[i];
            if (b == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<(string Content, int LinesShown, bool Truncated, string? TruncateReason)> ReadTextPreviewAsync(string file, int maxLines, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var lines = new List<string>();
        var bytes = 0;
        var truncated = false;
        string? reason = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            var lineBytes = Encoding.UTF8.GetByteCount(line + "\n");
            if (bytes + lineBytes > FilePreviewMaxBytes)
            {
                truncated = true;
                reason = "byte_limit";
                break;
            }

            lines.Add(line);
            bytes += lineBytes;
            if (lines.Count >= maxLines)
            {
                truncated = !reader.EndOfStream;
                reason = truncated ? "max_lines" : null;
                break;
            }
        }

        return (string.Join("\n", lines), lines.Count, truncated, reason);
    }

    private static async Task<long> CopyWithLimitAsync(Stream input, Stream output, int maxBytes, CancellationToken cancellationToken)
    {
        var total = 0L;
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("file too large");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total;
    }
}
