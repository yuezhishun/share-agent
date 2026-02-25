using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class TerminalWebSocketEndpoint
{
    public static IEndpointRouteBuilder MapTerminalWebSocketEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/ws/terminal", async (HttpContext context, SessionManager manager, CancellationToken ct) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var q = context.Request.Query;
            var sessionId = q["sessionId"].ToString();
            var replay = q["replay"].ToString() == "1";
            var replayMode = q["replayMode"].ToString();
            var sinceSeq = int.TryParse(q["sinceSeq"], out var parsedSinceSeq) ? parsedSinceSeq : (int?)null;
            var writeToken = q["writeToken"].ToString();

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                await SessionManager.SendAsync(socket, new { type = "error", code = "SESSION_REQUIRED", message = "sessionId is required" }, ct);
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "missing sessionId", ct);
                return;
            }

            try
            {
                await manager.AttachAsync(sessionId, socket, replay, replayMode, sinceSeq, writeToken, ct);
            }
            catch (Exception ex)
            {
                await SessionManager.SendAsync(socket, new { type = "error", code = "SESSION_NOT_FOUND", message = ex.Message }, ct);
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "session not found", ct);
                return;
            }

            var buffer = new byte[16 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                try
                {
                    var raw = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var msg = JsonSerializer.Deserialize<WsClientMessage>(raw);
                    switch (msg?.Type)
                    {
                        case "input":
                            if (!manager.IsPeerWritable(sessionId, socket))
                            {
                                await SessionManager.SendAsync(socket, new { type = "error", code = "READ_ONLY", message = "session is read-only for this connection" }, CancellationToken.None);
                                break;
                            }

                            await manager.WriteAsync(sessionId, msg.Data ?? string.Empty, CancellationToken.None);
                            break;
                        case "resize":
                            await manager.ResizeAsync(sessionId, msg.Cols, msg.Rows, CancellationToken.None);
                            break;
                        case "ping":
                            await SessionManager.SendAsync(socket, new { type = "pong", ts = msg.Ts ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, CancellationToken.None);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    await SessionManager.SendAsync(socket, new { type = "error", code = "BAD_MESSAGE", message = ex.Message }, CancellationToken.None);
                }
            }

            manager.Detach(sessionId, socket);
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
        });

        return app;
    }
}
