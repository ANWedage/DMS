using System.Security.Claims;
using DMS.Models;
using DMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DMS.Api;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IUserService _users;
    private readonly ChatPresenceService _presence;

    public ChatHub(IUserService users, ChatPresenceService presence)
    {
        _users = users;
        _presence = presence;
    }

    public override async Task OnConnectedAsync()
    {
        var identity = GetIdentity();
        if (identity != null)
        {
            _presence.Connect(identity.Value.Id, identity.Value.Role);
            await Clients.All.SendAsync("UserPresenceChanged", identity.Value.Id, identity.Value.Role, true);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var identity = GetIdentity();
        if (identity != null && _presence.Disconnect(identity.Value.Id, identity.Value.Role))
            await Clients.All.SendAsync("UserPresenceChanged", identity.Value.Id, identity.Value.Role, false);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<ChatMessage> SendMessage(string recipientId, string recipientRole, string messageText)
    {
        var identity = GetIdentity();
        var senderId = identity?.Id;
        var senderRole = identity?.Role;
        if (string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(senderRole))
            throw new HubException("Your chat session is invalid.");

        try
        {
            var message = _users.SaveChatMessage(senderId, senderRole, recipientId, recipientRole, messageText);
            await Clients.Users(senderId, recipientId).SendAsync("ReceiveMessage", message);
            return message;
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    private (string Id, string Role)? GetIdentity()
    {
        var id = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");
        var role = Context.User?.FindFirstValue(ClaimTypes.Role);
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(role) ? null : (id, role);
    }
}