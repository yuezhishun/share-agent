using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AcpCliExample
{
    /// <summary>
    /// ACP (Agent Communication Protocol) C# 客户端示例
    /// 演示如何调用支持 ACP 协议的 CLI 工具（如 Goose、Qwen、Augment 等）
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // 配置要调用的 CLI
            var config = new AcpClientConfig
            {
                CliCommand = "goose",           // CLI 命令
                AcpArgs = new[] { "acp" },      // ACP 启动参数
                WorkingDirectory = "/path/to/your/project",
                TimeoutSeconds = 300
            };

            // 创建 ACP 客户端
            using var client = new AcpClient(config);

            // 连接到 CLI
            Console.WriteLine("正在连接到 ACP CLI...");
            await client.ConnectAsync();
            Console.WriteLine("连接成功！\n");

            // 创建会话
            var session = await client.CreateSessionAsync();
            Console.WriteLine($"会话创建成功: {session.SessionId}\n");

            // 发送消息并接收流式响应
            Console.WriteLine("用户: 请帮我创建一个简单的 Hello World 程序\n");
            Console.WriteLine("AI:");

            await client.SendPromptAsync("请帮我创建一个简单的 Hello World 程序", (update) =>
            {
                switch (update.Type)
                {
                    case "agent_message_chunk":
                        Console.Write(update.Content.Text);
                        break;

                    case "tool_call":
                        Console.WriteLine($"\n[工具调用: {update.ToolCall.Title}]");
                        break;

                    case "tool_call_update":
                        if (update.ToolCall.Status == "completed")
                        {
                            Console.WriteLine($"[工具调用完成: {update.ToolCall.Title}]");
                        }
                        break;

                    case "plan":
                        Console.WriteLine("\n[执行计划:]");
                        foreach (var entry in update.Plan.Entries)
                        {
                            Console.WriteLine($"  - {entry.Content} [{entry.Status}]");
                        }
                        break;
                }
            });

            Console.WriteLine("\n\n对话完成，按任意键退出...");
            Console.ReadKey();

            // 断开连接
            await client.DisconnectAsync();
        }
    }

    /// <summary>
    /// ACP 客户端配置
    /// </summary>
    public class AcpClientConfig
    {
        public string CliCommand { get; set; } = "goose";
        public string[] AcpArgs { get; set; } = new[] { "acp" };
        public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
        public int TimeoutSeconds { get; set; } = 300;
        public Dictionary<string, string>? EnvironmentVariables { get; set; }
    }

    /// <summary>
    /// ACP 客户端 - 用于与 CLI 工具通信
    /// </summary>
    public class AcpClient : IDisposable
    {
        private readonly AcpClientConfig _config;
        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;
        private int _requestId = 0;
        private bool _isConnected = false;
        private Task? _readTask;
        private CancellationTokenSource? _cts;

        // 用于等待响应的 TaskCompletionSource
        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();

        public AcpClient(AcpClientConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// 连接到 ACP CLI
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_isConnected) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = _config.CliCommand,
                Arguments = string.Join(" ", _config.AcpArgs),
                WorkingDirectory = _config.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // 添加环境变量
            if (_config.EnvironmentVariables != null)
            {
                foreach (var (key, value) in _config.EnvironmentVariables)
                {
                    startInfo.EnvironmentVariables[key] = value;
                }
            }

            _process = new Process { StartInfo = startInfo };
            _process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine($"[CLI Error] {e.Data}");
                }
            };

            _process.Start();
            _process.BeginErrorReadLine();

            _stdin = new StreamWriter(_process.StandardInput.BaseStream) { AutoFlush = true };
            _stdout = new StreamReader(_process.StandardOutput.BaseStream);

            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));

            // 发送 initialize 请求
            var initResponse = await SendRequestAsync("initialize", new
            {
                protocolVersion = 1,
                clientCapabilities = new
                {
                    fs = new
                    {
                        readTextFile = true,
                        writeTextFile = true
                    }
                }
            });

            Console.WriteLine($"协议初始化成功: {initResponse}");
            _isConnected = true;
        }

        /// <summary>
        /// 创建新会话
        /// </summary>
        public async Task<AcpSession> CreateSessionAsync(string? resumeSessionId = null)
        {
            var parameters = new Dictionary<string, object>
            {
                ["cwd"] = _config.WorkingDirectory,
                ["mcpServers"] = new object[] { }
            };

            if (resumeSessionId != null)
            {
                parameters["resumeSessionId"] = resumeSessionId;
            }

            var response = await SendRequestAsync("session/new", parameters);

            var sessionId = response.GetProperty("sessionId").GetString()
                ?? throw new InvalidOperationException("Session ID not returned");

            return new AcpSession
            {
                SessionId = sessionId,
                Models = ExtractModels(response),
                ConfigOptions = ExtractConfigOptions(response)
            };
        }

        /// <summary>
        /// 发送消息并接收流式响应
        /// </summary>
        public async Task SendPromptAsync(string prompt, Action<AcpSessionUpdate> onUpdate)
        {
            if (!_isConnected) throw new InvalidOperationException("Not connected");

            var sessionId = await GetCurrentSessionIdAsync();

            // 订阅会话更新事件
            EventHandler<AcpSessionUpdate>? handler = null;
            handler = (sender, update) =>
            {
                onUpdate(update);
            };

            SessionUpdateReceived += handler;

            try
            {
                await SendRequestAsync("session/prompt", new
                {
                    sessionId,
                    prompt = new[] { new { type = "text", text = prompt } }
                });

                // 等待响应完成（通过其他机制判断，如超时或收到 end_turn）
                await Task.Delay(100); // 给流式响应一点时间开始
            }
            finally
            {
                SessionUpdateReceived -= handler;
            }
        }

        /// <summary>
        /// 设置模型
        /// </summary>
        public async Task SetModelAsync(string modelId)
        {
            var sessionId = await GetCurrentSessionIdAsync();
            await SendRequestAsync("session/set_model", new { sessionId, modelId });
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            _cts?.Cancel();

            if (_readTask != null)
            {
                try { await _readTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch { /* ignore */ }
            }

            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync();
            }

            _stdin?.Dispose();
            _stdout?.Dispose();
            _process?.Dispose();
            _cts?.Dispose();

            _isConnected = false;
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
        }

        // 事件：收到会话更新
        public event EventHandler<AcpSessionUpdate>? SessionUpdateReceived;

        /// <summary>
        /// 发送 JSON-RPC 请求并等待响应
        /// </summary>
        private async Task<JsonElement> SendRequestAsync(string method, object? parameters = null)
        {
            var id = Interlocked.Increment(ref _requestId);
            var request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Id = id,
                Method = method,
                Params = parameters
            };

            var requestJson = JsonSerializer.Serialize(request);
            var tcs = new TaskCompletionSource<JsonElement>();
            _pendingRequests[id] = tcs;

            await _stdin!.WriteLineAsync(requestJson);

            // 设置超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.TimeoutSeconds));
            await using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }

        /// <summary>
        /// 后台读取循环
        /// </summary>
        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _stdout != null)
                {
                    var line = await _stdout.ReadLineAsync(ct);
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        // 检查是否是响应（有 id 字段）
                        if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number)
                        {
                            var id = idElement.GetInt32();
                            if (_pendingRequests.TryRemove(id, out var tcs))
                            {
                                if (root.TryGetProperty("result", out var result))
                                {
                                    tcs.TrySetResult(result.Clone());
                                }
                                else if (root.TryGetProperty("error", out var error))
                                {
                                    tcs.TrySetException(new Exception($"ACP Error: {error}"));
                                }
                            }
                        }
                        // 检查是否是通知/请求（有 method 字段）
                        else if (root.TryGetProperty("method", out var methodElement))
                        {
                            var method = methodElement.GetString();
                            if (root.TryGetProperty("params", out var paramsElement))
                            {
                                HandleIncomingRequest(method!, paramsElement.Clone());
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"[JSON Parse Error] {ex.Message}: {line}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Read Loop Error] {ex.Message}");
            }
        }

        /// <summary>
        /// 处理来自 CLI 的请求/通知
        /// </summary>
        private void HandleIncomingRequest(string method, JsonElement parameters)
        {
            switch (method)
            {
                case "session/update":
                    var update = ParseSessionUpdate(parameters);
                    if (update != null)
                    {
                        SessionUpdateReceived?.Invoke(this, update);
                    }
                    break;

                case "session/request_permission":
                    HandlePermissionRequest(parameters);
                    break;

                case "fs/read_text_file":
                    HandleFileReadRequest(parameters);
                    break;

                case "fs/write_text_file":
                    HandleFileWriteRequest(parameters);
                    break;

                default:
                    Console.WriteLine($"[Unknown Method] {method}");
                    break;
            }
        }

        /// <summary>
        /// 解析会话更新
        /// </summary>
        private AcpSessionUpdate? ParseSessionUpdate(JsonElement paramsElement)
        {
            if (!paramsElement.TryGetProperty("update", out var updateElement)) return null;
            if (!updateElement.TryGetProperty("sessionUpdate", out var typeElement)) return null;

            var type = typeElement.GetString();
            var update = new AcpSessionUpdate { Type = type! };

            switch (type)
            {
                case "agent_message_chunk":
                    if (updateElement.TryGetProperty("content", out var content))
                    {
                        update.Content = new AcpMessageContent
                        {
                            Text = content.GetProperty("text").GetString()
                        };
                    }
                    break;

                case "tool_call":
                case "tool_call_update":
                    update.ToolCall = new AcpToolCall
                    {
                        ToolCallId = updateElement.GetProperty("toolCallId").GetString()!,
                        Title = updateElement.GetProperty("title").GetString()!,
                        Status = updateElement.GetProperty("status").GetString()!,
                        Kind = updateElement.GetProperty("kind").GetString()!
                    };
                    break;

                case "plan":
                    var entries = new List<AcpPlanEntry>();
                    if (updateElement.TryGetProperty("entries", out var entriesElement))
                    {
                        foreach (var entry in entriesElement.EnumerateArray())
                        {
                            entries.Add(new AcpPlanEntry
                            {
                                Content = entry.GetProperty("content").GetString()!,
                                Status = entry.GetProperty("status").GetString()!
                            });
                        }
                    }
                    update.Plan = new AcpPlan { Entries = entries };
                    break;
            }

            return update;
        }

        /// <summary>
        /// 处理权限请求
        /// </summary>
        private async void HandlePermissionRequest(JsonElement parameters)
        {
            var options = parameters.GetProperty("options").EnumerateArray();
            var toolCall = parameters.GetProperty("toolCall");

            Console.WriteLine($"\n[权限请求] 工具: {toolCall.GetProperty("title").GetString()}");
            foreach (var option in options)
            {
                Console.WriteLine($"  - {option.GetProperty("name").GetString()} ({option.GetProperty("optionId").GetString()})");
            }

            // 自动允许（实际应用中应该询问用户）
            var response = new
            {
                jsonrpc = "2.0",
                id = parameters.GetProperty("id").GetInt32(),
                result = new
                {
                    outcome = new
                    {
                        outcome = "selected",
                        optionId = "allow_once"
                    }
                }
            };

            await _stdin!.WriteLineAsync(JsonSerializer.Serialize(response));
        }

        /// <summary>
        /// 处理文件读取请求
        /// </summary>
        private async void HandleFileReadRequest(JsonElement parameters)
        {
            var path = parameters.GetProperty("path").GetString()!;
            var id = parameters.GetProperty("id").GetInt32();

            try
            {
                var content = await File.ReadAllTextAsync(path);
                var response = new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new { content }
                };
                await _stdin!.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                var response = new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32000, message = ex.Message }
                };
                await _stdin!.WriteLineAsync(JsonSerializer.Serialize(response));
            }
        }

        /// <summary>
        /// 处理文件写入请求
        /// </summary>
        private async void HandleFileWriteRequest(JsonElement parameters)
        {
            var path = parameters.GetProperty("path").GetString()!;
            var content = parameters.GetProperty("content").GetString()!;
            var id = parameters.GetProperty("id").GetInt32();

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(path, content);
                var response = new
                {
                    jsonrpc = "2.0",
                    id,
                    result = (object?)null
                };
                await _stdin!.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                var response = new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32000, message = ex.Message }
                };
                await _stdin!.WriteLineAsync(JsonSerializer.Serialize(response));
            }
        }

        private Task<string> GetCurrentSessionIdAsync()
        {
            // 实际实现中需要跟踪当前会话 ID
            return Task.FromResult("current-session-id");
        }

        private static AcpModelInfo? ExtractModels(JsonElement response)
        {
            // 解析模型信息
            if (!response.TryGetProperty("models", out var models)) return null;
            return new AcpModelInfo();
        }

        private static List<AcpConfigOption> ExtractConfigOptions(JsonElement response)
        {
            var options = new List<AcpConfigOption>();
            if (response.TryGetProperty("configOptions", out var configOptions))
            {
                foreach (var opt in configOptions.EnumerateArray())
                {
                    options.Add(new AcpConfigOption
                    {
                        Id = opt.GetProperty("id").GetString()!,
                        Type = opt.GetProperty("type").GetString()!
                    });
                }
            }
            return options;
        }
    }

    // 数据模型

    public class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        public object? Params { get; set; }
    }

    public class AcpSession
    {
        public string SessionId { get; set; } = "";
        public AcpModelInfo? Models { get; set; }
        public List<AcpConfigOption> ConfigOptions { get; set; } = new();
    }

    public class AcpSessionUpdate
    {
        public string Type { get; set; } = "";
        public AcpMessageContent? Content { get; set; }
        public AcpToolCall? ToolCall { get; set; }
        public AcpPlan? Plan { get; set; }
    }

    public class AcpMessageContent
    {
        public string? Text { get; set; }
        public string? Type { get; set; }
    }

    public class AcpToolCall
    {
        public string ToolCallId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
        public string Kind { get; set; } = "";
    }

    public class AcpPlan
    {
        public List<AcpPlanEntry> Entries { get; set; } = new();
    }

    public class AcpPlanEntry
    {
        public string Content { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class AcpModelInfo
    {
        public string? CurrentModelId { get; set; }
        public List<AcpAvailableModel> AvailableModels { get; set; } = new();
    }

    public class AcpAvailableModel
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
    }

    public class AcpConfigOption
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string? CurrentValue { get; set; }
    }
}
