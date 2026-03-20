using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;

namespace TerminalGateway.Api.Services;

public sealed class FileApiService
{
    private const int DefaultLargeFileThresholdBytes = 100 * 1024;
    private const int DefaultFileChunkBytes = 64 * 1024;
    private const int DefaultFileChunkMaxLines = 800;
    private const int FilePreviewMaxBytes = 1024 * 1024;
    private const int FileEditMaxBytes = 2 * 1024 * 1024;
    private const int TextProbeBytes = 8192;
    public const int UploadMaxBytes = 10 * 1024 * 1024;
    public const int GenericUploadMaxBytes = 25 * 1024 * 1024;
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();
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

    public async Task<object> ReadAsync(
        string basePath,
        string? inputPath,
        int maxLines,
        string? mode,
        CancellationToken cancellationToken,
        int? chunkBytes = null,
        int? lineOffset = null,
        string? direction = null,
        int? largeFileThresholdBytes = null)
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

        var normalizedMode = NormalizeReadMode(mode);
        var effectiveChunkBytes = Math.Clamp(chunkBytes ?? DefaultFileChunkBytes, 1, FilePreviewMaxBytes);
        var effectiveChunkMaxLines = Math.Clamp(maxLines > 0 ? maxLines : DefaultFileChunkMaxLines, 1, 5000);
        var effectiveLargeFileThresholdBytes = Math.Clamp(largeFileThresholdBytes ?? DefaultLargeFileThresholdBytes, 1, FileEditMaxBytes);

        if (normalizedMode == "edit")
        {
            if (info.Length > effectiveLargeFileThresholdBytes)
            {
                var progressive = await ReadLargeFileChunkAsync(
                    targetPath,
                    effectiveChunkMaxLines,
                    effectiveChunkBytes,
                    Math.Max(0, lineOffset ?? 0),
                    NormalizeChunkDirection(direction),
                    cancellationToken);
                return BuildProgressiveReadPayload(targetPath, info.Length, progressive);
            }

            var content = await ReadTextForEditAsync(targetPath, cancellationToken);
            var lineCount = CountLines(content);
            return new
            {
                path = targetPath,
                encoding = "utf-8",
                size = info.Length,
                content,
                lines_shown = lineCount,
                max_lines = (int?)null,
                truncated = false,
                truncate_reason = (string?)null,
                byte_limit = FileEditMaxBytes,
                chunk_bytes = (int?)null,
                read_only = false,
                large_file = false,
                has_more_before = false,
                has_more_after = false,
                cursor_start = 0,
                cursor_end = lineCount,
                loaded_bytes = Encoding.UTF8.GetByteCount(content),
                loaded_lines = lineCount,
                mode = normalizedMode
            };
        }

        if (normalizedMode == "progressive")
        {
            var progressive = await ReadLargeFileChunkAsync(
                targetPath,
                effectiveChunkMaxLines,
                effectiveChunkBytes,
                Math.Max(0, lineOffset ?? 0),
                NormalizeChunkDirection(direction),
                cancellationToken);
            return BuildProgressiveReadPayload(targetPath, info.Length, progressive);
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
            byte_limit = FilePreviewMaxBytes,
            chunk_bytes = (int?)null,
            read_only = true,
            large_file = false,
            has_more_before = false,
            has_more_after = false,
            cursor_start = 0,
            cursor_end = preview.LinesShown,
            loaded_bytes = Encoding.UTF8.GetByteCount(preview.Content),
            loaded_lines = preview.LinesShown,
            mode = normalizedMode
        };
    }

    public async Task<object> WriteAsync(string basePath, string? inputPath, string? content, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(basePath);
        var targetPath = ResolveWithinBase(root, inputPath);
        if (targetPath is null)
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (Directory.Exists(targetPath))
        {
            throw new InvalidDataException("Path is a directory");
        }

        var parentPath = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException("Path not found");
        }

        if (File.Exists(targetPath) && !await IsLikelyTextFileAsync(targetPath, cancellationToken))
        {
            throw new InvalidDataException("File is not a supported text file");
        }

        var text = content ?? string.Empty;
        var bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes > FilePreviewMaxBytes * 2)
        {
            throw new InvalidDataException("content too large");
        }

        await File.WriteAllTextAsync(targetPath, text, new UTF8Encoding(false), cancellationToken);
        var info = new FileInfo(targetPath);
        return new
        {
            path = targetPath,
            size = info.Length,
            mtime = info.LastWriteTimeUtc.ToString("O"),
            encoding = "utf-8"
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

    public object CreateDirectory(string basePath, string? parentPath, string? name)
    {
        var root = Path.GetFullPath(basePath);
        var parent = ResolveWithinBase(root, parentPath);
        if (parent is null)
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException("Path not found");
        }

        var directoryName = NormalizeName(name, "name");
        var target = Path.GetFullPath(Path.Combine(parent, directoryName));
        if (!IsWithinBase(root, target))
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (File.Exists(target))
        {
            throw new IOException("A file with the same name already exists");
        }

        Directory.CreateDirectory(target);
        return BuildEntryPayload(target);
    }

    public object RenameEntry(string basePath, string? inputPath, string? newName)
    {
        var root = Path.GetFullPath(basePath);
        var targetPath = ResolveWithinBase(root, inputPath);
        if (targetPath is null)
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        var existsAsFile = File.Exists(targetPath);
        var existsAsDir = Directory.Exists(targetPath);
        if (!existsAsFile && !existsAsDir)
        {
            throw new FileNotFoundException("Path not found", targetPath);
        }

        var parent = Directory.GetParent(targetPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) || !IsWithinBase(root, parent))
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        var normalizedName = NormalizeName(newName, "new_name");
        var destination = Path.GetFullPath(Path.Combine(parent, normalizedName));
        if (!IsWithinBase(root, destination))
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (string.Equals(targetPath, destination, StringComparison.Ordinal))
        {
            return BuildEntryPayload(targetPath);
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("Target already exists");
        }

        if (existsAsFile)
        {
            File.Move(targetPath, destination);
        }
        else
        {
            Directory.Move(targetPath, destination);
        }

        return BuildEntryPayload(destination);
    }

    public object RemoveEntry(string basePath, string? inputPath, bool recursive)
    {
        var root = Path.GetFullPath(basePath);
        var targetPath = ResolveWithinBase(root, inputPath);
        if (targetPath is null)
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (string.Equals(targetPath, root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot remove base path");
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
            return new { ok = true, path = targetPath, kind = "file" };
        }

        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, recursive);
            return new { ok = true, path = targetPath, kind = "dir" };
        }

        throw new FileNotFoundException("Path not found", targetPath);
    }

    public async Task<object> UploadToPathAsync(
        string basePath,
        string? targetPath,
        string fileName,
        Stream content,
        long? expectedLength,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(basePath);
        var directory = ResolveWithinBase(root, targetPath);
        if (directory is null)
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("Path not found");
        }

        var normalizedName = NormalizeName(fileName, "file");
        if (expectedLength is > GenericUploadMaxBytes)
        {
            throw new InvalidDataException("file too large");
        }

        var destination = EnsureUniquePath(directory, normalizedName);
        await using var output = File.Create(destination);
        var copied = await CopyWithLimitAsync(content, output, GenericUploadMaxBytes, cancellationToken);

        return new
        {
            path = destination,
            size = copied,
            name = Path.GetFileName(destination)
        };
    }

    public DownloadStreamResult OpenDownloadStream(string basePath, string? inputPath)
    {
        var root = Path.GetFullPath(basePath);
        var targetPath = ResolveWithinBase(root, inputPath);
        if (targetPath is null)
        {
            throw new UnauthorizedAccessException("Path is outside allowed base");
        }

        if (File.Exists(targetPath))
        {
            var name = Path.GetFileName(targetPath);
            var contentType = ContentTypeProvider.TryGetContentType(name, out var mime)
                ? mime
                : "application/octet-stream";
            var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
            return new DownloadStreamResult(stream, name, contentType, EnableRangeProcessing: true);
        }

        if (Directory.Exists(targetPath))
        {
            var folderName = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = "archive";
            }

            var zipName = $"{folderName}.zip";
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"webcli-download-{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(targetPath, tempZipPath, CompressionLevel.Fastest, includeBaseDirectory: true);
            var stream = new FileStream(
                tempZipPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            return new DownloadStreamResult(stream, zipName, "application/zip", EnableRangeProcessing: true);
        }

        throw new FileNotFoundException("Path not found", targetPath);
    }

    public sealed record DownloadStreamResult(
        Stream Stream,
        string Name,
        string ContentType,
        bool EnableRangeProcessing);

    private static string? ResolveWithinBase(string basePath, string? inputPath)
    {
        var candidate = string.IsNullOrWhiteSpace(inputPath) ? basePath : Path.GetFullPath(inputPath.Trim());
        if (IsWithinBase(basePath, candidate))
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

    private static bool IsWithinBase(string basePath, string targetPath)
    {
        return targetPath == basePath
               || targetPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NormalizeName(string? input, string fieldName)
    {
        var normalized = Path.GetFileName((input ?? string.Empty).Trim());
        if (normalized.Length == 0 || normalized is "." or "..")
        {
            throw new InvalidDataException($"{fieldName} is required");
        }

        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException($"{fieldName} contains invalid characters");
        }

        return normalized;
    }

    private static string EnsureUniquePath(string directory, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var index = 0;
        var candidate = Path.Combine(directory, fileName);
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            index += 1;
            candidate = Path.Combine(directory, $"{stem} ({index}){ext}");
        }

        return candidate;
    }

    private static object BuildEntryPayload(string path)
    {
        var attr = File.GetAttributes(path);
        var isDir = attr.HasFlag(FileAttributes.Directory);
        var isSym = attr.HasFlag(FileAttributes.ReparsePoint);
        var info = isDir ? new DirectoryInfo(path) as FileSystemInfo : new FileInfo(path);
        return new
        {
            name = Path.GetFileName(path),
            path,
            kind = isDir ? "dir" : (isSym ? "symlink" : "file"),
            size = isDir ? (long?)null : ((FileInfo)info).Length,
            mtime = info.LastWriteTimeUtc.ToString("O")
        };
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

    private static async Task<ProgressiveChunkResult> ReadLargeFileChunkAsync(
        string file,
        int maxLines,
        int chunkBytes,
        int lineOffset,
        string direction,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);

        return string.Equals(direction, "tail", StringComparison.Ordinal)
            ? await ReadTailChunkAsync(reader, maxLines, chunkBytes, cancellationToken)
            : await ReadForwardChunkAsync(reader, maxLines, chunkBytes, lineOffset, cancellationToken);
    }

    private static async Task<ProgressiveChunkResult> ReadForwardChunkAsync(
        StreamReader reader,
        int maxLines,
        int chunkBytes,
        int lineOffset,
        CancellationToken cancellationToken)
    {
        var skipped = 0;
        while (skipped < lineOffset && !reader.EndOfStream)
        {
            await reader.ReadLineAsync(cancellationToken);
            skipped++;
        }

        var lines = new List<string>();
        var bytes = 0;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            var lineBytes = Encoding.UTF8.GetByteCount(line + "\n");
            if (lines.Count > 0 && bytes + lineBytes > chunkBytes)
            {
                break;
            }
            if (lines.Count >= maxLines)
            {
                break;
            }

            lines.Add(line);
            bytes += lineBytes;
        }

        var endLine = lineOffset + lines.Count;
        return new ProgressiveChunkResult(
            string.Join("\n", lines),
            lineOffset,
            endLine,
            lineOffset > 0,
            !reader.EndOfStream,
            bytes,
            lines.Count,
            "progressive",
            !reader.EndOfStream ? "large_file_readonly" : null);
    }

    private static async Task<ProgressiveChunkResult> ReadTailChunkAsync(
        StreamReader reader,
        int maxLines,
        int chunkBytes,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(string Line, int Bytes)>();
        var totalLines = 0;
        var totalBytes = 0;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            var lineBytes = Encoding.UTF8.GetByteCount(line + "\n");
            queue.Enqueue((line, lineBytes));
            totalLines++;
            totalBytes += lineBytes;

            while (queue.Count > maxLines || (queue.Count > 1 && totalBytes > chunkBytes))
            {
                var removed = queue.Dequeue();
                totalBytes -= removed.Bytes;
            }
        }

        var startLine = Math.Max(0, totalLines - queue.Count);
        return new ProgressiveChunkResult(
            string.Join("\n", queue.Select(x => x.Line)),
            startLine,
            totalLines,
            startLine > 0,
            false,
            totalBytes,
            queue.Count,
            "tail",
            "tail_preview");
    }

    private static async Task<string> ReadTextForEditAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        if (stream.Length > FileEditMaxBytes)
        {
            throw new InvalidDataException("file too large for edit mode");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(content) > FileEditMaxBytes)
        {
            throw new InvalidDataException("file too large for edit mode");
        }

        return content;
    }

    private static string NormalizeReadMode(string? mode)
    {
        if (string.Equals(mode, "edit", StringComparison.OrdinalIgnoreCase))
        {
            return "edit";
        }

        return string.Equals(mode, "progressive", StringComparison.OrdinalIgnoreCase) ? "progressive" : "preview";
    }

    private static string NormalizeChunkDirection(string? direction)
    {
        return string.Equals(direction, "tail", StringComparison.OrdinalIgnoreCase) ? "tail" : "forward";
    }

    private static object BuildProgressiveReadPayload(string targetPath, long size, ProgressiveChunkResult chunk)
    {
        return new
        {
            path = targetPath,
            encoding = "utf-8",
            size,
            content = chunk.Content,
            lines_shown = chunk.LoadedLines,
            max_lines = chunk.LoadedLines,
            truncated = chunk.HasMoreBefore || chunk.HasMoreAfter,
            truncate_reason = chunk.TruncateReason,
            byte_limit = DefaultFileChunkBytes,
            chunk_bytes = chunk.LoadedBytes,
            read_only = true,
            large_file = true,
            has_more_before = chunk.HasMoreBefore,
            has_more_after = chunk.HasMoreAfter,
            cursor_start = chunk.StartLine,
            cursor_end = chunk.EndLine,
            loaded_bytes = chunk.LoadedBytes,
            loaded_lines = chunk.LoadedLines,
            mode = chunk.Mode
        };
    }

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        var lines = 1;
        foreach (var ch in content)
        {
            if (ch == '\n')
            {
                lines++;
            }
        }
        return lines;
    }

    private sealed record ProgressiveChunkResult(
        string Content,
        int StartLine,
        int EndLine,
        bool HasMoreBefore,
        bool HasMoreAfter,
        int LoadedBytes,
        int LoadedLines,
        string Mode,
        string? TruncateReason);

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
