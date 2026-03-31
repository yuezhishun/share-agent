using Microsoft.AspNetCore.SignalR;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Endpoints;

public sealed class AgentHub : Hub
{
    public async Task Subscribe(AgentGatewayHubHandshakeRequest request)
    {
        var gatewaySessionId = (request.GatewaySessionId ?? string.Empty).Trim();
        if (gatewaySessionId.Length == 0)
        {
            throw new HubException("gatewaySessionId is required");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, gatewaySessionId);
    }

    public async Task Unsubscribe(AgentGatewayHubHandshakeRequest request)
    {
        var gatewaySessionId = (request.GatewaySessionId ?? string.Empty).Trim();
        if (gatewaySessionId.Length == 0)
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, gatewaySessionId);
    }
}
