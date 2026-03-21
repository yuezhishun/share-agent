using System.Text.Json;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class ClusterCommandExecutor
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions ClusterJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly GatewayOptions _options;
    private readonly InstanceManager _instances;
    private readonly FileApiService _files;
    private readonly ProcessApiService _processes;

    public ClusterCommandExecutor(GatewayOptions options, InstanceManager instances, FileApiService files, ProcessApiService processes)
    {
        _options = options;
        _instances = instances;
        _files = files;
        _processes = processes;
    }

    public async Task<ClusterCommandResult> ExecuteAsync(ClusterCommandEnvelope command, CancellationToken cancellationToken)
    {
        try
        {
            switch (command.Type)
            {
                case "instance.create":
                {
                    var request = command.Payload.Deserialize<CreateInstanceRequest>(CaseInsensitiveJson) ?? new CreateInstanceRequest();
                    var created = await _instances.CreateAsync(request, _options.FilesBasePath, cancellationToken);
                    return Ok(command, new
                    {
                        instance_id = created.Id,
                        summary = created
                    });
                }
                case "instance.input":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    if (string.IsNullOrWhiteSpace(instanceId) || !_instances.WriteStdin(instanceId, ReadString(command.Payload, "data") ?? string.Empty))
                    {
                        return Fail(command, "instance not found");
                    }

                    return Ok(command, new { ok = true });
                }
                case "instance.resize":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    var cols = ReadInt(command.Payload, "cols");
                    var rows = ReadInt(command.Payload, "rows");
                    if (string.IsNullOrWhiteSpace(instanceId) || _instances.Resize(instanceId, cols, rows) is null)
                    {
                        return Fail(command, "instance not found");
                    }

                    return Ok(command, new { ok = true });
                }
                case "instance.sync":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    if (string.IsNullOrWhiteSpace(instanceId))
                    {
                        return Fail(command, "instance_id is required");
                    }

                    var syncType = (ReadString(command.Payload, "type") ?? "raw").Trim().ToLowerInvariant();
                    if (syncType == "raw")
                    {
                        var reqId = ReadString(command.Payload, "req_id")
                            ?? ReadString(command.Payload, "reqId")
                            ?? $"raw-sync-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                        var sinceSeq = ReadInt(command.Payload, "since_seq");
                        if (sinceSeq <= 0)
                        {
                            sinceSeq = ReadInt(command.Payload, "sinceSeq");
                        }

                        var replay = _instances.RawReplayEvent(instanceId, sinceSeq > 0 ? sinceSeq : null, reqId);
                        if (replay is null)
                        {
                            return Fail(command, "instance not found");
                        }

                        return Ok(command, replay);
                    }

                    if (syncType == "history")
                    {
                        var before = ReadString(command.Payload, "before") ?? "h-1";
                        var limit = Math.Clamp(ReadInt(command.Payload, "limit"), 1, 500);
                        var reqId = ReadString(command.Payload, "req_id") ?? ReadString(command.Payload, "reqId") ?? $"history-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                        var chunk = _instances.HistoryChunk(instanceId, reqId, before, limit);
                        if (chunk is null)
                        {
                            return Fail(command, "instance not found");
                        }

                        return Ok(command, chunk);
                    }

                    var snapshot = _instances.Snapshot(instanceId, advanceSeq: true);
                    if (snapshot is null)
                    {
                        return Fail(command, "instance not found");
                    }

                    return Ok(command, snapshot);
                }
                case "instance.terminate":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    if (string.IsNullOrWhiteSpace(instanceId) || !_instances.Terminate(instanceId))
                    {
                        return Fail(command, "instance not found");
                    }

                    return Ok(command, new { ok = true });
                }
                case "files.upload":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    var targetPath = ReadString(command.Payload, "path");
                    var fileName = ReadString(command.Payload, "file_name") ?? ReadString(command.Payload, "fileName");
                    var contentBase64 = ReadString(command.Payload, "content_base64") ?? ReadString(command.Payload, "contentBase64");
                    if ((string.IsNullOrWhiteSpace(instanceId) && string.IsNullOrWhiteSpace(targetPath))
                        || string.IsNullOrWhiteSpace(fileName)
                        || string.IsNullOrWhiteSpace(contentBase64))
                    {
                        return Fail(command, "invalid upload payload");
                    }

                    byte[] bytes;
                    try
                    {
                        bytes = Convert.FromBase64String(contentBase64);
                    }
                    catch
                    {
                        return Fail(command, "invalid upload content");
                    }

                    object uploaded;
                    if (!string.IsNullOrWhiteSpace(instanceId))
                    {
                        var state = _instances.Get(instanceId);
                        if (state is null)
                        {
                            return Fail(command, "instance not found");
                        }
                        uploaded = await _files.SaveUploadBytesAsync(_options.FilesBasePath, state.Cwd, fileName, bytes, cancellationToken);
                    }
                    else
                    {
                        await using var stream = new MemoryStream(bytes, writable: false);
                        uploaded = await _files.UploadToPathAsync(_options.FilesBasePath, targetPath, fileName, stream, bytes.Length, cancellationToken);
                    }
                    return Ok(command, uploaded);
                }
                case "files.list":
                {
                    var path = ReadString(command.Payload, "path");
                    var showHidden = ReadBool(command.Payload, "show_hidden") || ReadBool(command.Payload, "showHidden");
                    return Ok(command, _files.List(_options.FilesBasePath, path, showHidden));
                }
                case "files.read":
                {
                    var path = ReadString(command.Payload, "path");
                    var maxLines = ReadInt(command.Payload, "max_lines");
                    var chunkBytes = ReadInt(command.Payload, "chunk_bytes");
                    var lineOffset = ReadInt(command.Payload, "line_offset");
                    var mode = ReadString(command.Payload, "mode");
                    var direction = ReadString(command.Payload, "direction");
                    if (maxLines <= 0)
                    {
                        maxLines = ReadInt(command.Payload, "maxLines");
                    }
                    if (chunkBytes <= 0)
                    {
                        chunkBytes = ReadInt(command.Payload, "chunkBytes");
                    }
                    if (lineOffset < 0)
                    {
                        lineOffset = 0;
                    }
                    maxLines = Math.Clamp(maxLines > 0 ? maxLines : _options.FileChunkMaxLines, 1, 5000);
                    chunkBytes = Math.Clamp(chunkBytes > 0 ? chunkBytes : _options.FileChunkBytes, 1, 1024 * 1024);
                    return Ok(command, await _files.ReadAsync(
                        _options.FilesBasePath,
                        path,
                        maxLines,
                        mode,
                        cancellationToken,
                        chunkBytes,
                        lineOffset,
                        direction,
                        _options.LargeFileThresholdBytes));
                }
                case "files.write":
                {
                    var path = ReadString(command.Payload, "path");
                    var content = ReadString(command.Payload, "content") ?? string.Empty;
                    return Ok(command, await _files.WriteAsync(_options.FilesBasePath, path, content, cancellationToken));
                }
                case "files.mkdir":
                {
                    var path = ReadString(command.Payload, "path");
                    var name = ReadString(command.Payload, "name");
                    return Ok(command, _files.CreateDirectory(_options.FilesBasePath, path, name));
                }
                case "files.download":
                {
                    var path = ReadString(command.Payload, "path");
                    var download = _files.OpenDownloadStream(_options.FilesBasePath, path);
                    await using var stream = download.Stream;
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, cancellationToken);
                    return Ok(command, new
                    {
                        name = download.Name,
                        content_type = download.ContentType,
                        enable_range_processing = download.EnableRangeProcessing,
                        content_base64 = Convert.ToBase64String(buffer.ToArray())
                    });
                }
                case "process.run":
                {
                    var request = command.Payload.Deserialize<RunProcessRequest>(CaseInsensitiveJson) ?? new RunProcessRequest();
                    return Ok(command, await _processes.RunAsync(request, cancellationToken));
                }
                case "process.start":
                {
                    var request = command.Payload.Deserialize<RunProcessRequest>(CaseInsensitiveJson) ?? new RunProcessRequest();
                    return Ok(command, await _processes.StartManagedAsync(request, cancellationToken));
                }
                case "process.list":
                    return Ok(command, new { items = _processes.ListManaged() });
                case "process.get":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    return Ok(command, _processes.GetManaged(processId));
                }
                case "process.output":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    return Ok(command, new { items = _processes.GetOutput(processId) });
                }
                case "process.wait":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    var timeoutMs = ReadNullableInt(command.Payload, "timeout_ms") ?? ReadNullableInt(command.Payload, "timeoutMs");
                    return Ok(command, await _processes.WaitManagedAsync(processId, timeoutMs));
                }
                case "process.stop":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    var body = command.Payload.Deserialize<StopManagedProcessRequest>(CaseInsensitiveJson) ?? new StopManagedProcessRequest();
                    return Ok(command, await _processes.StopManagedAsync(processId, body.Force == true));
                }
                case "process.remove":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    return Ok(command, _processes.RemoveManaged(processId));
                }
                default:
                    return Fail(command, $"unsupported command: {command.Type}");
            }
        }
        catch (Exception ex)
        {
            return Fail(command, ex.Message);
        }
    }

    private ClusterCommandResult Ok(ClusterCommandEnvelope command, object payload)
    {
        return new ClusterCommandResult
        {
            CommandId = command.CommandId,
            NodeId = NormalizeTargetNodeId(command),
            SourceNodeId = NormalizeSourceNodeId(command),
            TargetNodeId = NormalizeTargetNodeId(command),
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(payload, ClusterJson)
        };
    }

    private ClusterCommandResult Fail(ClusterCommandEnvelope command, string error)
    {
        return new ClusterCommandResult
        {
            CommandId = command.CommandId,
            NodeId = NormalizeTargetNodeId(command),
            SourceNodeId = NormalizeSourceNodeId(command),
            TargetNodeId = NormalizeTargetNodeId(command),
            Ok = false,
            Error = error,
            Payload = JsonSerializer.SerializeToElement(new { }, ClusterJson)
        };
    }

    private string NormalizeSourceNodeId(ClusterCommandEnvelope command)
    {
        var value = (command.SourceNodeId ?? string.Empty).Trim();
        return value.Length == 0 ? _options.NodeId : value;
    }

    private string NormalizeTargetNodeId(ClusterCommandEnvelope command)
    {
        var value = (command.TargetNodeId ?? command.NodeId ?? string.Empty).Trim();
        return value.Length == 0 ? _options.NodeId : value;
    }

    private static string? ReadString(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && TryGetPropertyInsensitive(payload, name, out var prop)
            && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int ReadInt(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && TryGetPropertyInsensitive(payload, name, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;
    }

    private static int? ReadNullableInt(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && TryGetPropertyInsensitive(payload, name, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }

    private static bool ReadBool(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !TryGetPropertyInsensitive(payload, name, out var prop))
        {
            return false;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => prop.TryGetInt32(out var number) && number != 0,
            JsonValueKind.String => bool.TryParse(prop.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static bool TryGetPropertyInsensitive(JsonElement payload, string name, out JsonElement value)
    {
        if (payload.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
