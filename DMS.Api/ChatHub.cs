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

    public ChatHub(IUserService users)
    {
        _users = users;
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