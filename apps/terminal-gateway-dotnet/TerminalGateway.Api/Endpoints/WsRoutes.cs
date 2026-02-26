using System.Net.WebSockets;
using System.Text;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class WsRoutes
{
    private const int HeartbeatIntervalMs = 20_000;

    public static IEndpointRouteBuilder MapWsRoutes(this IEndpointRouteBuilder app)
    {
        var rawClients = new HashSet<WebSocket>();
        var rawReadyClients = new HashSet<WebSocket>();
        var gate = new object();
        var manager = app.ServiceProvider.GetRequiredService<InstanceManager>();

        app.MapGet("/ws/term", async (HttpContext context, InstanceManager wsManager, CancellationToken ct) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var query = context.Request.Query;
            var instanceId = query["instance_id"].ToString();
            var wantsRaw = query["raw"].ToString() is "1" or "true";

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "missing instance_id", ct);
                return;
            }

            if (!wsManager.AttachClient(instanceId, socket))
            {
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "instance not found", ct);
                return;
            }

            if (wantsRaw)
            {
                lock (gate)
                {
                    rawClients.Add(socket);
                }
            }

            await SendInitialState(wsManager, socket, instanceId, wantsRaw, ct);
            if (wantsRaw)
            {
                lock (gate)
                {
                    rawReadyClients.Add(socket);
                }
            }

            var buffer = new byte[16 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                using var ms = new MemoryStream();
                try
                {
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            goto LOOP_END;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                }
                catch
                {
                    break;
                }

                var payload = Encoding.UTF8.GetString(ms.ToArray());
                var message = WebCliClientMessage.Parse(payload);
                if (message is null)
                {
                    await InstanceManager.SendAsync(socket, new { error = "invalid message" }, CancellationToken.None);
                    continue;
                }

                if (!string.Equals(message.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    await InstanceManager.SendAsync(socket, new { error = "instance mismatch" }, CancellationToken.None);
                    continue;
                }

                switch (message)
                {
                    case WsStdinMessage stdin:
                        wsManager.WriteStdin(instanceId, stdin.Data);
                        break;
                    case WsResizeMessage resize:
                    {
                        var snapshot = wsManager.Resize(instanceId, resize.Cols, resize.Rows);
                        if (snapshot is not null)
                        {
                            await InstanceManager.SendAsync(socket, snapshot, CancellationToken.None);
                        }
                        break;
                    }
                    case WsResyncMessage:
                    {
                        if (wantsRaw)
                        {
                            lock (gate)
                            {
                                rawReadyClients.Remove(socket);
                            }
                        }

                        await SendInitialState(wsManager, socket, instanceId, wantsRaw, CancellationToken.None);
                        if (wantsRaw)
                        {
                            lock (gate)
                            {
                                rawReadyClients.Add(socket);
                            }
                        }
                        break;
                    }
                    case WsHistoryGetMessage history:
                    {
                        var chunk = wsManager.HistoryChunk(instanceId, history.ReqId, history.Before, history.Limit);
                        if (chunk is not null)
                        {
                            await InstanceManager.SendAsync(socket, chunk, CancellationToken.None);
                        }
                        break;
                    }
                    case WsPingMessage ping:
                        await InstanceManager.SendAsync(socket, new { v = 1, type = "pong", instance_id = instanceId, ts = ping.Ts ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, CancellationToken.None);
                        break;
                }
            }

        LOOP_END:
            lock (gate)
            {
                rawClients.Remove(socket);
                rawReadyClients.Remove(socket);
            }

            wsManager.DetachClient(instanceId, socket);
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
        });

        var timer = new System.Threading.Timer(async _ =>
        {
            try
            {
                var instances = manager.List();
                foreach (var instance in instances)
                {
                    var target = manager.Get(instance.Id);
                    if (target is null)
                    {
                        continue;
                    }

                    List<WebSocket> clients;
                    lock (target.Sync)
                    {
                        clients = target.Clients.ToList();
                    }

                    var ping = new { v = 1, type = "ping", instance_id = instance.Id, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                    foreach (var client in clients)
                    {
                        await InstanceManager.SendAsync(client, ping, CancellationToken.None);
                    }
                }
            }
            catch
            {
            }
        }, null, HeartbeatIntervalMs, HeartbeatIntervalMs);

        manager.Raw += async (instanceId, payload) =>
        {
            var state = manager.Get(instanceId);
            if (state is null)
            {
                return;
            }

            List<WebSocket> clients;
            lock (state.Sync)
            {
                clients = state.Clients.ToList();
            }

            foreach (var client in clients)
            {
                var canSend = false;
                lock (gate)
                {
                    canSend = rawClients.Contains(client) && rawReadyClients.Contains(client);
                }

                if (canSend)
                {
                    await InstanceManager.SendAsync(client, payload, CancellationToken.None);
                }
            }
        };

        manager.Exited += async (instanceId, payload) =>
        {
            var state = manager.Get(instanceId);
            if (state is null)
            {
                return;
            }

            List<WebSocket> clients;
            lock (state.Sync)
            {
                clients = state.Clients.ToList();
            }

            foreach (var client in clients)
            {
                await InstanceManager.SendAsync(client, payload, CancellationToken.None);
            }
        };

        app.ServiceProvider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => timer.Dispose());
        return app;
    }

    private static async Task SendInitialState(InstanceManager manager, WebSocket socket, string instanceId, bool prefersRaw, CancellationToken ct)
    {
        if (prefersRaw)
        {
            var replay = manager.RawReplay(instanceId);
            if (!string.IsNullOrEmpty(replay))
            {
                await InstanceManager.SendAsync(socket, new
                {
                    v = 1,
                    type = "term.raw",
                    instance_id = instanceId,
                    node_id = manager.NodeId,
                    node_name = manager.NodeName,
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    replay = true,
                    data = replay
                }, ct);
                return;
            }
        }

        var snapshot = manager.Snapshot(instanceId, advanceSeq: true);
        if (snapshot is not null)
        {
            await InstanceManager.SendAsync(socket, snapshot, ct);
        }
    }
}
