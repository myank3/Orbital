using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Orbital.DTOs;
using Orbital.Service;

namespace Orbital.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly PresenceTracker _presenceTracker;
    private readonly DirectMessageService _dmService;
    private readonly GroupService _groupService;

    public ChatHub(
        PresenceTracker presenceTracker,
        DirectMessageService dmService,
        GroupService groupService)
    {
        _presenceTracker = presenceTracker;
        _dmService = dmService;
        _groupService = groupService;
    }

    private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new HubException("Unauthorized: No user identifier present in token.");

    private string UserHandle => Context.User?.FindFirst("handle")?.Value ?? "anonymous";

    // =========================================================
    // 1. CONNECTION LIFECYCLE & PRESENCE
    // =========================================================
    public override async Task OnConnectedAsync()
    {
        var isFirstConnection = await _presenceTracker.UserConnected(UserId, Context.ConnectionId);

        // Notify all clients that this user just came online
        if (isFirstConnection)
        {
            await Clients.Others.SendAsync("UserStatusChanged", new { userId = UserId, isOnline = true });
        }

        // Send current list of online users to the newly connected client
        var onlineUsers = await _presenceTracker.GetOnlineUsers();
        await Clients.Caller.SendAsync("InitialPresenceList", onlineUsers);

        // Auto-join SignalR groups for all channels the user belongs to
        var userGroups = await _groupService.GetUserGroupsAsync(UserId);
        foreach (var group in userGroups)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"group_{group.Id}");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var isCompletelyOffline = await _presenceTracker.UserDisconnected(UserId, Context.ConnectionId);

        if (isCompletelyOffline)
        {
            await Clients.Others.SendAsync("UserStatusChanged", new { userId = UserId, isOnline = false });
        }

        await base.OnDisconnectedAsync(exception);
    }

    // =========================================================
    // 2. DIRECT MESSAGING (1-ON-1)
    // =========================================================
    public async Task SendDirectMessage(SendDirectMessageDto dto)
    {
        var (success, error, message) = await _dmService.SendMessageAsync(UserId, dto);
        if (!success || message == null)
        {
            throw new HubException(error ?? "Failed to send message.");
        }

        // Deliver to recipient's active sessions
        await Clients.User(dto.RecipientId).SendAsync("ReceiveDirectMessage", message);

        // Echo back to caller's other active tabs/devices
        await Clients.User(UserId).SendAsync("ReceiveDirectMessage", message);
    }

    // =========================================================
    // 3. GROUP MESSAGING & CHANNEL ROOMS
    // =========================================================
    public async Task JoinGroupRoom(int groupId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"group_{groupId}");
    }

    public async Task LeaveGroupRoom(int groupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"group_{groupId}");
    }

    public async Task SendGroupMessage(SendGroupMessageDto dto)
    {
        var (success, error, message) = await _groupService.SendGroupMessageAsync(UserId, dto);
        if (!success || message == null)
        {
            throw new HubException(error ?? "Failed to send group message.");
        }

        // Broadcast to all members subscribed to the group channel
        await Clients.Group($"group_{dto.GroupId}").SendAsync("ReceiveGroupMessage", message);
    }

    // =========================================================
    // 4. REAL-TIME TYPING INDICATORS
    // =========================================================
    public async Task SendDirectTyping(string recipientId, bool isTyping)
    {
        await Clients.User(recipientId).SendAsync("DirectTypingStatus", new
        {
            senderId = UserId,
            senderHandle = UserHandle,
            isTyping
        });
    }

    public async Task SendGroupTyping(int groupId, bool isTyping)
    {
        await Clients.GroupExcept($"group_{groupId}", Context.ConnectionId).SendAsync("GroupTypingStatus", new
        {
            groupId,
            userId = UserId,
            handle = UserHandle,
            isTyping
        });
    }

    // =========================================================
    // 5. WEBRTC SIGNALING (AUDIO / VIDEO PEER-TO-PEER)
    // =========================================================
    public async Task SendCallOffer(string targetUserId, object sdpOffer, Guid sessionId)
    {
        await Clients.User(targetUserId).SendAsync("ReceiveCallOffer", new
        {
            callerId = UserId,
            callerHandle = UserHandle,
            sdp = sdpOffer,
            sessionId
        });
    }

    public async Task SendCallAnswer(string targetUserId, object sdpAnswer, Guid sessionId)
    {
        await Clients.User(targetUserId).SendAsync("ReceiveCallAnswer", new
        {
            responderId = UserId,
            sdp = sdpAnswer,
            sessionId
        });
    }

    public async Task SendIceCandidate(string targetUserId, object iceCandidate, Guid sessionId)
    {
        await Clients.User(targetUserId).SendAsync("ReceiveIceCandidate", new
        {
            senderId = UserId,
            candidate = iceCandidate,
            sessionId
        });
    }

    public async Task RejectOrEndCall(string targetUserId, Guid sessionId, string reason)
    {
        await Clients.User(targetUserId).SendAsync("CallTerminated", new
        {
            terminatedBy = UserId,
            sessionId,
            reason
        });
    }
}